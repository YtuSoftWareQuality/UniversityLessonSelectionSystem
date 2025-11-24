using System;
using University.Lms.Ports;
using UniversityLessionSelectionSystem.Domain; 
using UniversityLessonSelectionSystem.Domain.Enums; 
using UniversityLessonSelectionSystem.Domain.SLAAndHealthMonitor;
using UniversityLessonSelectionSystem.Ports.SLAAndHealthMonitor;

namespace University.Lms.Services
{
    /// <summary>
    /// Dönem kayıt penceresinde;
    /// - Term fazı (Registration, AddDrop, PreRegistration),
    /// - Saat dilimi (peak/off-peak),
    /// - SLA/health snapshot'ı (error ratio, latency, backlog),
    /// - Kullanıcının rolü ve akademik risk bandı,
    /// - Kullanıcının önceki throttle geçmişi,
    /// gibi faktörleri değerlendirerek kayıt isteklerini Allow / SoftThrottle / HardThrottle / Block
    /// şeklinde sınıflandıran servistir. SLAAndHealthMonitor çıktılarıyla birlikte çalışır ve
    /// Alert üreterek durumu raporlayabilir.
    /// </summary>
    public sealed class RegistrationWindowThrottleService
    {
        #region Fields

        private readonly ILogger _logger;
        private readonly IAlertGateway _alerts;

        #endregion

        #region Policy Constants

        private const decimal ERROR_RATIO_SOFT = 0.05m;
        private const decimal ERROR_RATIO_HARD = 0.10m;

        private const decimal LATENCY_SOFT_MS = 800m;
        private const decimal LATENCY_HARD_MS = 1500m;

        private const int BACKLOG_SOFT = 200;
        private const int BACKLOG_HARD = 400;

        private const int MAX_RECENT_THROTTLES_BEFORE_BLOCK = 3;

        #endregion

        #region Constructor

        public RegistrationWindowThrottleService(ILogger logger, IAlertGateway alerts)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _alerts = alerts ?? throw new ArgumentNullException(nameof(alerts));
        }

        #endregion

        #region Public API

        /// <summary>
        /// Kayıt isteği için throttling kararını üretir.
        /// </summary>
        public RegistrationThrottleDecision Evaluate(RegistrationThrottleContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            var decision = new RegistrationThrottleDecision
            {
                UserId = ctx.UserId,
                Role = ctx.Role
            };

            var level = RegistrationThrottleLevel.Allow;

            // 1) SLA snapshot’ı bazlı sistemsel baskı
            level = Combine(level, LevelFromSla(ctx));

            // 2) Term fazı ve peak saat etkisi
            level = Combine(level, LevelFromPhaseAndHour(ctx));

            // 3) Kullanıcı rolü ve risk bandı
            level = Combine(level, LevelFromUserRisk(ctx));

            // 4) Önceki throttle geçmişi → block’a kadar yükseltebilir
            level = Combine(level, LevelFromHistory(ctx));

            decision.Level = level;
            decision.RequireCaptcha = ShouldRequireCaptcha(ctx, level);
            decision.DelaySeconds = ComputeDelaySeconds(level);

            MaybeEmitAlert(ctx, decision);

            _logger.Info($"Registration throttle for {ctx.UserId}: Level={decision.Level}, Delay={decision.DelaySeconds}s, Captcha={decision.RequireCaptcha}");
            return decision;
        }

        #endregion

