using System;
using System.Collections.Generic;
using System.Linq; 
using University.Lms.Ports;
using UniversityLessonSelectionSystem.Domain.Enums;
using UniversityLessonSelectionSystem.Domain.FeeAndInvoice;
using UniversityLessonSelectionSystem.Ports.FeeAndInvoice;
using UniversityLessonSelectionSystem.Ports.ScholarshipEligibilityEngine;

namespace University.Lms.Services
{
    /// <summary> 
    ///  Öğrencinin term faturası için ücret kalemlerini (tuition, surcharges, waivers, installments) hesaplar.
    /// High CC via orthogonal rule layers; testability via DI catalogs; no string compares.    
    /// Bir öğrencinin belirli bir dönem için ödemesi gereken toplam ücreti (fatura) hesaplayan servistir;
    /// temel ders ücreti, program ve bölüm ek ücretleri (lab/stüdyo), ikamet/uluslararası farkları, geç kayıt cezaları,
    /// burs ve muafiyetler (üst sınır ve birbirini dışlama kuralları), taksit planı ücretleri/finansman maliyeti
    /// ve uyumsuzluk (compliance) bloklarının indirimleri iptal etmesi gibi tüm katmanları art arda uygulayarak
    /// satır bazlı fatura kalemleri ve genel toplam üretir. 
    /// </summary>
    public sealed class FeeAndInvoiceComputationService
    {
        #region Fields
        private readonly IClock _clock;
        private readonly ILogger _logger;
        private readonly IFeeCatalogRepo _catalog;
        private readonly IFinanceRepo _finance;
        #endregion

        #region Policy constants

        private const int UG_FULLTIME_MIN = 12;
        private const int GRAD_FULLTIME_MIN = 9;

        private const decimal INTERNATIONAL_SUPPORT_FEE = 250m;
        private const decimal INSTALLMENT_PLAN_FEE = 35m;
        private const decimal FINANCING_RATE_MONTHLY = 0.01m; // 1%/month
        private const decimal LATE_REG_FEE_REGISTRATION = 50m;
        private const decimal LATE_REG_FEE_ADDDROP = 100m;

        private const int INSTALLMENT_MONTHS = 4;
        private const decimal WAIVER_MAX_RATIO = 0.75m; // waivers cannot exceed 75% of tuition+program fees
        private const decimal SCHOLARSHIP_OFFSET_CAP = 0.85m; // scholarships+waivers cannot exceed 85%

        private static readonly HashSet<Department> LabSurchargeDepartments =
            new HashSet<Department> { Department.CS, Department.EE, Department.ME, Department.BIO };

        private static readonly HashSet<Department> StudioSurchargeDepartments =
            new HashSet<Department> { Department.ART };

        #endregion

        #region Constructor
        public FeeAndInvoiceComputationService(IClock clock, ILogger logger, IFeeCatalogRepo catalog, IFinanceRepo finance)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _finance = finance ?? throw new ArgumentNullException(nameof(finance));
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Builds a full invoice with line items and totals for a given student/term context.
        /// </summary>
        public InvoiceSummary BuildInvoice(FeeContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            var inv = new InvoiceSummary();

            // 1) Tuition band
            var tuition = ComputeTuition(ctx);
            inv.Items.Add(tuition);

            // 2) Program/department surcharges
            var programFees = ProgramAndDeptFees(ctx);
            foreach (var item in programFees)
            {
                inv.Items.Add(item);
            }

            // 3) Residency differential
            var residency = ResidencyFee(ctx);
            if (residency != null) inv.Items.Add(residency);

            // 4) Late registration penalties (phase-aware)
            var late = LateRegistrationPenalty(ctx);
            if (late != null) inv.Items.Add(late);

            // 5) Scholarships & waivers (with caps and exclusivity)
            var scholarships = Scholarships(ctx, inv);
            foreach (var item in scholarships)
            {
                inv.Items.Add(item);
            }

            var waivers = Waivers(ctx, inv);
            foreach (var item in waivers)
            {
                inv.Items.Add(item);
            }

            // 6) Installment plan & financing charge
            var installment = InstallmentFees(ctx, inv);
            if (installment != null) inv.Items.Add(installment);

            // 7) International support fee
            var intl = InternationalSupport(ctx);
            if (intl != null) inv.Items.Add(intl);

            // 8) Holds enforcement: remove benefits if blocking
            ApplyHoldGates(ctx, inv);

            // Totals
            inv.Subtotal = inv.Items.Where(i => i.Kind == InvoiceKind.Debit).Sum(i => i.Amount)
                        - inv.Items.Where(i => i.Kind == InvoiceKind.Credit).Sum(i => i.Amount);

            inv.Tax = 0m; // academic invoices typically tax-exempt; left for extensibility
            inv.Total = inv.Subtotal + inv.Tax;

            _logger.Info($"Invoice built: items={inv.Items.Count} total={inv.Total:0.00}");
            return inv;
        }
        #endregion

