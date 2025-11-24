using System;
using System.Collections.Generic;
using University.Lms.Ports;
using UniversityLessionSelectionSystem.Domain; 
using UniversityLessonSelectionSystem.Domain.Enums;
using UniversityLessonSelectionSystem.Domain.SLAAndHealthMonitor;
using UniversityLessonSelectionSystem.Ports.EnrollmentEligibility;
using UniversityLessonSelectionSystem.Ports.SLAAndHealthMonitor;

namespace University.Lms.Services
{
    /// <summary>
    /// Öğrencinin dönem ve kümülatif performans ölçümlerini kullanarak;
    /// - GPA bantları,
    /// - Fail edilen kredi sayısı,
    /// - Incomplete sayısı,
    /// - Disiplin olayları,
    /// - Peş peşe probation dönemleri,
    /// - Program türü (lisans/lisansüstü),
    /// - Dönem tipi (normal/yaz),
    /// sinyallerini değerlendirip yeni akademik durum (Good/Probation/Suspension) ve risk bandını
    /// (Low/Medium/High/Critical) hesaplayan; gerekiyorsa aksiyon önerileri ve Alert üreten servistir.
    /// Eğitim amaçlı, yüksek karar dallanmasına sahip, ancak küçük kural metotlarına bölünmüş ve DI ile
    /// kolay test edilebilir şekilde tasarlanmıştır.
    /// </summary>
    public sealed class AcademicStandingRecalculationService
    {
        #region Fields

        private readonly ILogger _logger;
        private readonly IStudentRepo _students;
        private readonly IAlertGateway _alerts;

        #endregion

        #region Policy Constants

        private const decimal GPA_GOOD_MIN = 2.50m;
        private const decimal GPA_PROBATION_MIN = 2.00m;

        private const int FAIL_CREDITS_PROBATION = 6;
        private const int FAIL_CREDITS_SUSPENSION = 12;

        private const int INCOMPLETE_LIMIT = 3;
        private const int MAX_CONSECUTIVE_PROBATION = 2;

        private const int MISCONDUCT_MINOR_THRESHOLD = 1;
        private const int MISCONDUCT_MAJOR_THRESHOLD = 2;

        private const decimal GPA_HONORS_BAND = 3.50m;

        #endregion

        #region Constructor

        public AcademicStandingRecalculationService(
            ILogger logger,
            IStudentRepo students,
            IAlertGateway alerts)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _students = students ?? throw new ArgumentNullException(nameof(students));
            _alerts = alerts ?? throw new ArgumentNullException(nameof(alerts));
        }

        #endregion

        #region Public API

        /// <summary>
        /// Öğrencinin yeni akademik durumunu ve risk bandını hesaplar, gerekli aksiyonları belirler.
        /// </summary>
        public AcademicStandingDecision Recalculate(AcademicStandingContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            var student = _students.GetById(ctx.StudentId);

            var decision = new AcademicStandingDecision
            {
                StudentId = ctx.StudentId,
                PreviousStanding = ctx.PreviousStanding
            };

            var riskBand = ComputeRiskBand(ctx);
            var standing = ComputeNewStanding(ctx, student.Program, riskBand);
            var actions = DeriveActions(ctx, standing, riskBand);

            decision.NewStanding = standing;
            decision.RiskBand = riskBand;
            foreach (var a in actions) decision.Actions.Add(a);

            MaybeEmitAlert(ctx, decision);

            _logger.Info($"AcademicStanding recalculated for {ctx.StudentId}: {ctx.PreviousStanding} -> {standing}, Risk={riskBand}");
            return decision;
        }

        #endregion

        #region Private - Risk & Standing

        /// <summary>
        /// Dönem ve kümülatif GPA, fail edilen kredi, incomplete sayısı,
        /// disiplin olayları ve önceki probation serisini kullanarak öğrencinin
        /// akademik risk bandını (Low/Medium/High/Critical) hesaplar.
        /// </summary>
        private AcademicRiskBand ComputeRiskBand(AcademicStandingContext ctx)
        {
            AcademicRiskBand band = AcademicRiskBand.Low;

            // GPA katmanı
            if (ctx.TermGpa < GPA_PROBATION_MIN || ctx.CumulativeGpa < GPA_PROBATION_MIN)
            {
                band = Elevate(band, AcademicRiskBand.High);
            }
            else if (ctx.TermGpa < GPA_GOOD_MIN || ctx.CumulativeGpa < GPA_GOOD_MIN)
            {
                band = Elevate(band, AcademicRiskBand.Medium);
            }
            else if (ctx.CumulativeGpa >= GPA_HONORS_BAND)
            {
                band = Lower(band, AcademicRiskBand.Low);
            }

            // Fail kredi katmanı
            if (ctx.FailedCredits >= FAIL_CREDITS_SUSPENSION)
            {
                band = Elevate(band, AcademicRiskBand.Critical);
            }
            else if (ctx.FailedCredits >= FAIL_CREDITS_PROBATION)
            {
                band = Elevate(band, AcademicRiskBand.High);
            }

            // Incomplete katmanı
            if (ctx.IncompleteCount > INCOMPLETE_LIMIT)
            {
                band = Elevate(band, AcademicRiskBand.High);
            }

            // Disiplin katmanı
            if (ctx.MisconductIncidents >= MISCONDUCT_MAJOR_THRESHOLD)
            {
                band = AcademicRiskBand.Critical;
            }
            else if (ctx.MisconductIncidents >= MISCONDUCT_MINOR_THRESHOLD)
            {
                band = Elevate(band, AcademicRiskBand.High);
            }

            // Probation serisi
            if (ctx.PreviousStanding == StudentStanding.Probation && ctx.ConsecutiveProbationTerms >= MAX_CONSECUTIVE_PROBATION)
            {
                band = Elevate(band, AcademicRiskBand.Critical);
            }

            // Yaz dönemi toleransı
            if (ctx.TermType == TermType.Summer && band == AcademicRiskBand.Medium)
            {
                band = AcademicRiskBand.Low;
            }

            return band;
        }

