using System;
using System.Collections.Generic;
using System.Linq; 
using University.Lms.Ports;
using UniversityLessonSelectionSystem.Domain.Enums;
using UniversityLessonSelectionSystem.Domain.GradingPolicy;
using UniversityLessonSelectionSystem.Ports.GradingPolicy;

namespace University.Lms.Services
{
    /// <summary>
    /// Bir dersin notlandırma çıktıları ve kurallarını kurum politikasına göre denetleyen servistir;
    /// bölüm bazlı eğri (curve) dağılımlarını, geç teslim cezalarının monoton ve sınırlar içinde olmasını,
    /// ekstra kredi üst sınırlarını, tekrar (retake) kurallarını ve not değiştirme mantığını,
    /// Incomplete/Withdraw işlenişini ve not değişiklikleri için onay zinciri ile zaman pencerelerini kontrol eder;
    /// politika ihlallerini ve uyarıları, mümkün olduğunda düzeltme önerileriyle birlikte üretir.
    /// Eğitim amaçlı, okunabilir ve test edilebilir kalırken yüksek karar dallanması barındıracak şekilde tasarlanmıştır.
    /// </summary>
    public sealed class GradingPolicyAuditService
    {
        #region Fields
        private readonly ILogger _logger;
        private readonly IGradingPolicyRepo _policy;
        #endregion

        #region Constants

        private const decimal MAX_EXTRA_CREDIT_RATIO = 0.10m; // 10% of total
        private const int GRADE_CHANGE_WINDOW_DAYS = 14;

        private static readonly Dictionary<Department, (decimal minA, decimal maxF)> CurveBands =
            new Dictionary<Department, (decimal, decimal)>
            {
                { Department.CS, (0.20m, 0.10m) },
                { Department.EE, (0.15m, 0.12m) },
                { Department.ME, (0.18m, 0.12m) },
                { Department.BUS,(0.25m, 0.08m) },
                { Department.BIO,(0.20m, 0.10m) },
                { Department.ART,(0.30m, 0.05m) },
                { Department.LAW,(0.10m, 0.05m) }
            };

        #endregion

