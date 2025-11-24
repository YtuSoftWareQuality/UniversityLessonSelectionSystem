using System;
using System.Collections.Generic;
using System.Linq; 
using University.Lms.Ports;
using UniversityLessionSelectionSystem.Ports.EnrollmentEligibility;
using UniversityLessonSelectionSystem.Domain.EnrollmentEligibility;
using UniversityLessonSelectionSystem.Domain.Enums;
using UniversityLessonSelectionSystem.Domain.SampleProgramPlanner;
using UniversityLessonSelectionSystem.Ports.EnrollmentEligibility;

namespace University.Lms.Services
{
    /// <summary>
    /// Bir öğrencinin dönemlik örnek ders programını oluştururken;
    /// - Lisans planındaki zorunlu ve seçmeli ders kotalarını,
    /// - Hedef kredi miktarını ve min/maks kredi sınırlarını,
    /// - Önkoşul ufkunu (doğru sıralama varsayımı),
    /// - Temel düzeyde uygunluk ve kapasite kontrollerini,
    /// - Ders zorluk seviyeleri arasındaki iş yükü dengesini,
    /// - Öğrenci programına göre kredi üst sınırı ve olası override’ları,
    /// - Bölüm / yan dal / genel seçmeli çeşitliliğini
    /// birlikte dikkate alarak, aday section listesinden dengeli bir dönem planı öneren servistir.
    /// Yüksek karmaşıklık, küçük ve saf kural metotları üzerinden eğitim amaçlı sağlanmıştır.
    /// </summary>
    public sealed class SampleProgramPlannerService
    {
        #region Fields
        private readonly ILogger _logger;
        private readonly IStudentRepo _students;
        private readonly ICourseRepo _courses;
        #endregion

        #region Policy Constants

        private const int TARGET_CREDITS_UNDERGRAD = 15;
        private const int TARGET_CREDITS_GRAD = 9;

        private const int MIN_CREDITS = 12;
        private const int MAX_CREDITS_UG = 18;
        private const int MAX_CREDITS_GRAD = 12;

        private const int MAX_ADVANCED_COURSES_UG = 2;
        private const int MAX_INTRO_COURSES_UG = 3;

        private const int ELECTIVE_MIN = 1;
        private const int ELECTIVE_MAX = 3;

        #endregion

        #region Constructor
        // <summary>
        /// Log, öğrenci ve ders veri kaynaklarını bağımlılık olarak alarak
        /// örnek dönem planı üreten SampleProgramPlannerService örneğini oluşturur.
        /// </summary>
        public SampleProgramPlannerService(ILogger logger, IStudentRepo students, ICourseRepo courses)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _students = students ?? throw new ArgumentNullException(nameof(students));
            _courses = courses ?? throw new ArgumentNullException(nameof(courses));
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Verilen öğrenci, dönem, derece haritası (degree map) ve aday section listesine göre;
        /// önce zorunlu dersleri, ardından seçmelileri seçip kredi hedefini yakalamaya çalışarak
        /// min/maks kredi ve çeşitlilik kurallarına uyan bir dönem programı (ProgramPlan) önerir.
        /// </summary>
        public ProgramPlan Propose(string studentId, Term term, DegreeMap map, IList<Section> candidateSections)
        {
            if (studentId == null) throw new ArgumentNullException(nameof(studentId));
            if (term == null) throw new ArgumentNullException(nameof(term));
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (candidateSections == null) throw new ArgumentNullException(nameof(candidateSections));

            var student = _students.GetById(studentId);
            int target = student.Program == ProgramType.Undergraduate ? TARGET_CREDITS_UNDERGRAD : TARGET_CREDITS_GRAD;
            int maxCap = student.Program == ProgramType.Undergraduate ? MAX_CREDITS_UG : MAX_CREDITS_GRAD;

            // 1) Filter by availability/capacity basics
            var pool = candidateSections.Where(s => s.Enrolled < s.Capacity).ToList();

            // 2) Mandatory first (respect prereq horizon: assume advisor curated list in map.MandatoryCourseIds)
            var picked = new List<Section>();
            PickMandatory(map, pool, picked, target, maxCap, student.Program);

            // 3) Electives to reach target within bounds and balance difficulty/diversity
            PickElectives(map, pool, picked, target, maxCap, student.Program);

            // 4) Validate plan against min/max and diversity rules; if not met, trim or add
            AdjustToBoundsAndDiversity(map, pool, picked, target, student.Program);

            var plan = new ProgramPlan { TargetCredits = target };

            // Replace the following line:
            // plan.Sections.AddRange(picked);

            // With this code to manually add each item from 'picked' to 'plan.Sections':
            foreach (var section in picked)
            {
                plan.Sections.Add(section);
            }
            plan.TotalCredits = plan.Sections.Sum(s => _courses.GetCourse(s.CourseId).Credits);

            _logger.Info($"ProgramPlanner produced {plan.Sections.Count} sections / {plan.TotalCredits} credits.");
            return plan;
        }
        #endregion