        #region Private - Level components
        /// <summary>
        /// SLA snapshot’ı (hata oranı, latency, backlog büyüklüğü) üzerinden
        /// sistemsel baskıyı değerlendirip, Allow/SoftThrottle/HardThrottle
        /// seviyelerinden birini döner; en yüksek eşikleri aşan durumlarda
        /// en sert throttle seviyesini seçer.
        /// </summary>
        private RegistrationThrottleLevel LevelFromSla(RegistrationThrottleContext ctx)
        {
            var level = RegistrationThrottleLevel.Allow;

            if (ctx.ErrorRatio >= ERROR_RATIO_HARD || ctx.LatencyMs >= LATENCY_HARD_MS || ctx.BacklogSize >= BACKLOG_HARD)
            {
                level = RegistrationThrottleLevel.HardThrottle;
            }
            else if (ctx.ErrorRatio >= ERROR_RATIO_SOFT || ctx.LatencyMs >= LATENCY_SOFT_MS || ctx.BacklogSize >= BACKLOG_SOFT)
            {
                level = RegistrationThrottleLevel.SoftThrottle;
            }

            return level;
        }
        /// <summary>
        /// Dönem fazı (Registration, PreRegistration, AddDrop, Finals) ile isteğin
        /// yapıldığı saat bilgisini (UTC) birlikte yorumlayarak, faz ve peak saatlere
        /// göre ek bir throttle seviyesi (özellikle Hard veya Block) döner.
        /// </summary>
        private RegistrationThrottleLevel LevelFromPhaseAndHour(RegistrationThrottleContext ctx)
        {
            if (ctx.TermPhase == TermPhase.Finals)
            {
                return RegistrationThrottleLevel.Block;
            }

            if (ctx.TermPhase == TermPhase.PreRegistration)
            {
                if (IsPeakHour(ctx.RequestHourUtc))
                {
                    return RegistrationThrottleLevel.SoftThrottle;
                }
                return RegistrationThrottleLevel.Allow;
            }

            if (ctx.TermPhase == TermPhase.Registration)
            {
                if (IsPeakHour(ctx.RequestHourUtc))
                {
                    return RegistrationThrottleLevel.HardThrottle;
                }
                return RegistrationThrottleLevel.SoftThrottle;
            }

            if (ctx.TermPhase == TermPhase.AddDrop)
            {
                return RegistrationThrottleLevel.SoftThrottle;
            }

            return RegistrationThrottleLevel.Allow;
        }
        /// <summary>
        /// Kullanıcının rolü (student, advisor, registrar, admin) ve akademik risk bandına
        /// göre, role/risk temelli bir throttle seviyesi hesaplar; kritik riskteki
        /// öğrenciler için daha agresif throttle uygular, admin/registrar için
        /// neredeyse her zaman Allow döner.
        /// </summary>
        private RegistrationThrottleLevel LevelFromUserRisk(RegistrationThrottleContext ctx)
        {
            if (ctx.Role == Role.Admin || ctx.Role == Role.Registrar)
            {
                return RegistrationThrottleLevel.Allow;
            }

            if (ctx.Role == Role.Advisor && ctx.AcademicRiskBand >= AcademicRiskBand.High)
            {
                return RegistrationThrottleLevel.SoftThrottle;
            }

            if (ctx.Role == Role.Student)
            {
                if (ctx.AcademicRiskBand == AcademicRiskBand.Critical)
                {
                    return RegistrationThrottleLevel.HardThrottle;
                }
                if (ctx.AcademicRiskBand == AcademicRiskBand.High)
                {
                    return RegistrationThrottleLevel.SoftThrottle;
                }
            }

            return RegistrationThrottleLevel.Allow;
        }
        /// <summary>
        /// Kullanıcının yakın geçmişte aldığı throttle sayısına göre, ekstra bir
        /// throttle seviyesi hesaplar; tekrar eden throttle’larda seviyesi yükseltir,
        /// çok sayıda throttle biriktiğinde Block seviyesine çıkarır.
        /// </summary>
        private RegistrationThrottleLevel LevelFromHistory(RegistrationThrottleContext ctx)
        {
            if (ctx.RecentThrottleCount >= MAX_RECENT_THROTTLES_BEFORE_BLOCK)
            {
                return RegistrationThrottleLevel.Block;
            }

            if (ctx.RecentThrottleCount > 0)
            {
                return RegistrationThrottleLevel.SoftThrottle;
            }

            return RegistrationThrottleLevel.Allow;
        }

