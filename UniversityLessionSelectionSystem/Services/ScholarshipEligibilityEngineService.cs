using System; 
using University.Lms.Ports;
using UniversityLessonSelectionSystem.Domain.Enums;
using UniversityLessonSelectionSystem.Domain.ScholarshipEligibilityEngine;
using UniversityLessonSelectionSystem.Ports.ScholarshipEligibilityEngine;

namespace University.Lms.Services
{
    /// <summary>
    /// Bir öğrencinin burs uygunluğunu ve alabileceği burs kademesini (ScholarshipTier)
    /// çok katmanlı kurallara göre hesaplayan servistir:
    /// - GPA seviyeleri (altın/gümüş/bronz) ve akademik durum,
    /// - Program türüne göre tam zamanlı/kısmi zamanlı kredi yükü,
    /// - Major/minor bölüm uygunluğu ve ikamet durumu,
    /// - Finans tarafından sağlanan ihtiyaç (need index) skorları,
    /// - Disiplin kayıtları ve probation/uzaklaştırma durumları,
    /// - Tekrar edilen ders kredisi ve hazırlık/remedial kredi sınırları,
    /// - Dönemsel toplam burs kontenjanı ve bölüm bazlı kota kuralları.
    /// 
    /// Sonuç olarak, burs durumu (onay/ret) ve gerekçelerle birlikte hangi tier’in
    /// verileceğini belirler; eğitim amaçlı olarak temiz, test edilebilir ve
    /// enum temelli dallanma ile okunabilir olacak şekilde tasarlanmıştır.
    /// </summary>
    public sealed class ScholarshipEligibilityEngineService
    {
        #region Fields
        private readonly IFinanceRepo _finance;
        private readonly ILogger _logger;
        #endregion

        #region Policy Constants

        private const decimal GPA_TIER_GOLD = 3.80m;
        private const decimal GPA_TIER_SILVER = 3.50m;
        private const decimal GPA_TIER_BRONZE = 3.20m;

        private const int UG_FULLTIME_MIN = 12;
        private const int GRAD_FULLTIME_MIN = 9;

        private const int MAX_REPEATABLE_CREDITS = 6;
        private const int MAX_REMEDIAL_CREDITS = 6;

        private const int NEED_INDEX_STRONG = 80; // 0..100 (higher = more need)
        private const int NEED_INDEX_MIN = 50;

        private const int TERM_BUDGET_CAP = 100; // award slots overall
        private const int DEPT_QUOTA_MIN = 5;

        #endregion

