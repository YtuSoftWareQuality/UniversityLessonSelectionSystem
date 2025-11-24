using System;
using System.Collections.Generic;
using System.Linq; 
using University.Lms.Ports;
using UniversityLessionSelectionSystem.Ports.EnrollmentEligibility;
using UniversityLessonSelectionSystem.Domain.EnrollmentEligibility;
using UniversityLessonSelectionSystem.Domain.Enums;
using UniversityLessonSelectionSystem.Ports.EnrollmentEligibility;

namespace University.Lms.Services
{
    /// <summary>
    /// Bir öğrencinin belirli bir dönemde, belirli bir section'a kayıt olup olamayacağını
    /// dönem fazı, öğrenci durumu/engelleri, bölüm ve program kuralları, önkoşul/ortak koşul/eşdeğerlikler,
    /// kapasite ve öncelik katmanları (sporcu, onur, burslu), ikamet ve program kısıtları ile
    /// not ortalığı ve kredi yüküne bağlı overload politikalarını ardışık katmanlar halinde değerlendirerek
    /// onay, ret, bekleme listesi veya koşullu onay şeklinde ayrıntılı bir uygunluk kararı üreten servistir.
    /// Öğrencinin bir derse kaydolmaya uygun olup olmadığını önkoşul, kontenjan ve zaman çakışmalarına göre belirler.
    /// </summary> 
    public sealed class EnrollmentEligibilityService
    {
        #region Fields
        private readonly IClock _clock;
        private readonly ILogger _logger;
        private readonly IStudentRepo _students;
        private readonly ICourseRepo _courses;
        #endregion

        #region Policy Constants

        private const decimal MIN_GPA_FOR_OVERLOAD = 3.30m;
        private const int MAX_CREDITS_UNDERGRAD = 18;
        private const int MAX_CREDITS_GRAD = 12;
        private const int OVERLOAD_EXTRA_CREDITS = 3;

        private const int PREREQ_LOOKBACK_YEARS = 7; // documentary only; repo abstracts time
        private const int MAX_WAIT_BEFORE_CONSENT_MINUTES = 60; // demonstrative guard

        private const int SEAT_SOFT_CAP_BUFFER = 2; // near-full soft edge

        private static readonly HashSet<Department> DeptNeedingAdvisor = new HashSet<Department> { Department.LAW, Department.BUS };

        #endregion