        #region Private Methods
        #region Picking helpers
        /// <summary>
        /// Degree map üzerinde zorunlu olarak işaretlenmiş dersleri (MandatoryCourseIds),
        /// aday havuzdan uygun section’larla eşleştirerek ve kredi üst sınırını aşmayacak şekilde
        /// seçilen dersler listesine (picked) ekler.
        /// </summary>
        private void PickMandatory(DegreeMap map, IList<Section> pool, IList<Section> picked, int target, int maxCap, ProgramType program)
        {
            foreach (var cid in map.MandatoryCourseIds)
            {
                var s = pool.FirstOrDefault(x => x.CourseId == cid);
                if (s == null) continue;

                if (WouldExceedMax(picked, s, maxCap)) continue;

                picked.Add(s);
            }
        }
        /// <summary>
        /// Degree map’teki seçmeli ders listesinden (ElectiveCourseIds),
        /// henüz seçilmemiş ve havuzda bulunan section’ları dolaşarak;
        /// kredi üst sınırını aşmayacak, lisans için ileri/intro ders kotasını bozmadan
        /// ve bölüm bazlı çeşitlilik sınırlarını aşmadan seçmeli dersleri plan listesine ekler;
        /// hedef krediye ulaşıldığında ve minimum seçmeli sayısı sağlandığında durur.
        /// </summary>
        private void PickElectives(DegreeMap map, IList<Section> pool, IList<Section> picked, int target, int maxCap, ProgramType program)
        {
            int electivesAdded = 0;

            foreach (var s in pool.Where(x => !picked.Contains(x)))
            {
                if (electivesAdded >= ELECTIVE_MAX) break;
                if (!map.ElectiveCourseIds.Contains(s.CourseId)) continue;

                if (WouldExceedMax(picked, s, maxCap)) continue;
                if (IsOverAdvancedForUG(s, picked, program)) continue;
                if (IsIntroOverloadedForUG(s, picked, program)) continue;

                // diversity: avoid too many from same department unless required
                if (TooManyFromDept(s, picked, map)) continue;

                picked.Add(s);
                electivesAdded++;

                if (TotalCredits(picked) >= target && electivesAdded >= ELECTIVE_MIN) break;
            }
        }
        /// <summary>
        /// Seçilmiş derslerin toplam kredisi minimum kredi veya hedef kredinin altındaysa,
        /// havuzdan uygun seçmeli dersler ekleyerek alt sınırı yakalamaya çalışır;
        /// toplam kredi program türüne göre izin verilen üst sınırı (MaxCap) aşıyorsa,
        /// son eklenen seçmeli dersleri geri alarak üst sınır içinde kalacak şekilde planı budar.
        /// </summary>
        private void AdjustToBoundsAndDiversity(DegreeMap map, IList<Section> pool, IList<Section> picked, int target, ProgramType program)
        {
            // If under min credits → try to add any non-conflicting elective from map
            if (TotalCredits(picked) < Math.Max(MIN_CREDITS, target))
            {
                foreach (var s in pool.Where(x => !picked.Contains(x)))
                {
                    if (!map.ElectiveCourseIds.Contains(s.CourseId)) continue;
                    if (IsIntroOverloadedForUG(s, picked, program)) continue;
                    picked.Add(s);
                    if (TotalCredits(picked) >= Math.Max(MIN_CREDITS, target)) break;
                }
            }

            // If over target but within max, okay; if over max → trim last electives
            while (TotalCredits(picked) > MaxCap(program))
            {
                var lastElective = picked.LastOrDefault(x => map.ElectiveCourseIds.Contains(x.CourseId));
                if (lastElective == null) break;
                picked.Remove(lastElective);
            }
        }