        #region Constructor
        /// <summary>
        /// Finans ve log bağımlılıklarını alarak burs uygunluk motoru
        /// (ScholarshipEligibilityEngineService) örneğini oluşturur.
        /// </summary>
        public ScholarshipEligibilityEngineService(IFinanceRepo finance, ILogger logger)
        {
            _finance = finance ?? throw new ArgumentNullException(nameof(finance));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Verilen dönem bağlamı (ScholarshipContext) için burs uygunluğunu değerlendirir;
        /// tüm gate kontrollerini sırayla çalıştırır, ardından ihtiyaç bandı ve GPA seviyesini
        /// hesaplayarak bunlardan türeyen burs tier kararını üretir.
        /// </summary>
        public ScholarshipDecision Evaluate(ScholarshipContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            var decision = new ScholarshipDecision();

            if (!StandingOk(ctx)) return Reject(decision, "Standing/disciplinary restriction.");
            if (!ProgramLoadOk(ctx)) return Reject(decision, "Insufficient credit load.");
            if (!MajorMinorOk(ctx)) return Reject(decision, "Major/minor not eligible.");
            if (!ResidencyOk(ctx)) return Reject(decision, "Residency not eligible.");
            if (!RepeatsRemedialOk(ctx)) return Reject(decision, "Repeat/remedial credit limits exceeded.");
            if (!BudgetCapacityOk(ctx)) return Reject(decision, "Term budget/department quota exhausted.");

            var needBand = CheckNeedBand(ctx);
            var gpaTier = CheckGpaTier(ctx);

            decision.Tier = TierFrom(needBand, gpaTier);
            decision.Status = ScholarshipStatus.Approved;
            decision.Reasons.Add($"Tier={decision.Tier} via NeedBand={needBand} & GPATier={gpaTier}");

            _logger.Info($"Scholarship approve: Tier={decision.Tier}");
            return decision;
        }
        #endregion

        #region Private Methods

        #region Gates
        /// <summary>
        /// Öğrencinin akademik durumu (Standing) ve disiplin bayraklarını değerlendirerek,
        /// probation veya suspension durumunda ya da herhangi bir disiplin kaydı varsa bursu engeller;
        /// aksi durumda bu katmandan geçişe izin verir.
        /// </summary>
        private static bool StandingOk(ScholarshipContext c)
        {
            if (c.Standing == StudentStanding.Probation || c.Standing == StudentStanding.Suspension)
                return false;
            if (c.DisciplinaryFlags > 0) return false;
            return true;
        }
        /// <summary>
        /// Öğrencinin kredi yükünü, program türüne göre (lisans/lisansüstü) minimum tam zamanlı
        /// kredi eşiği ile karşılaştırır; kredi yükü yeterli değilse bursu engeller.
        /// </summary>
        private static bool ProgramLoadOk(ScholarshipContext c)
        {
            int min = c.Program == ProgramType.Undergraduate ? UG_FULLTIME_MIN : GRAD_FULLTIME_MIN;
            return c.CreditLoad >= min;
        }
        /// <summary>
        /// Burs politikasında tanımlı uygun bölümler listesine (AllowedDepartments) göre,
        /// öğrencinin major bölümünün burs almaya uygun olup olmadığını kontrol eder;
        /// liste boş veya null ise tüm bölümleri kabul eder.
        /// </summary>
        private static bool MajorMinorOk(ScholarshipContext c)
        {
            if (c.AllowedDepartments != null && c.AllowedDepartments.Count > 0)
                return c.AllowedDepartments.Contains(c.MajorDepartment);
            return true;
        }
        /// <summary>
        /// Öğrencinin ikamet durumuna (Residency) göre ek eşik uygular;
        /// örneğin uluslararası öğrenciler için daha yüksek ihtiyaç indeksi (NeedIndex)
        /// gerektirir; eşik altında kalırsa bursu engeller.
        /// </summary>
        private static bool ResidencyOk(ScholarshipContext c)
        {
            // Example: International may require higher need index
            if (c.Residency == ResidencyStatus.International && c.NeedIndex < NEED_INDEX_STRONG)
                return false;
            return true;
        }
        /// <summary>
        /// Öğrencinin tekrar ettiği ders kredisi (RepeatCredits) ve remedial/ hazırlık ders
        /// kredisi (RemedialCredits) için tanımlı maksimum sınırları kontrol eder;
        /// sınırlar aşıldığında bursu engeller.
        /// </summary>
        private static bool RepeatsRemedialOk(ScholarshipContext c)
        {
            if (c.RepeatCredits > MAX_REPEATABLE_CREDITS) return false;
            if (c.RemedialCredits > MAX_REMEDIAL_CREDITS) return false;
            return true;
        }
        /// <summary>
        /// Dönem bazlı toplam burs kontenjanını (TERM_BUDGET_CAP) ve bölüm bazlı kullanım
        /// sayılarını finans sisteminden çekerek, yeni bir burs verilebilecek kapasite olup
        /// olmadığını kontrol eder; toplam kullanım üst sınıra ulaştıysa bursu engeller.
        /// Bölüm bazlı minimum kota (DEPT_QUOTA_MIN) için taban sağlanana kadar esnek davranır.
        /// </summary>
        private bool BudgetCapacityOk(ScholarshipContext c)
        {
            // Finance repo provides current usage
            var usedTotal = _finance.AwardsUsedTotal(c.TermId);
            var usedDept = _finance.AwardsUsedByDepartment(c.TermId, c.MajorDepartment);

            if (usedTotal >= TERM_BUDGET_CAP) return false;
            if (usedDept < DEPT_QUOTA_MIN) return true; // ensure floor for each dept
            return true;
        }

        #endregion

        #region Bands
        /// <summary>
        /// Öğrencinin NeedIndex değerine göre, ihtiyaç bandını (Low/Medium/High)
        /// belirler; daha yüksek indeks daha yüksek finansal ihtiyaç anlamına gelir
        /// ve üst bantlara yerleştirir.
        /// </summary>
        private static NeedBand CheckNeedBand(ScholarshipContext c)
        {
            if (c.NeedIndex >= NEED_INDEX_STRONG) return NeedBand.High;
            if (c.NeedIndex >= NEED_INDEX_MIN) return NeedBand.Medium;
            return NeedBand.Low;
        }
        /// <summary>
        /// Öğrencinin dönemsel not ortalamasına (Gpa) göre,
        /// Gold/Silver/Bronze/None şeklinde GPA tier'ını belirler; bu tier burs kademesi
        /// kararında ihtiyaç bandı ile birlikte kullanılır.
        /// </summary>
        private static GpaTier CheckGpaTier(ScholarshipContext c)
        {
            if (c.Gpa >= GPA_TIER_GOLD) return GpaTier.Gold;
            if (c.Gpa >= GPA_TIER_SILVER) return GpaTier.Silver;
            if (c.Gpa >= GPA_TIER_BRONZE) return GpaTier.Bronze;
            return GpaTier.None;
        }
        /// <summary>
        /// Hesaplanan ihtiyaç bandı (NeedBand) ve GPA seviyesi (GpaTier) kombinasyonuna göre
        /// hangi burs kademesinin (ScholarshipTier) verileceğini belirler;
        /// basit bir matris üzerinden enum-temelli dallanma yapar.
        /// </summary>
        private static ScholarshipTier TierFrom(NeedBand need, GpaTier gpa)
        {
            // simple matrix to keep enum-only branching
            if (need == NeedBand.High && gpa == GpaTier.Gold) return ScholarshipTier.A;
            if (need == NeedBand.High && gpa == GpaTier.Silver) return ScholarshipTier.B;
            if (need == NeedBand.Medium && gpa == GpaTier.Gold) return ScholarshipTier.B;
            if (need == NeedBand.Medium && gpa == GpaTier.Silver) return ScholarshipTier.C;
            if (need == NeedBand.High && gpa == GpaTier.Bronze) return ScholarshipTier.C;
            if (need == NeedBand.Medium && gpa == GpaTier.Bronze) return ScholarshipTier.D;
            return ScholarshipTier.None;
        }
        /// <summary>
        /// Burs kararını reddedilmiş (Rejected) duruma getirir ve verilen gerekçeyi
        /// Reasons listesine ekler; zincirlenebilir şekilde aynı nesneyi geri döner.
        /// </summary>
        private static ScholarshipDecision Reject(ScholarshipDecision d, string reason)
        {
            d.Status = ScholarshipStatus.Rejected;
            d.Reasons.Add(reason);
            return d;
        }

        #endregion

        #endregion
    }
}