        #region Constructor
        /// <summary>
        /// Uygunluk değerlendirmesinde kullanılacak saat, log, öğrenci ve ders veri kaynaklarını
        /// bağımlılık olarak alarak EnrollmentEligibilityService örneğini oluşturur.
        /// </summary>
        public EnrollmentEligibilityService(IClock clock, ILogger logger, IStudentRepo students, ICourseRepo courses)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _students = students ?? throw new ArgumentNullException(nameof(students));
            _courses = courses ?? throw new ArgumentNullException(nameof(courses));
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Öğrenci kimliği, section kimliği ve dönem bilgisine göre tüm karar katmanlarını sırayla çalıştırarak,
        /// ortaya çıkan uygunluk kararını (onay/ret/bekleme listesi/koşullu) nedenler ve gerekli aksiyonlarla birlikte döner.
        /// </summary>
        public EligibilityDecision Evaluate(string studentId, string sectionId, Term term)
        {
            if (studentId == null) throw new ArgumentNullException(nameof(studentId));
            if (sectionId == null) throw new ArgumentNullException(nameof(sectionId));
            if (term == null) throw new ArgumentNullException(nameof(term));

            var student = _students.GetById(studentId);
            var section = _courses.GetSection(sectionId);
            var course = _courses.GetCourse(section.CourseId);

            var decision = new EligibilityDecision();

            if (!PassesTermPhase(term, decision)) return Finalize(decision);
            if (!PassesStandingAndHolds(student, decision)) return Finalize(decision);
            if (!PassesDepartmentAdvisorRule(student, section, decision)) return Finalize(decision);
            if (!PassesLevelAndProgram(student, course, decision)) return Finalize(decision);

            if (!PassesPrerequisites(student, course, decision)) return Finalize(decision);
            if (!PassesCorequisites(student, course, term, decision)) return Finalize(decision);

            if (!PassesConsent(section, student, decision)) return Finalize(decision);
            if (!PassesCapacity(section, student, decision)) return Finalize(decision);

            if (!PassesCreditLoad(student, term, course, decision)) return Finalize(decision);

            decision.Outcome = ApprovalOutcome.Approved;
            decision.Reasons.Add("All policy layers satisfied.");
            _logger.Info($"Enrollment approved for Student={student.Id} Section={section.Id}");
            return decision;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Dönemin fazına (Registration, AddDrop, PreRegistration, Finals, Closed) göre
        /// kayıt penceresinin açık olup olmadığını kontrol eder; kapalı veya finals fazında ise kararı reddeder,
        /// pre-registration fazı çok uzadıysa danışman onayı gerekebileceğine dair uyarı ekler.
        /// </summary>
        private bool PassesTermPhase(Term term, EligibilityDecision d)
        {
            if (term.Phase == TermPhase.Closed || term.Phase == TermPhase.Finals)
            {
                d.Outcome = ApprovalOutcome.Rejected;
                d.Reasons.Add("Term is not open for enrollment.");
                return false;
            }

            if (term.Phase == TermPhase.PreRegistration && _clock.UtcNow > term.UtcNowSnapshot.AddMinutes(MAX_WAIT_BEFORE_CONSENT_MINUTES))
            {
                // example time-based guard branch
                d.Warnings.Add("Pre-registration window aging; advisor consent may be required.");
            }

            return true;
        }
        /// <summary>
        /// Öğrencinin akademik durumu (standing) ve üzerinde finansal/disiplin engeli (hold) olup olmadığını kontrol eder;
        /// uzaklaştırma (suspension) veya aktif hold varsa kararı reddeder ve ilgili nedeni ekler.
        /// </summary>
        private bool PassesStandingAndHolds(Student s, EligibilityDecision d)
        {
            if (s.Standing == StudentStanding.Suspension)
            {
                d.Outcome = ApprovalOutcome.Rejected;
                d.Reasons.Add("Student is suspended.");
                return false;
            }
            if (s.HasFinancialHold || s.HasDisciplinaryHold)
            {
                d.Outcome = ApprovalOutcome.Rejected;
                d.Reasons.Add("Student has active holds.");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Section'ın bağlı olduğu bölümün danışman onayı gerektiren bölümler setinde olup olmadığını,
        /// section seviyesinde danışman onayı şartı bulunup bulunmadığını ve öğrencinin bu onayı alıp almadığını kontrol eder;
        /// şartlı olup onay yoksa kararı koşulluya çevirir ve danışman onayı aksiyonunu ekler.
        /// </summary>
        private bool PassesDepartmentAdvisorRule(Student s, Section sec, EligibilityDecision d)
        {
            if (DeptNeedingAdvisor.Contains(sec.Department) && sec.RequiresAdvisorApproval)
            {
                if (!_students.HasAdvisorApproval(s.Id, sec.Id))
                {
                    d.Outcome = ApprovalOutcome.Conditional;
                    d.Reasons.Add("Advisor approval required.");
                    d.RequiredActions.Add(RequiredAction.AdvisorApproval);
                    return false;
                }
            }
            return true;
        }
        /// <summary>
        /// Ders seviyesini (CourseLevel) ve öğrencinin program türünü (ProgramType) birlikte değerlendirerek,
        /// lisans öğrencisinin sadece lisansüstü ders almasını engeller, bazı özel kombinasyonlarda da uyarı üretir
        /// ancak kararı reddetmeden devam eder.
        /// </summary>
        private bool PassesLevelAndProgram(Student s, Course c, EligibilityDecision d)
        {
            if (c.Level == CourseLevel.GraduateOnly && s.Program == ProgramType.Undergraduate)
            {
                d.Outcome = ApprovalOutcome.Rejected;
                d.Reasons.Add("Graduate-only course cannot be taken by undergraduates.");
                return false;
            }
            if (s.Program == ProgramType.Executive && c.Level == CourseLevel.Introductory)
            {
                d.Warnings.Add("Executive program selecting introductory course.");
            }

            return true;
        }
        /// <summary>
        /// Dersin önkoşul ders listesine bakarak, öğrencinin bu dersleri en az belirlenen not bandında
        /// (örneğin C ve üzeri) başarıyla tamamlayıp tamamlamadığını kontrol eder; eksik önkoşulları listeye ekler
        /// ve herhangi bir önkoşul eksikse kararı reddeder.
        /// </summary>
        private bool PassesPrerequisites(Student s, Course c, EligibilityDecision d)
        {
            if (c.PrerequisiteCourseIds == null || c.PrerequisiteCourseIds.Count == 0) return true;

            bool allMet = true;
            foreach (var pre in c.PrerequisiteCourseIds)
            {
                if (!_students.HasCompleted(s.Id, pre, GradeBand.C))
                {
                    allMet = false;
                    d.MissingPrereqs.Add(pre);
                }
            }

            if (!allMet)
            {
                d.Outcome = ApprovalOutcome.Rejected;
                d.Reasons.Add("Missing prerequisite(s).");
                return false;
            }

            return true;
        }
        /// <summary>
        /// Dersin ortak koşul (corequisite) derslerini, öğrencinin ilgili dönemde kayıtlı olduğu derslerle karşılaştırır;
        /// gerekli ortak ders(ler) kayıtlı değilse kararı koşulluya çevirir ve ortak ders ekleme aksiyonunu ekler.
        /// </summary>
        private bool PassesCorequisites(Student s, Course c, Term term, EligibilityDecision d)
        {
            if (c.CorequisiteCourseIds == null || c.CorequisiteCourseIds.Count == 0) return true;

            var current = _courses.GetStudentSections(s.Id, term.Id)
                                  .Select(ss => _courses.GetCourse(ss.CourseId).Id)
                                  .ToHashSet();

            bool ok = c.CorequisiteCourseIds.All(cr => current.Contains(cr));
            if (!ok)
            {
                d.Outcome = ApprovalOutcome.Conditional;
                d.Reasons.Add("Co-requisite required.");
                d.RequiredActions.Add(RequiredAction.AddCorequisite);
                return false;
            }
            return true;
        }
        /// <summary>
        /// Section için danışman onayı gerekliliğini ve öğrencinin bu onaya sahip olup olmadığını kontrol eder;
        /// onay bekleniyorsa kararı koşulluya çevirir. Çapraz listelenmiş (cross-listed) ve onur öncelikli section'larda
        /// lisansüstü program öğrencisi için kapasite sıkışıklığına dair uyarı ekler.
        /// </summary>
        private bool PassesConsent(Section sec, Student s, EligibilityDecision d)
        {
            if (sec.RequiresAdvisorApproval && !_students.HasAdvisorApproval(s.Id, sec.Id))
            {
                d.Outcome = ApprovalOutcome.Conditional;
                d.Reasons.Add("Advisor approval pending.");
                d.RequiredActions.Add(RequiredAction.AdvisorApproval);
                return false;
            }

            if (sec.IsCrossListed && s.Program == ProgramType.Graduate && sec.PriorityTier == PriorityTier.Honors)
            {
                d.Warnings.Add("Cross-listed with priority to Honors; enrollment may later be rescinded if capacity tightens.");
            }

            return true;
        }
        /// <summary>
        /// Section'ın toplam kapasitesi ile mevcut kayıt sayısını karşılaştırarak boş koltuk olup olmadığını kontrol eder;
        /// hiç koltuk yoksa öğrenciyi otomatik bekleme listesine taşıyan bir karar üretir.
        /// Kalan koltuk sayısı yumuşak tampon eşiğinin altındaysa ve öğrenci sporcu olup section da sporcu öncelikli ise
        /// sporcu önceliği uygulandığına dair uyarı ekler.
        /// </summary>
        private bool PassesCapacity(Section sec, Student s, EligibilityDecision d)
        {
            int remaining = sec.Capacity - sec.Enrolled;
            if (remaining <= 0)
            {
                d.Outcome = ApprovalOutcome.Waitlist;
                d.Reasons.Add("No seats. Placed on waitlist.");
                d.RequiredActions.Add(RequiredAction.AutoWaitlist);
                return false;
            }

            if (remaining <= SEAT_SOFT_CAP_BUFFER && s.IsAthlete && sec.PriorityTier == PriorityTier.Athlete)
            {
                d.Warnings.Add("Seat near soft cap; athlete priority applied.");
            }

            return true;
        }
        /// <summary>
        /// Öğrencinin mevcut dönem kredi yükünü ve almak istediği dersin kredisini toplayarak
        /// lisans/lisansüstü için tanımlı maksimum kredi kapasitesini aşıp aşmadığını kontrol eder;
        /// aşmıyorsa devam eder, aşıyorsa not ortalaması ve dönem fazına göre overload politikasını
        /// koşullu onay ile uygulamaya çalışır, koşullar sağlanmıyorsa kredi yükü gerekçesiyle kararı reddeder.
        /// </summary>
        private bool PassesCreditLoad(Student s, Term t, Course c, EligibilityDecision d)
        {
            int current = _students.CurrentCreditLoad(s.Id, t.Id);
            int capacity = s.Program == ProgramType.Undergraduate ? MAX_CREDITS_UNDERGRAD : MAX_CREDITS_GRAD;

            if (current + c.Credits <= capacity) return true;

            // overload branch
            if (s.Gpa >= MIN_GPA_FOR_OVERLOAD && t.Phase == TermPhase.AddDrop)
            {
                if (current + c.Credits <= capacity + OVERLOAD_EXTRA_CREDITS)
                {
                    d.Outcome = ApprovalOutcome.Conditional;
                    d.Reasons.Add("Overload allowed based on GPA.");
                    d.RequiredActions.Add(RequiredAction.OverloadForm);
                    return false;
                }
            }

            d.Outcome = ApprovalOutcome.Rejected;
            d.Reasons.Add("Credit load exceeds limit.");
            return false;
        }
        /// <summary>
        /// Uygunluk kararı henüz bir sonuca bağlanmamışsa (Outcome = None),
        /// güvenli tarafta kalmak için sonucu reddedilmiş (Rejected) olarak ayarlar ve kararı döner.
        /// Zaten bir sonuç varsa olduğu gibi geri döner.
        /// </summary>
        private static EligibilityDecision Finalize(EligibilityDecision d)
        {
            if (d.Outcome == ApprovalOutcome.None) d.Outcome = ApprovalOutcome.Rejected;
            return d;
        }

        #endregion
    }
}
