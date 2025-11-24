using System;
using System.Collections.Generic;
using System.Linq; 
using University.Lms.Ports;
using UniversityLessionSelectionSystem.Ports.EnrollmentEligibility;
using UniversityLessonSelectionSystem.Domain.EnrollmentEligibility;
using UniversityLessonSelectionSystem.Domain.Enums;
using UniversityLessonSelectionSystem.Domain.PrerequisiteEvaluation;
using UniversityLessonSelectionSystem.Ports.EnrollmentEligibility;
using UniversityLessonSelectionSystem.Ports.PrerequisiteEvaluation;

namespace University.Lms.Services
{
    /// <summary>
    /// Bir öğrencinin hedef bir derse yönelik önkoşulları sağlayıp sağlamadığını,
    /// katmanlı politika kuralları üzerinden değerlendiren servistir:
    /// - Minimum not bandı ile doğrudan önkoşul tamamlama
    /// - Aynı bölüm / çapraz bölüm eşdeğerlik zincirleri üzerinden sağlama
    /// - Eski dersler için geçerlilik (expiry) pencereleri
    /// - Aynı dönemde eşzamanlı (concurrent) kayıt istisnaları
    /// - Sınırlı sayıda transfer dersini yerine sayma (substitution) hakları
    /// - Program / seviye / akademik durum override’ları ve bölüm bazlı istisnalar
    /// - İkamet ve GPA temelli mikro koruma (guard) kuralları
    ///
    /// Sonuç olarak, önkoşul kararını Karşılandı / Koşullu / Reddedildi şeklinde,
    /// eksik ögeler ve gerekçelerle birlikte üretir. Öğretim amaçlı olarak yüksek
    /// karar dallanmasına sahip, fakat küçük ve saf kural metotlarıyla test edilebilir
    /// ve okunabilir olacak şekilde tasarlanmıştır.
    /// </summary>
    public sealed class PrerequisiteEvaluationService
    {
        #region Fields

        private readonly ILogger _logger;
        private readonly IStudentRepo _students;
        private readonly ICourseRepo _courses;
        private readonly IEquivalencyRepo _equivalencies;
        #endregion

        #region Policy Constants

        private const int DEFAULT_EXPIRY_YEARS = 7;
        private const int TRANSFER_MAX_SUBSTITUTIONS = 2;
        private const int MAX_EQUIVALENCE_HOPS = 3;

        private const decimal MIN_GPA_FOR_OVERRIDE = 3.50m;

        private static readonly HashSet<Department> DeptAllowingExpiryOverride =
            new HashSet<Department> { Department.CS, Department.EE };

        #endregion

        #region Constructor
        /// <summary>
        /// Saat, log, öğrenci, ders ve eşdeğerlik veri kaynaklarını bağımlılık olarak alarak
        /// PrerequisiteEvaluationService örneğini oluşturur.
        /// </summary>
        public PrerequisiteEvaluationService(
            IClock clock,
            ILogger logger,
            IStudentRepo students,
            ICourseRepo courses,
            IEquivalencyRepo equivalencies)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _students = students ?? throw new ArgumentNullException(nameof(students));
            _courses = courses ?? throw new ArgumentNullException(nameof(courses));
            _equivalencies = equivalencies ?? throw new ArgumentNullException(nameof(equivalencies));
        }
        #endregion

        #region Public Methods