        /// <summary>
        /// Hesaplanmış risk bandı, program türü ve öğrencinin önceki akademik durumu
        /// (Good/Probation/Suspension) üzerinden yeni akademik durumu belirler;
        /// ağır disiplin, yüksek fail yükü ve probation zinciri gibi faktörleri
        /// suspension lehine yorumlar.
        /// </summary>
        private StudentStanding ComputeNewStanding(
            AcademicStandingContext ctx,
            ProgramType program,
            AcademicRiskBand band)
        {
            // ağır disiplin
            if (ctx.MisconductIncidents >= MISCONDUCT_MAJOR_THRESHOLD)
            {
                return StudentStanding.Suspension;
            }

            // üst üste probation
            if (ctx.PreviousStanding == StudentStanding.Probation &&
                ctx.ConsecutiveProbationTerms >= MAX_CONSECUTIVE_PROBATION &&
                band >= AcademicRiskBand.High)
            {
                return StudentStanding.Suspension;
            }

            // GPA + fail krediler
            if (ctx.CumulativeGpa < GPA_PROBATION_MIN ||
                ctx.FailedCredits >= FAIL_CREDITS_SUSPENSION)
            {
                return StudentStanding.Suspension;
            }

            if (ctx.CumulativeGpa < GPA_GOOD_MIN ||
                ctx.FailedCredits >= FAIL_CREDITS_PROBATION)
            {
                return StudentStanding.Probation;
            }

            // lisansüstü için daha sıkı yorum
            if (program == ProgramType.Graduate && ctx.CumulativeGpa < GPA_HONORS_BAND &&
                band >= AcademicRiskBand.Medium)
            {
                return StudentStanding.Probation;
            }

            // recovery
            if (ctx.PreviousStanding == StudentStanding.Probation &&
                ctx.TermGpa >= GPA_GOOD_MIN &&
                ctx.FailedCredits == 0)
            {
                return StudentStanding.Good;
            }

            return StudentStanding.Good;
        }

        /// <summary>
        /// Yeni akademik duruma ve risk bandına göre uygulanması gereken aksiyonları
        /// (kredi sınırlama, danışman/dekan görüşmesi, early-alert, danışmanlık vb.)
        /// türetir ve bir aksiyon listesi olarak döndürür.
        /// </summary>
        private IList<StandingAction> DeriveActions(
            AcademicStandingContext ctx,
            StudentStanding newStanding,
            AcademicRiskBand band)
        {
            var list = new List<StandingAction>();

            if (newStanding == StudentStanding.Suspension)
            {
                list.Add(StandingAction.BlockNewEnrollment);
                list.Add(StandingAction.RequireDeanMeeting);
            }
            else if (newStanding == StudentStanding.Probation)
            {
                list.Add(StandingAction.LimitCredits);
                list.Add(StandingAction.RequireAdvisorMeeting);
            }

            if (band == AcademicRiskBand.Critical)
            {
                list.Add(StandingAction.FlagForEarlyAlert);
            }
            else if (band == AcademicRiskBand.High)
            {
                list.Add(StandingAction.OfferTutoring);
            }

            if (ctx.IncompleteCount > INCOMPLETE_LIMIT)
            {
                list.Add(StandingAction.ReviewIncompleteContracts);
            }

            if (ctx.MisconductIncidents > 0)
            {
                list.Add(StandingAction.NotifyStudentAffairs);
            }

            return list;
        }

        #endregion

        #region Private - Alert & helpers
        /// <summary>
        /// Risk bandı High veya Critical ise, öğrencinin durumu için bir Alert nesnesi
        /// oluşturur, IAlertGateway üzerinden kaydeder ve gerekiyorsa escalasyon (paging)
        /// çağrısını tetikler.
        /// </summary>
        private void MaybeEmitAlert(AcademicStandingContext ctx, AcademicStandingDecision decision)
        {
            if (decision.RiskBand < AcademicRiskBand.High) return;

            var alert = new Alert
            {
                Id = $"AST-{ctx.StudentId}-{ctx.TermId}",
                Code = AlertCode.AcademicRisk,
                Message = $"Academic risk {decision.RiskBand} for {ctx.StudentId}, Standing={decision.NewStanding}",
                Severity = decision.RiskBand == AcademicRiskBand.Critical
                    ? AlertSeverity.Critical
                    : AlertSeverity.Warning,
                Escalate = decision.RiskBand == AcademicRiskBand.Critical
            };

            _alerts.Record(alert);

            if (alert.Escalate)
            {
                _alerts.Escalate(alert);
            }
        }
        /// <summary>
        /// Mevcut risk bandını, hedef band ile karşılaştırarak daha yüksek olanı seçer;
        /// risk seviyesini yukarı doğru güncellemek için kullanılır.
        /// </summary>
        private static AcademicRiskBand Elevate(AcademicRiskBand current, AcademicRiskBand target)
        {
            return (AcademicRiskBand)Math.Max((int)current, (int)target);
        }
        /// <summary>
        /// Mevcut risk bandını, hedef band ile karşılaştırarak daha düşük olanı seçer;
        /// belirli durumlarda risk seviyesini aşağı çekmek için kullanılır.
        /// </summary>
        private static AcademicRiskBand Lower(AcademicRiskBand current, AcademicRiskBand target)
        {
            return (AcademicRiskBand)Math.Min((int)current, (int)target);
        }

        #endregion
    }
}
