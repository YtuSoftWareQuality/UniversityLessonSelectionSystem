using System;
using System.Collections.Generic;
using System.Linq;
using University.Lms.Ports;
using UniversityLessionSelectionSystem.Domain;
using UniversityLessionSelectionSystem.Ports.EnrollmentEligibility; 
using UniversityLessonSelectionSystem.Domain.Enums;
using UniversityLessonSelectionSystem.Domain.SampleProgramPlanner;
using UniversityLessonSelectionSystem.Ports.EnrollmentEligibility;
using UniversityLessonSelectionSystem.Ports.PrerequisiteEvaluation;

namespace University.Lms.Services
{
    /// <summary>
    /// Öğrencinin degree map/derece planına göre mezuniyet ilerlemesini denetler:
    /// - Zorunlu derslerin tamamlama oranı,
    /// - Seçmeli ders sayısı ve kredi dengesi,
    /// - Üst seviye (advanced) ders oranı,
    /// - Tekrar edilen ders yükü ve transfer ders payı,
    /// - Kalan dönem sayısı tahmini ve risk bandı.
    /// 
    /// Sonuçta DegreeProgressReport ile eksik dersleri, risk bandını ve tavsiye edilen aksiyonları üretir.
    /// </summary>
    public sealed class DegreeProgressAuditService
    {
        #region Fields

        private readonly ILogger _logger;
        private readonly IStudentRepo _students;
        private readonly ICourseRepo _courses;
        private readonly IEquivalencyRepo _equivalencies;

        #endregion

        #region Policy Constants

        private const decimal MANDATORY_OK_RATIO = 0.75m;
        private const decimal ELECTIVE_OK_RATIO = 0.50m;
        private const decimal ADVANCED_MIN_RATIO = 0.30m;

        private const int MAX_REPEAT_COURSES = 4;
        private const int MAX_TRANSFER_CREDITS = 60;

        private const int STANDARD_TOTAL_CREDITS = 120;

        #endregion

        #region Constructor

        public DegreeProgressAuditService(
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

        #region Public API

        /// <summary>
        /// Öğrencinin degree map'e göre ilerlemesini analiz eder ve dereceli bir progress raporu döner.
        /// </summary>
        public DegreeProgressReport Audit(DegreeMap map, DegreeProgressContext ctx)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            var student = _students.GetById(ctx.StudentId);

            var report = new DegreeProgressReport
            {
                StudentId = ctx.StudentId,
                Program = student.Program
            };

            var completedCourses = ctx.CompletedCourses.Select(c => c.CourseId).ToHashSet();

            AnalyzeMandatory(map, ctx, completedCourses, report);
            AnalyzeElectives(map, ctx, completedCourses, report);
            AnalyzeAdvancedBalance(map, ctx, completedCourses, report);
            AnalyzeRepeatsAndTransfers(ctx, report);
            EstimateRemainingTerms(map, ctx, report);

            _logger.Info($"DegreeProgressAudit for {ctx.StudentId}: RiskBand={report.RiskBand}, MissingMandatories={report.MissingMandatory.Count}");
            return report;
        }

        #endregion