        #region Private Methods


        #region Layers
        /// <summary>
        /// Program türü ve kredi yüküne göre öğrencinin kredi bandını belirler,
        /// kataloğu kullanarak kredi başına ücreti bulur ve toplam temel ders ücretini (tuition) borç kalemi olarak oluşturur.
        /// </summary>
        private InvoiceItem ComputeTuition(FeeContext c)
        {
            var band = TuitionBand(c);
            var rate = _catalog.RatePerCredit(c.Program, band);
            var credits = Math.Max(0, c.CreditLoad);
            var amount = rate * credits;

            return new InvoiceItem
            {
                Component = FeeComponentType.Tuition,
                Kind = InvoiceKind.Debit,
                Amount = amount,
                Notes = $"Band={band} Rate={rate:0.00} Credits={credits}"
            };
        }
        /// <summary>
        /// Öğrencinin kredi yükünü ve program türünü (lisans/lisansüstü) dikkate alarak
        /// None, PartTime veya FullTime kredi bandını belirler; tam zamanlı eşiklerin altı yarı zamanlı sayılır.
        /// </summary>
        private CreditBand TuitionBand(FeeContext c)
        {
            int fullMin = c.Program == ProgramType.Undergraduate ? UG_FULLTIME_MIN : GRAD_FULLTIME_MIN;
            if (c.CreditLoad == 0) return CreditBand.None;
            if (c.CreditLoad < fullMin) return CreditBand.PartTime;
            return CreditBand.FullTime;
        }
        /// <summary>
        /// Programa özel sabit ücretleri ve ilgili bölümlere ait laboratuvar/stüdyo ek ücretlerini hesaplayarak,
        /// borç kalemi listesi olarak döner; program ücreti tek kalemken, her bölüm için lab/stüdyo ek kalemleri eklenir.
        /// </summary>
        private IList<InvoiceItem> ProgramAndDeptFees(FeeContext c)
        {
            var items = new List<InvoiceItem>();

            // program fee
            var pFee = _catalog.ProgramFee(c.Program);
            if (pFee > 0m)
            {
                items.Add(new InvoiceItem
                {
                    Component = FeeComponentType.Program,
                    Kind = InvoiceKind.Debit,
                    Amount = pFee,
                    Notes = $"Program={c.Program}"
                });
            }

            // department surcharges
            foreach (var dept in c.EnrolledDepartments)
            {
                if (LabSurchargeDepartments.Contains(dept))
                {
                    var lab = _catalog.LabSurcharge(dept);
                    if (lab > 0m)
                        items.Add(Debit(FeeComponentType.LabSurcharge, lab, $"Dept={dept}"));
                }
                if (StudioSurchargeDepartments.Contains(dept))
                {
                    var studio = _catalog.StudioSurcharge(dept);
                    if (studio > 0m)
                        items.Add(Debit(FeeComponentType.StudioSurcharge, studio, $"Dept={dept}"));
                }
            }

            return items;
        }
        /// <summary>
        /// Öğrencinin ikamet durumuna göre uluslararası öğrenciler için uygulanacak fark ücretini hesaplar;
        /// katalogdan uluslararası fark tutarını alır ve pozitifse borç kalemi olarak döner, aksi halde null döner.
        /// </summary>
        private InvoiceItem ResidencyFee(FeeContext c)
        {
            if (c.Residency == ResidencyStatus.International)
            {
                var diff = _catalog.InternationalDifferential();
                if (diff > 0m) return Debit(FeeComponentType.ResidencyDifferential, diff, "International");
            }
            return null;
        }
        /// <summary>
        /// Dönem fazına (Registration / AddDrop) ve geç kayıt bayrağına bakarak,
        /// ilgili faz için tanımlı geç kayıt cezasını uygular ve borç kalemi olarak döner; koşullar sağlanmıyorsa null döner.
        /// </summary>
        private InvoiceItem LateRegistrationPenalty(FeeContext c)
        {
            if (c.TermPhase == TermPhase.Registration && c.IsLateRegistration)
                return Debit(FeeComponentType.LateRegistration, LATE_REG_FEE_REGISTRATION, "Registration");

            if (c.TermPhase == TermPhase.AddDrop && c.IsLateRegistration)
                return Debit(FeeComponentType.LateRegistration, LATE_REG_FEE_ADDDROP, "AddDrop");

            return null;
        }
        /// <summary>
        /// Öğrencinin burs katmanı, programı ve kredi bandına göre önerilen burs tutarını kataloğa sorar,
        /// burs ve muafiyetlerin toplamının temel (tuition+program) tutarın en fazla %85'ine kadar çıkabilmesi kuralını uygular
        /// ve uygun tutarda burs kalemini (alacak/credit) listeye ekler.
        /// </summary>
        private IList<InvoiceItem> Scholarships(FeeContext c, InvoiceSummary inv)
        {
            var items = new List<InvoiceItem>();
            if (c.ScholarshipTier == ScholarshipTier.None) return items;

            // cap: scholarships+waivers ≤ 85% of eligible base (tuition+program fees)
            var eligibleBase = inv.Items.Where(i => i.Component == FeeComponentType.Tuition
                                                 || i.Component == FeeComponentType.Program)
                                        .Sum(i => i.Amount);

            var maxScholarship = eligibleBase * SCHOLARSHIP_OFFSET_CAP;
            var suggested = _catalog.ScholarshipAward(c.ScholarshipTier, c.Program, TuitionBand(c));
            var grant = Math.Min(suggested, maxScholarship);

            if (grant > 0m)
                items.Add(Credit(FeeComponentType.Scholarship, grant, $"Tier={c.ScholarshipTier}"));

            return items;
        }
        /// <summary>
        /// Öğrencinin muafiyet politikasına ve programına göre önerilen muafiyet tutarını kataloğa sorar,
        /// muafiyetlerin temel tutarın en fazla %75'i ile sınırlandığı kuralını uygular;
        /// uluslararası destek ücretiyle birlikte kullanıldığında ise daha sıkı bir üst sınır (örneğin %25) uygular
        /// ve uygun tutarda muafiyet kalemini (alacak/credit) listeye ekler.
        /// </summary>
        private IList<InvoiceItem> Waivers(FeeContext c, InvoiceSummary inv)
        {
            var items = new List<InvoiceItem>();
            if (c.WaiverPolicy == WaiverPolicy.None) return items;

            var eligibleBase = inv.Items.Where(i => i.Component == FeeComponentType.Tuition
                                                 || i.Component == FeeComponentType.Program)
                                        .Sum(i => i.Amount);

            var cap = eligibleBase * WAIVER_MAX_RATIO;
            var suggested = _catalog.WaiverAmount(c.WaiverPolicy, c.Program);
            var waiver = Math.Min(cap, suggested);

            // exclusivity example: International support + large waiver not combinable
            if (c.Residency == ResidencyStatus.International && waiver > 0m && c.IncludeInternationalSupport)
                waiver = Math.Min(waiver, eligibleBase * 0.25m);

            if (waiver > 0m)
                items.Add(Credit(FeeComponentType.Waiver, waiver, $"Policy={c.WaiverPolicy}"));

            return items;
        }