        #endregion

        #region Private - Helpers
        /// <summary>
        /// İki throttle seviyesini karşılaştırarak, şiddet olarak daha yüksek olanını döner;
        /// farklı katmanlardan gelen throttle önerilerini birleştirmek için kullanılır.
        /// </summary>
        private static RegistrationThrottleLevel Combine(RegistrationThrottleLevel a, RegistrationThrottleLevel b)
        {
            return (RegistrationThrottleLevel)Math.Max((int)a, (int)b);
        }
        /// <summary>
        /// UTC saatine göre isteğin peak kabul edilen saatlere (örn. 7–11 ve 16–21 aralığı)
        /// denk gelip gelmediğini kontrol eder; peak saatlerde true döner.
        /// </summary>
        private static bool IsPeakHour(int hourUtc)
        {
            // 7–11 UTC ve 16–21 UTC arası peak varsayalım
            if (hourUtc >= 7 && hourUtc <= 11) return true;
            if (hourUtc >= 16 && hourUtc <= 21) return true;
            return false;
        }
        /// <summary>
        /// Hesaplanmış throttle seviyesi, kullanıcının rolü ve risk geçmişine göre
        /// bu istekte CAPTCHA zorunlu tutulup tutulmayacağına karar verir; özellikle
        /// HardThrottle ve yüksek riskli öğrenci için CAPTCHA’yı aktifleştirir.
        /// </summary>
        private static bool ShouldRequireCaptcha(RegistrationThrottleContext ctx, RegistrationThrottleLevel level)
        {
            if (level == RegistrationThrottleLevel.HardThrottle && ctx.Role == Role.Student)
            {
                return true;
            }

            if (level == RegistrationThrottleLevel.SoftThrottle &&
                ctx.RecentThrottleCount > 0 &&
                ctx.AcademicRiskBand >= AcademicRiskBand.High)
            {
                return true;
            }

            return false;
        }
        /// <summary>
        /// Throttle seviyesi (Soft/Hard/Block) için kullanıcının isteğinde uygulanacak
        /// gecikme süresini (saniye cinsinden) hesaplar; Allow için 0, Soft için kısa,
        /// Hard için orta, Block için uzun gecikme döner.
        /// </summary>
        private static int ComputeDelaySeconds(RegistrationThrottleLevel level)
        {
            switch (level)
            {
                case RegistrationThrottleLevel.SoftThrottle:
                    return 2;
                case RegistrationThrottleLevel.HardThrottle:
                    return 5;
                case RegistrationThrottleLevel.Block:
                    return 30;
                default:
                    return 0;
            }
        }
        /// <summary>
        /// Karar sonucu Allow değilse, ilgili kullanıcı için throttle kararını temsil eden
        /// bir Alert nesnesi oluşturur, IAlertGateway üzerinden kaydeder ve Block durumunda
        /// ayrıca Escalate çağrısını tetikler.
        /// </summary>
        private void MaybeEmitAlert(RegistrationThrottleContext ctx, RegistrationThrottleDecision decision)
        {
            if (decision.Level == RegistrationThrottleLevel.Allow) return;

            var alert = new Alert
            {
                Id = $"THR-{ctx.UserId}-{ctx.TermId}-{ctx.RequestSequence}",
                Code = AlertCode.RegistrationThrottle,
                Message = $"Throttle={decision.Level} User={ctx.UserId} Role={ctx.Role} Risk={ctx.AcademicRiskBand}",
                Severity = decision.Level == RegistrationThrottleLevel.Block
                    ? AlertSeverity.Critical
                    : AlertSeverity.Warning,
                Escalate = decision.Level == RegistrationThrottleLevel.Block
            };

            _alerts.Record(alert);
            if (alert.Escalate)
            {
                _alerts.Escalate(alert);
            }
        }

        #endregion
    }
} 