        #region Private - Analyses
        /// <summary>
        /// Degree map üzerindeki tüm zorunlu (mandatory) dersler için,
        /// tamamlanma durumunu ve oranını hesaplar; eksik dersleri rapora ekler
        /// ve oran belirlenen eşik altında ise risk bandını yükseltir.
        /// </summary>
        private void AnalyzeMandatory(
            DegreeMap map,
            DegreeProgressContext ctx,
            HashSet<string> completed,
            DegreeProgressReport report)
        {
            int totalMandatory = map.MandatoryCourseIds.Count;
            int completedMandatory = 0;

            foreach (var cid in map.MandatoryCourseIds)
            {
                if (completed.Contains(cid) || HasEquivalentCompletion(ctx, cid))
                {
                    completedMandatory++;
                    report.CompletedMandatory.Add(cid);
                }
                else
                {
                    report.MissingMandatory.Add(cid);
                }
            }

            report.MandatoryCompletionRatio = totalMandatory == 0
                ? 1.0m
                : (decimal)completedMandatory / totalMandatory;

            if (report.MandatoryCompletionRatio < MANDATORY_OK_RATIO)
            {
                ElevateRisk(report, DegreeProgressRiskBand.High);
                report.Recommendations.Add("Core mandatory completion below target.");
            }
        }
        /// <summary>
        /// Degree map’teki seçmeli dersler için tamamlanma sayısını ve oranını
        /// hesaplar; seçmeli tamamlama oranı hedefin altındaysa rapora uyarı notları
        /// ekler ve risk bandını Medium seviyesine yükseltebilir.
        /// </summary>
        private void AnalyzeElectives(
            DegreeMap map,
            DegreeProgressContext ctx,
            HashSet<string> completed,
            DegreeProgressReport report)
        {
            int requiredElectives = map.ElectiveCourseIds.Count == 0
                ? ctx.RequiredElectiveCount
                : map.ElectiveCourseIds.Count;

            int completedElectives = 0;

            foreach (var cid in map.ElectiveCourseIds)
            {
                if (completed.Contains(cid) || HasEquivalentCompletion(ctx, cid))
                {
                    completedElectives++;
                    report.CompletedElectives.Add(cid);
                }
            }

            report.ElectiveCompletionRatio = requiredElectives == 0
                ? 1.0m
                : (decimal)completedElectives / requiredElectives;

            if (report.ElectiveCompletionRatio < ELECTIVE_OK_RATIO)
            {
                ElevateRisk(report, DegreeProgressRiskBand.Medium);
                report.Recommendations.Add("Elective completion below target.");
            }
        }
        /// <summary>
        /// Zorunlu ve seçmeli dersler içindeki advanced (üst seviye) dersleri tarar,
        /// ne kadarının tamamlandığını hesaplar; advanced oranı minimum eşiğin
        /// altındaysa risk bandını yükseltir ve dengeyi düzeltmek için tavsiye ekler.
        /// </summary>
        private void AnalyzeAdvancedBalance(
            DegreeMap map,
            DegreeProgressContext ctx,
            HashSet<string> completed,
            DegreeProgressReport report)
        {
            int advancedTotal = 0;
            int advancedCompleted = 0;

            foreach (var cid in map.MandatoryCourseIds.Concat(map.ElectiveCourseIds))
            {
                var course = _courses.GetCourse(cid);
                if (course.Level == CourseLevel.Advanced)
                {
                    advancedTotal++;
                    if (completed.Contains(cid) || HasEquivalentCompletion(ctx, cid))
                    {
                        advancedCompleted++;
                    }
                }
            }

            report.AdvancedCompletionRatio = advancedTotal == 0
                ? 1.0m
                : (decimal)advancedCompleted / advancedTotal;

            if (report.AdvancedCompletionRatio < ADVANCED_MIN_RATIO)
            {
                ElevateRisk(report, DegreeProgressRiskBand.Medium);
                report.Recommendations.Add("Advanced/upper-level course share below minimum.");
            }
        }
        /// <summary>
        /// Tekrar edilen ders sayısı ve transfer kredi miktarını kontrol eder;
        /// program politikalarını aşan tekrar veya transfer yükü varsa risk bandını
        /// yükseltir ve ilgili uyarı/tavsiye mesajlarını rapora ekler.
        /// </summary>
        private void AnalyzeRepeatsAndTransfers(DegreeProgressContext ctx, DegreeProgressReport report)
        {
            if (ctx.RepeatedCourseCount > MAX_REPEAT_COURSES)
            {
                ElevateRisk(report, DegreeProgressRiskBand.High);
                report.Recommendations.Add("Excessive repeated courses.");
            }

            if (ctx.TransferCredits > MAX_TRANSFER_CREDITS)
            {
                ElevateRisk(report, DegreeProgressRiskBand.High);
                report.Recommendations.Add("Transfer credits above program guideline.");
            }
        }
        /// <summary>
        /// Toplam planlanan kredi ile öğrencinin tamamladığı krediler arasındaki
        /// farktan kalan krediyi hesaplar; tipik dönem kredi yüküne göre tahmini
        /// kalan dönem sayısını hesaplar ve beklenen planı aşan durumlarda risk
        /// bandını Critical seviyesine yükseltir.
        /// </summary>
        private void EstimateRemainingTerms(DegreeMap map, DegreeProgressContext ctx, DegreeProgressReport report)
        {
            int totalPlannedCredits = map.TotalPlannedCredits > 0 ? map.TotalPlannedCredits : STANDARD_TOTAL_CREDITS;
            int completedCredits = ctx.CompletedCredits;
            int remaining = Math.Max(0, totalPlannedCredits - completedCredits);

            report.RemainingCredits = remaining;

            int typicalTermCredits = ctx.TypicalTermCredits <= 0 ? 15 : ctx.TypicalTermCredits;
            report.EstimatedRemainingTerms = typicalTermCredits == 0
                ? 0
                : (int)Math.Ceiling((decimal)remaining / typicalTermCredits);

            if (report.EstimatedRemainingTerms > ctx.ExpectedRemainingTerms)
            {
                ElevateRisk(report, DegreeProgressRiskBand.Critical);
                report.Recommendations.Add("Estimated remaining terms exceed expected plan.");
            }
        }

        #endregion

        #region Private - Helpers
        /// <summary>
        /// Hedef ders için equivalency grafiğini kullanarak, öğrencinin eşdeğer bir dersi
        /// tamamlamış sayılıp sayılamayacağını kontrol eder; herhangi bir eşdeğer tamam
        /// ise true döner.
        /// </summary>
        private bool HasEquivalentCompletion(DegreeProgressContext ctx, string targetCourseId)
        {
            var equivalents = _equivalencies.GetEquivalents(targetCourseId);
            foreach (var eq in equivalents)
            {
                if (ctx.CompletedCourses.Any(c => c.CourseId == eq.CourseId))
                {
                    return true;
                }
            }
            return false;
        }
        /// <summary>
        /// Rapor üzerindeki mevcut risk bandını, verilen hedef band ile
        /// karşılaştırarak daha yüksek olan değere yükseltir; birden fazla
        /// analiz katmanının risk katkısını birleştirmek için kullanılır.
        /// </summary>
        private static void ElevateRisk(DegreeProgressReport report, DegreeProgressRiskBand target)
        {
            report.RiskBand = (DegreeProgressRiskBand)Math.Max((int)report.RiskBand, (int)target);
        }

        #endregion
    }
} 