        /// <summary>
        /// Öğrencinin seçtiği ödeme planına göre (taksitli veya finansmanlı taksitli),
        /// borç ve alacak kalemlerinin net tutarını baz alarak sabit taksit planı ücretini
        /// ve varsa finansman faizini hesaplar; sonucu borç kalemi olarak döner.
        /// </summary>
        private InvoiceItem InstallmentFees(FeeContext c, InvoiceSummary inv)
        {
            if (c.Plan == PaymentPlan.None) return null;

            var baseAmount = inv.Items.Where(i => i.Kind == InvoiceKind.Debit).Sum(i => i.Amount)
                           - inv.Items.Where(i => i.Kind == InvoiceKind.Credit).Sum(i => i.Amount);

            if (baseAmount <= 0m) return null;

            decimal fee = INSTALLMENT_PLAN_FEE;
            if (c.Plan == PaymentPlan.InstallmentsWithFinancing)
            {
                // simple financing charge for teaching purposes
                fee += baseAmount * FINANCING_RATE_MONTHLY * INSTALLMENT_MONTHS;
            }

            return Debit(FeeComponentType.Installment, fee, $"Plan={c.Plan}");
        }

        /// <summary>
        /// Uluslararası öğrenciler için, öğrencinin uluslararası destek ücretinin dahil edilip edilmeyeceği bayrağına göre
        /// sabit uluslararası destek ücretini uygular ve borç kalemi oluşturur; koşullar sağlanmıyorsa null döner.
        /// </summary>
        private InvoiceItem InternationalSupport(FeeContext c)
        {
            if (c.Residency == ResidencyStatus.International && c.IncludeInternationalSupport)
                return Debit(FeeComponentType.InternationalSupport, INTERNATIONAL_SUPPORT_FEE, "International");
            return null;
        }
        /// <summary>
        /// Öğrencinin üzerinde uyumluluk (compliance) blokajı varsa,
        /// tüm indirim kalemlerini (burs ve muafiyetler) fatura satırlarından kaldırır,
        /// yalnızca borç kalemlerini bırakarak indirimleri fiilen devre dışı bırakır ve
        /// bu durumu açıklayan sıfır tutarlı bir blokaj cezası kalemi ekler.
        /// </summary>
        private void ApplyHoldGates(FeeContext c, InvoiceSummary inv)
        {
            if (!c.HasComplianceHold) return;

            // Remove credits (scholarships/waivers) until hold resolved
            inv.Items = inv.Items.Where(i => i.Kind == InvoiceKind.Debit).ToList();
            inv.Items.Add(Debit(FeeComponentType.HoldPenalty, 0m, "Benefits blocked by hold"));
        }

        #endregion

        #region Builders
        /// <summary>
        /// Verilen bileşen türü, tutar ve açıklama için bir borç (debit) türü fatura kalemi oluşturur.
        /// </summary>
        private static InvoiceItem Debit(FeeComponentType t, decimal amt, string notes) =>
            new InvoiceItem { Component = t, Kind = InvoiceKind.Debit, Amount = amt, Notes = notes };
        /// <summary>
        /// Verilen bileşen türü, tutar ve açıklama için bir alacak (credit) türü fatura kalemi oluşturur.
        /// </summary>
        private static InvoiceItem Credit(FeeComponentType t, decimal amt, string notes) =>
            new InvoiceItem { Component = t, Kind = InvoiceKind.Credit, Amount = amt, Notes = notes };

        #endregion

        #endregion
    }
}