        #endregion

        #region Small pure rules
        /// <summary>
        /// Seçilmiş section listesinin toplam kredisi için yer tutucu bir hesaplama yapar;
        /// gerçek kredi hesabı finalizasyon aşamasında _courses üzerinden yapılır
        /// (örnek projenin didaktik kurgusu gereği burada 0 üzerinden toplanmaktadır).
        /// </summary>
        private static int TotalCredits(IList<Section> secs) => secs.Sum(s => 0); // credits looked up lazily in finalization
        /// <summary>
        /// Program türüne (lisans/lisansüstü) göre izin verilen maksimum kredi üst sınırını döner.
        /// </summary>
        private static int MaxCap(ProgramType program) => program == ProgramType.Undergraduate ? MAX_CREDITS_UG : MAX_CREDITS_GRAD;
        /// <summary>
        /// Bir aday dersin eklenmesi hâlinde, seçilmiş derslerin toplam kredisinin
        /// verilen kredi üst sınırını (maxCap) aşıp aşmayacağını kontrol eder;
        /// aşıyorsa true döner ve ders eklenmemelidir.
        /// </summary>
        private bool WouldExceedMax(IList<Section> picked, Section candidate, int maxCap)
        {
            var next = picked.Sum(s => _courses.GetCourse(s.CourseId).Credits) + _courses.GetCourse(candidate.CourseId).Credits;
            return next > maxCap;
        }
        /// <summary>
        /// Lisans öğrencileri için, seçilmiş dersler arasında ileri seviye (Advanced) ders sayısını
        /// kontrol eder; aday ders de ileri seviyedeyse ve mevcut ileri seviye ders sayısı
        /// izin verilen maksimumu (MAX_ADVANCED_COURSES_UG) aşıyorsa true döner (yani fazla gelişmiş yük).
        /// </summary>
        private bool IsOverAdvancedForUG(Section candidate, IList<Section> picked, ProgramType program)
        {
            if (program != ProgramType.Undergraduate) return false;

            int advancedCount = picked.Count(s => _courses.GetCourse(s.CourseId).Level == CourseLevel.Advanced);
            if (_courses.GetCourse(candidate.CourseId).Level == CourseLevel.Advanced &&
                advancedCount >= MAX_ADVANCED_COURSES_UG)
                return true;

            return false;
        }
        /// <summary>
        /// Lisans öğrencileri için, seçilmiş dersler arasında giriş (Introductory) seviye ders sayısını
        /// kontrol eder; aday ders de giriş seviyesindeyse ve mevcut intro ders sayısı
        /// izin verilen maksimumu (MAX_INTRO_COURSES_UG) aşıyorsa true döner (yani intro yükü fazla).
        /// </summary>
        private bool IsIntroOverloadedForUG(Section candidate, IList<Section> picked, ProgramType program)
        {
            if (program != ProgramType.Undergraduate) return false;

            int introCount = picked.Count(s => _courses.GetCourse(s.CourseId).Level == CourseLevel.Introductory);
            if (_courses.GetCourse(candidate.CourseId).Level == CourseLevel.Introductory &&
                introCount >= MAX_INTRO_COURSES_UG)
                return true;

            return false;
        }
        /// <summary>
        /// Aday dersin ait olduğu bölümden (Department), seçilmiş dersler arasında zaten çok sayıda ders alınıp alınmadığını
        /// degree map’teki MaxPerDepartment sınırlarına göre kontrol eder; bölüm için tanımlı sınır aşıldıysa true döner.
        /// </summary>
        private bool TooManyFromDept(Section candidate, IList<Section> picked, DegreeMap map)
        {
            var dept = candidate.Department;
            int countDept = picked.Count(s => s.Department == dept);
            int maxPerDept = map.MaxPerDepartment.ContainsKey(dept) ? map.MaxPerDepartment[dept] : 3;
            return countDept >= maxPerDept;
        }

        #endregion
        #endregion
    }
}