        #region Constructor
        /// <summary>
        /// Log ve not politikası veri kaynağı bağımlılıklarını alarak,
        /// notlandırma denetim kurallarını çalıştıracak GradingPolicyAuditService örneğini oluşturur.
        /// </summary>
        public GradingPolicyAuditService(ILogger logger, IGradingPolicyRepo policy)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Verilen not denetim bağlamına göre (not dağılımları, geç teslim cezaları,
        /// ekstra krediler, tekrar denemeler, özel işaretler ve not değişiklikleri),
        /// tüm kontrol katmanlarını çalıştırır ve sonuçta politika ihlalleri ile uyarıları içeren bir denetim sonucu döner.
        /// </summary>
        public GradingAuditResult Audit(GradingAuditContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            var result = new GradingAuditResult();

            CheckCurve(ctx, result);
            CheckLatePenalties(ctx, result);
            CheckExtraCredit(ctx, result);
            CheckRetakeRules(ctx, result);
            CheckIncompleteWithdraw(ctx, result);
            CheckGradeChangeApprovals(ctx, result);

            _logger.Info($"Grading audit: violations={result.Violations.Count}, warnings={result.Warnings.Count}");
            return result;
        }
        #endregion

        #region Private Methods

        #region Checks
        /// <summary>
        /// Bölüm için tanımlanmış eğri (curve) bantlarına göre;
        /// A harf notu oranının bölüm için minimum eşiğin altında olup olmadığını,
        /// F harf notu oranının bölüm için maksimum eşiğin üzerinde olup olmadığını kontrol eder.
        /// Eşik dışı durumlarda uyarı üretir.
        /// </summary>
        private void CheckCurve(GradingAuditContext c, GradingAuditResult r)
        {
            var band = CurveBands[c.Department];
            decimal fracA = Fraction(c.Grades, LetterGrade.A);
            decimal fracF = Fraction(c.Grades, LetterGrade.F);

            if (fracA < band.minA)
                r.Warnings.Add($"A-range fraction below dept band: actual={fracA:0.00}, min={band.minA:0.00}");
            if (fracF > band.maxF)
                r.Warnings.Add($"F-range fraction above dept band: actual={fracF:0.00}, max={band.maxF:0.00}");
        }
        /// <summary>
        /// Gün bazında tanımlanan geç teslim cezalarının (LatePenaltyByDay),
        /// gün ilerledikçe azalmadığını (monoton artan veya sabit) doğrular;
        /// her gün için ceza değerini sıralı olarak kontrol eder ve azalan bir değer tespit edilirse ihlal ekler.
        /// </summary>
        private void CheckLatePenalties(GradingAuditContext c, GradingAuditResult r)
        {
            // monotonic non-decreasing penalties
            int last = 0;
            foreach (var p in c.LatePenaltyByDay.OrderBy(kv => kv.Key))
            {
                if (p.Value < last)
                {
                    r.Violations.Add("Late penalty not monotonic (must be non-decreasing by day).");
                    break;
                }
                last = p.Value;
            }
        }
        /// <summary>
        /// Toplam puan ve ekstra kredi puanlarına bakarak,
        /// ekstra kredilerin toplam değerlendirmenin yüzde kaçına denk geldiğini hesaplar;
        /// bu oran tanımlı üst sınırı (MAX_EXTRA_CREDIT_RATIO) aşıyorsa politika ihlali üretir.
        /// </summary>
        private void CheckExtraCredit(GradingAuditContext c, GradingAuditResult r)
        {
            var totalPoints = c.TotalPoints;
            var ecPoints = c.ExtraCreditPoints;
            if (totalPoints == 0) return;

            var ratio = (decimal)ecPoints / totalPoints;
            if (ratio > MAX_EXTRA_CREDIT_RATIO)
                r.Violations.Add($"Extra credit exceeds cap: {ratio:P0} > {MAX_EXTRA_CREDIT_RATIO:P0}");
        }
        /// <summary>
        /// Tekrar alınan ders kümeleri (RetakeBundles) içinde,
        /// yalnızca en güncel (en yüksek TermOrder) denemenin geçerli kalıp kalmadığını kontrol eder;
        /// daha eski bir denemenin daha yüksek bir notla hâlâ aktif olduğu durumlarda retake kuralı ihlali üretir.
        /// </summary>
        private void CheckRetakeRules(GradingAuditContext c, GradingAuditResult r)
        {
            // Replacements should keep highest recent grade only
            foreach (var set in c.RetakeBundles)
            {
                var sorted = set.OrderByDescending(x => x.TermOrder).ToList();
                var top = sorted.First().Grade;
                if (sorted.Skip(1).Any(x => GradeValue(x.Grade) > GradeValue(top)))
                    r.Violations.Add("Retake rule violated: older attempt with higher grade still active.");
            }
        }
        /// <summary>
        /// Incomplete ve Withdraw özel işaretlerini (SpecialMarks) kontrol eder;
        /// Incomplete işaretinin politika tarafından tanımlanan maksimum süreyi aşıp aşmadığını,
        /// Withdraw kaydının da çekilme son tarihinden (WithdrawDeadlineUtc) sonra yapılıp yapılmadığını denetler.
        /// Süre aşımı veya geç çekilme durumlarında ihlal ekler.
        /// </summary>
        private void CheckIncompleteWithdraw(GradingAuditContext c, GradingAuditResult r)
        {
            foreach (var s in c.SpecialMarks)
            {
                if (s.Mark == SpecialMark.Incomplete && (c.Now - s.TimestampUtc).TotalDays > _policy.IncompleteMaxDays())
                    r.Violations.Add("Incomplete exceeded max days without resolution.");

                if (s.Mark == SpecialMark.Withdraw && !_policy.WithdrawAllowedAfter(c.WithdrawDeadlineUtc))
                    r.Violations.Add("Withdraw recorded after deadline.");
            }
        }
        /// <summary>
        /// Not değişikliği kayıtlarını (GradeChanges) kontrol eder;
        /// her değişikliğin not döneminden sonraki izinli zaman penceresi (GRADE_CHANGE_WINDOW_DAYS) içinde yapılıp yapılmadığını
        /// ve ilgili not değişikliği için gerekli tüm onay zincirinin (approval chain) tamamlanıp tamamlanmadığını
        /// IGradingPolicyRepo üzerinden doğrular; ihlal durumunda mesaj ekler.
        /// </summary>
        private void CheckGradeChangeApprovals(GradingAuditContext c, GradingAuditResult r)
        {
            foreach (var g in c.GradeChanges)
            {
                if ((c.Now - g.TimestampUtc).TotalDays > GRADE_CHANGE_WINDOW_DAYS)
                    r.Violations.Add("Grade change outside allowed window.");

                if (!_policy.ApprovalChainSatisfied(g.ChangeId))
                    r.Violations.Add("Grade change approval chain incomplete.");
            }
        }

        #endregion

        #region Helpers
        /// <summary>
        /// Verilen harf notunun (LetterGrade g), not listesi içinde yüzde olarak oranını hesaplar;
        /// not listesi boşsa 0 döner.
        /// </summary>
        private static decimal Fraction(IList<LetterGrade> grades, LetterGrade g)
            => grades.Count == 0 ? 0m : (decimal)grades.Count(x => x == g) / grades.Count;
        /// <summary>
        /// Harf notunu (A,B,C,D,F) sayısal bir değere dönüştürür;
        /// daha yüksek değer daha yüksek başarıyı temsil eder ve retake kuralı kontrollerinde karşılaştırma için kullanılır.
        /// </summary>
        private static int GradeValue(LetterGrade g)
        {
            switch (g)
            {
                case LetterGrade.A:
                    return 5;
                case LetterGrade.B:
                    return 4;
                case LetterGrade.C:
                    return 3;
                case LetterGrade.D:
                    return 2;
                default:
                    return 1;
            }
        }

        #endregion

        #endregion
    }
}