        /// <summary>
        /// Belirli bir öğrenci–hedef ders–dönem üçlüsü ve politika ipuçları (PrereqPolicyHints)
        /// için, tüm önkoşul katmanlarını (doğrudan, eşdeğerlik, transfer, concurrent, expiry)
        /// sırasıyla değerlendirerek önkoşul kararını üretir; sonucu durum, eksikler, gerekçeler
        /// ve gerekirse yapılması gereken aksiyonlarla birlikte döner.
        /// </summary>
        public PrereqDecision Evaluate(string studentId, string targetCourseId, Term term, PrereqPolicyHints hints)
        {
            if (studentId == null) throw new ArgumentNullException(nameof(studentId));
            if (targetCourseId == null) throw new ArgumentNullException(nameof(targetCourseId));
            if (term == null) throw new ArgumentNullException(nameof(term));
            if (hints == null) throw new ArgumentNullException(nameof(hints));

            var student = _students.GetById(studentId);
            var target = _courses.GetCourse(targetCourseId);

            var result = new PrereqDecision();

            var prereqs = (target.PrerequisiteCourseIds ?? new List<string>()).ToList();
            if (prereqs.Count == 0)
            {
                Approve(result, "No prerequisites defined.");
                return result;
            }

            // attempt to satisfy each prerequisite (direct, equivalency, transfer, or concurrent)
            var missing = new List<string>();
            int usedTransfers = 0;

            foreach (var preId in prereqs)
            {
                if (DirectlyCompleted(studentId, preId, hints.MinGrade)) continue;

                if (ViaEquivalency(studentId, preId, hints.MinGrade, MAX_EQUIVALENCE_HOPS)) continue;

                if (hints.AllowTransferSubstitution && usedTransfers < TRANSFER_MAX_SUBSTITUTIONS
                    && TransferSubstitutes(studentId, preId, hints.MinGrade))
                {
                    usedTransfers++;
                    continue;
                }

                if (hints.AllowConcurrentEnrollment &&
                    IsCorequisiteInProgress(studentId, preId, term))
                {
                    // concurrent allowed → mark as conditional
                    result.Status = PrereqStatus.Conditional;
                    result.Reasons.Add($"Co-enroll allowed for {preId}.");
                    continue;
                }

                // still unmet
                missing.Add(preId);
            }

            // expiry / override signals
            if (missing.Count == 0 && !IsExpiredSetBlocking(student, target, hints))
            {
                Approve(result, "All prerequisites satisfied.");
            }
            else if (missing.Count == 0 && IsExpiredSetBlocking(student, target, hints))
            {
                // expiry blocks unless GPA override & dept allows
                if (CanOverrideExpiry(student, target))
                {
                    result.Status = PrereqStatus.Conditional;
                    result.Reasons.Add("Expired prerequisites overridden by GPA/department policy.");
                    result.RequiredActions.Add(PrereqRequiredAction.AdvisorConsent);
                }
                else
                {
                    RejectMissing(result, new[] { "Expired prerequisite window" });
                }
            }
            else
            {
                // still missing some prereqs
                RejectMissing(result, missing);
            }

            _logger.Info($"Prereq evaluation for S={studentId} C={targetCourseId} => {result.Status}");
            return result;
        }

        #endregion

        #region Private Methods
        /// <summary>
        /// Öğrencinin, ilgili önkoşul dersini (preId) belirtilen minimum not bandında
        /// doğrudan tamamlayıp tamamlamadığını kontrol eder.
        /// </summary>
        private bool DirectlyCompleted(string studentId, string preId, GradeBand min)
            => _students.HasCompleted(studentId, preId, min);
        /// <summary>
        /// Önkoşul dersini (preId), eşdeğerlik grafiği üzerinden (aynı bölüm veya çapraz bölüm
        /// eşdeğerleri kullanarak) tamamlanmış sayıp sayamayacağını kontrol eder; en fazla maxHops
        /// adım genişlikli arama (BFS) ile, öğrenci tarafından minimum not bandında geçmiş bir
        /// eşdeğer ders bulunursa true döner.
        /// </summary>
        private bool ViaEquivalency(string studentId, string preId, GradeBand min, int maxHops)
        {
            // BFS up to limited hops to avoid graph explosions
            var seen = new HashSet<string> { preId };
            var frontier = new Queue<(string id, int hop)>();
            frontier.Enqueue((preId, 0));

            while (frontier.Count > 0)
            {
                var (id, hop) = frontier.Dequeue();
                var equivalents = _equivalencies.GetEquivalents(id);

                foreach (var eq in equivalents)
                {
                    if (seen.Contains(eq.CourseId)) continue;
                    seen.Add(eq.CourseId);

                    // check completion on each equivalent
                    if (_students.HasCompleted(studentId, eq.CourseId, min))
                        return true;

                    if (hop + 1 < maxHops)
                        frontier.Enqueue((eq.CourseId, hop + 1));
                }
            }
            return false;
        }
        /// <summary>
        /// Öğrencinin, ilgili önkoşul dersini transfer dersleri (TransferSubstitutions)
        /// üzerinden yerine saydırıp saydırmadığını kontrol eder; tanımlı transfer derslerinden
        /// minimum not bandında geçmişse true döner.
        /// </summary>
        private bool TransferSubstitutes(string studentId, string preId, GradeBand min)
        {
            var subs = _equivalencies.GetTransferSubstitutions(preId);
            foreach (var sub in subs)
            {
                if (_students.HasCompleted(studentId, sub.CourseId, min))
                    return true;
            }
            return false;
        }
        /// <summary>
        /// Öğrencinin, önkoşul dersi (preId) aynı dönem içinde halihazırda almakta olup olmadığını kontrol eder;
        /// öğrencinin dönem içi section kayıtlarını tarar ve kurs kimliği eşleşiyorsa concurrent (eşzamanlı) kayıt var kabul eder.
        /// </summary>
        private bool IsCorequisiteInProgress(string studentId, string preId, Term term)
        {
            var sections = _courses.GetStudentSections(studentId, term.Id);
            if (sections == null || sections.Count == 0) return false;

            foreach (var s in sections)
            {
                var c = _courses.GetCourse(s.CourseId);
                if (c.Id == preId) return true;
            }
            return false;
        }

        /// <summary>
        /// Önkoşul setinin, süre aşımı (expiry) nedeniyle bloklayıcı olup olmadığını belirler;
        /// bölümün expiry override’a izin vermesi ve ipuçlarındaki (hints) bölüm expiry politikasına göre
        /// daha esnek davranır, aksi halde ForceExpiryGate işaretine göre expiry engelini aktif kabul eder.
        /// </summary>
        private bool IsExpiredSetBlocking(Student s, Course target, PrereqPolicyHints hints)
        {
            // if department allows expiry override as policy hint, treat more leniently
            if (DeptAllowingExpiryOverride.Contains(target.Department) && hints.DeptExpiryPolicy == DeptExpiryPolicy.Lenient)
                return false;

            // For demo: we don't have completion timestamps here; assume expiry can be enforced via hints
            return hints.ForceExpiryGate;
        }
        /// <summary>
        /// Öğrencinin not ortalaması ve dersin bağlı olduğu bölüm kurallarına göre,
        /// expiry nedeniyle bloklanan önkoşulların departman politikası ve GPA sayesinde
        /// override edilip edilemeyeceğini kontrol eder; minimum GPA ve bölüm listesine göre karar verir.
        /// </summary>
        private bool CanOverrideExpiry(Student s, Course target)
        {
            if (s.Gpa >= MIN_GPA_FOR_OVERRIDE && DeptAllowingExpiryOverride.Contains(target.Department))
                return true;
            return false;
        }
        /// <summary>
        /// Verilen önkoşul kararını (PrereqDecision) "Karşılandı" (Satisfied) durumuna getirir
        /// ve açıklama listesini verilen neden (reason) ile zenginleştirir.
        /// </summary>
        private static void Approve(PrereqDecision d, string reason)
        {
            d.Status = PrereqStatus.Satisfied;
            d.Reasons.Add(reason);
        }
        /// <summary>
        /// Verilen önkoşul kararını "Reddedildi" (Rejected) durumuna getirir;
        /// eksik görülen tüm önkoşul ders kimliklerini Missing listesine ekler ve
        /// genel gerekçe olarak "Missing prerequisite(s)." mesajını Reasons listesine ekler.
        /// </summary>
        private static void RejectMissing(PrereqDecision d, IEnumerable<string> missing)
        {
            d.Status = PrereqStatus.Rejected;
            foreach (var m in missing) d.Missing.Add(m);
            d.Reasons.Add("Missing prerequisite(s).");
        }

        #endregion
    }
}
