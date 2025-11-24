using System;
using System.Collections.Generic;
using University.Lms.Ports;
using UniversityLessonSelectionSystem.Domain.Enums;
using UniversityLessonSelectionSystem.Domain.SLAAndHealthMonitor;
using UniversityLessonSelectionSystem.Ports.SLAAndHealthMonitor;

namespace University.Lms.Services
{
    /// <summary>
    /// SLAAndHealthMonitorService:
    /// Sistemin performans, hata oranı, kuyruk yükü, veri tabanı gecikmesi, cache başarımı,
    /// kimlik doğrulama başarısızlıkları, dependency sağlık durumu, surge (ani yoğunluk),
    /// flap (sürekli ihlal-normal dönüşümü) ve hata bütçesi tüketimini SLA/SLO eşiklerine göre
    /// kontrol ederek uyarı (Alert) üretir. MutePolicy ile sessize alma kuralları uygulanır.
    /// Ayrıca otomatik eskalasyon, akademik faza göre esnetilmiş eşikler ve paging politikası desteği sunar.
    /// Yüksek karmaşıklık, bağımsız ve test edilebilir kontrol metotlarına bölünmüş şekilde sağlanır.
    /// </summary>
    public sealed class SLAAndHealthMonitorService
    {
        #region Fields
        private readonly ILogger _logger;
        private readonly IAlertGateway _alerts;
        #endregion

        #region Static Thresholds

        private const int LATENCY_P95_WARN_MS = 1500;
        private const int LATENCY_P95_ALERT_MS = 2500;

        private const int BACKLOG_WAITLIST_WARN = 200;
        private const int BACKLOG_WAITLIST_ALERT = 500;

        private const int EMAIL_QUEUE_WARN = 1000;
        private const int EMAIL_QUEUE_ALERT = 3000;

        private const decimal ERROR_RATIO_WARN = 0.02m;
        private const decimal ERROR_RATIO_ALERT = 0.05m;

        private const decimal SURGE_MULTIPLIER_WARN = 2.0m;
        private const decimal SURGE_MULTIPLIER_ALERT = 3.5m;

        private const int AUTO_ESCALATE_AFTER_MIN = 15;

        private const int DB_LAG_WARN_SEC = 45;
        private const int DB_LAG_ALERT_SEC = 120;

        private const decimal CACHE_HIT_WARN = 0.85m;
        private const decimal CACHE_HIT_ALERT = 0.70m;

        private const decimal LOGIN_FAIL_WARN = 0.05m;
        private const decimal LOGIN_FAIL_ALERT = 0.10m;

        private const int FLAP_WINDOW_MIN = 10;
        private const int FLAP_MIN_TOGGLES = 3;

        private const decimal ERROR_BUDGET_DAILY = 0.01m;     // 1% per day
        private const decimal BURN_RATE_ALERT = 2.0m;         // 2x daily budget

        #endregion

        #region Phase Modifiers

        private static readonly Dictionary<AcademicPhase, int> PhaseLatencyRelax =
            new Dictionary<AcademicPhase, int>
            {
                { AcademicPhase.Normal, 0 },
                { AcademicPhase.Registration, 400 }, // ms relaxation
                { AcademicPhase.AddDrop, 300 }
            };

        #endregion

        #region Constructor
        public SLAAndHealthMonitorService(ILogger logger, IAlertGateway alerts)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _alerts = alerts ?? throw new ArgumentNullException(nameof(alerts));
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Evaluates snapshot and emits alerts/escalations; returns compiled report.
        /// </summary>
        public HealthReport Evaluate(OpsSnapshot s, MutePolicy mutes)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            if (mutes == null) throw new ArgumentNullException(nameof(mutes));

            var report = new HealthReport();

            if (s.MaintenanceActive && !s.AllowAlertsDuringMaintenance)
            {
                _logger.Info("Maintenance active; alerts muted.");
                return report;
            }

            CheckLatency(s, mutes, report);
            CheckBacklogs(s, mutes, report);
            CheckErrors(s, mutes, report);
            CheckSurges(s, mutes, report);
            CheckDbLag(s, mutes, report);
            CheckCache(s, mutes, report);
            CheckLoginFailures(s, mutes, report);
            CheckThirdParty(s, mutes, report);
            CheckFlapping(s, mutes, report);
            CheckErrorBudgetBurn(s, mutes, report);
            CheckRedundancyPaging(s, mutes, report);

            _logger.Info($"HealthMonitor: {report.Alerts.Count} alert(s).");
            return report;
        }

        #endregion

        #region Private Methods

        #region Checks
        /// <summary>
        /// Faz (AcademicPhase) bazlı latency eşiklerini rahatlatmak için latency ölçümünden
        /// ilgili faza ait ms tolerans değerini çıkarır; muafiyet sonrası kritik/seviye
        /// uyarı eşiğini geçen durumlarda Alert üretir.
        /// </summary>
        private void CheckLatency(OpsSnapshot s, MutePolicy m, HealthReport r)
        {
            if (m.MuteLatency) return;
            bool result = PhaseLatencyRelax.TryGetValue(s.Phase, out int relax);
            int p95 = s.RegistrationP95Ms - relax;

            if (p95 >= LATENCY_P95_ALERT_MS)
                AddAlert(r, AlertSeverity.Critical, AlertCode.RegistrationLatency, "RegistrationP95 high", AutoEscalate(s));
            else if (p95 >= LATENCY_P95_WARN_MS)
                AddAlert(r, AlertSeverity.Warning, AlertCode.RegistrationLatency, "RegistrationP95 elevated", false);
        }
        /// <summary>
        /// Waitlist ve email kuyruk derinliklerini ikili eşik (warning/alert) bazlı kontrol eder;
        /// mute edilmemiş metriklerde yığılma eşiklerini geçtiğinde ilgili tipte Alert üretir.
        /// </summary>
        private void CheckBacklogs(OpsSnapshot s, MutePolicy m, HealthReport r)
        {
            if (!m.MuteWaitlistBacklog)
            {
                if (s.WaitlistBacklog >= BACKLOG_WAITLIST_ALERT)
                    AddAlert(r, AlertSeverity.Critical, AlertCode.WaitlistBacklog, "Waitlist backlog high", AutoEscalate(s));
                else if (s.WaitlistBacklog >= BACKLOG_WAITLIST_WARN)
                    AddAlert(r, AlertSeverity.Warning, AlertCode.WaitlistBacklog, "Waitlist backlog elevated", false);
            }

            if (!m.MuteEmailQueue)
            {
                if (s.EmailQueueDepth >= EMAIL_QUEUE_ALERT)
                    AddAlert(r, AlertSeverity.Critical, AlertCode.EmailQueue, "Email queue high", AutoEscalate(s));
                else if (s.EmailQueueDepth >= EMAIL_QUEUE_WARN)
                    AddAlert(r, AlertSeverity.Warning, AlertCode.EmailQueue, "Email queue elevated", false);
            }
        }
        /// <summary>
        /// Başarısız istek oranını (ErrorRatio) hesaplayarak warning veya critical seviyede
        /// sistem hata yoğunluğu uyarıları üretir; mute edilmişse ölçümü atlar.
        /// </summary>
        private void CheckErrors(OpsSnapshot s, MutePolicy m, HealthReport r)
        {
            if (m.MuteErrors) return;

            var ratio = ErrorRatio(s);
            if (ratio >= ERROR_RATIO_ALERT)
                AddAlert(r, AlertSeverity.Critical, AlertCode.ErrorRatio, $"Error ratio {ratio:P1}", AutoEscalate(s));
            else if (ratio >= ERROR_RATIO_WARN)
                AddAlert(r, AlertSeverity.Warning, AlertCode.ErrorRatio, $"Error ratio {ratio:P1}", false);
        }
        /// <summary>
        /// Sistem trafiğinin normalin üzerinde (SURGE_MULTIPLIER_WARN/ALERT) olup olmadığını kontrol eder.
        /// Trafik çarpanı aşırıysa Alert üretir; mute edilmişse ölçümü atlar.
        /// </summary>
        private void CheckSurges(OpsSnapshot s, MutePolicy m, HealthReport r)
        {
            if (m.MuteSurges) return;

            var mult = s.SurgeMultiplier;
            if (mult >= SURGE_MULTIPLIER_ALERT)
                AddAlert(r, AlertSeverity.Critical, AlertCode.TrafficSurge, "Traffic surge high", AutoEscalate(s));
            else if (mult >= SURGE_MULTIPLIER_WARN)
                AddAlert(r, AlertSeverity.Warning, AlertCode.TrafficSurge, "Traffic surge elevated", false);
        }
        /// <summary>
        /// Veri tabanı replikasyon gecikmesini (sec) kontrol ederek warning veya alert seviyesinde uyarı oluşturur.
        /// Kritiklik durumunda otomatik eskalasyon tetiklenebilir.
        /// </summary>
        private void CheckDbLag(OpsSnapshot s, MutePolicy m, HealthReport r)
        {
            if (m.MuteDbLag) return;

            if (s.DbReplicationLagSec >= DB_LAG_ALERT_SEC)
                AddAlert(r, AlertSeverity.Critical, AlertCode.DbLag, "DB replication lag high", AutoEscalate(s));
            else if (s.DbReplicationLagSec >= DB_LAG_WARN_SEC)
                AddAlert(r, AlertSeverity.Warning, AlertCode.DbLag, "DB replication lag elevated", false);
        }
        /// <summary>
        /// Cache hit oranının (CACHE_HIT_WARN/ALERT) altına düşüp düşmediğini kontrol ederek
        /// cache verimliliği düşüşünde uyarı üretir; mute edilmişse ölçümü atlar.
        /// </summary>
        private void CheckCache(OpsSnapshot s, MutePolicy m, HealthReport r)
        {
            if (m.MuteCache) return;

            var hit = s.CacheHitRatio;
            if (hit <= CACHE_HIT_ALERT)
                AddAlert(r, AlertSeverity.Critical, AlertCode.CacheHit, "Cache hit ratio low", AutoEscalate(s));
            else if (hit <= CACHE_HIT_WARN)
                AddAlert(r, AlertSeverity.Warning, AlertCode.CacheHit, "Cache hit ratio reduced", false);
        }
        /// <summary>
        /// Kullanıcı oturum açma başarısızlık oranını kontrol eder; yüksek hata durumunda
        /// güvenlik veya kullanım problemi olarak Alert üretir.
        /// </summary>
        private void CheckLoginFailures(OpsSnapshot s, MutePolicy m, HealthReport r)
        {
            if (m.MuteAuth) return;

            var fail = s.LoginFailureRatio;
            if (fail >= LOGIN_FAIL_ALERT)
                AddAlert(r, AlertSeverity.Critical, AlertCode.AuthFailures, "Login failure ratio high", AutoEscalate(s));
            else if (fail >= LOGIN_FAIL_WARN)
                AddAlert(r, AlertSeverity.Warning, AlertCode.AuthFailures, "Login failure ratio elevated", false);
        }
        /// <summary>
        /// Sistem tarafından kullanılan üçüncü parti servislerin durumlarını (Down, Degraded) ve
        /// kritiklik seviyelerini inceleyerek ilgili alert türlerini üretir.
        /// </summary>
        private void CheckThirdParty(OpsSnapshot s, MutePolicy m, HealthReport r)
        {
            if (m.MuteThirdParty) return;

            // any critical external dependency down?
            foreach (var dep in s.Dependencies)
            {
                if (dep.State == DependencyState.Down && dep.Criticality == DependencyCriticality.Critical)
                    AddAlert(r, AlertSeverity.Critical, AlertCode.ThirdParty, $"Dependency down: {dep.Kind}", AutoEscalate(s));
                else if (dep.State == DependencyState.Degraded && dep.Criticality != DependencyCriticality.Low)
                    AddAlert(r, AlertSeverity.Warning, AlertCode.ThirdParty, $"Dependency degraded: {dep.Kind}", false);
            }
        }
        /// <summary>
        /// Belirli zaman aralığında ihlal-normal geçiş sayısı çok fazla ise (flapping),
        /// sistemde kararsızlık yaşandığını bildirir. AlertSeverity.Warning ile işaretlenir.
        /// </summary>
        private void CheckFlapping(OpsSnapshot s, MutePolicy m, HealthReport r)
        {
            if (m.MuteFlapping) return;

            if (s.BreachTogglesLastMinutes >= FLAP_MIN_TOGGLES && s.FlappingWindowMinutes <= FLAP_WINDOW_MIN)
                AddAlert(r, AlertSeverity.Warning, AlertCode.Flapping, "Frequent breach/recover toggles", false);
        }
        /// <summary>
        /// Günlük hata bütçesi (error budget) tüketim oranını takip eder.
        /// Günlük bütçenin çok üzerinde (BURN_RATE_ALERT) tüketim varsa kritik uyarı üretir.
        /// </summary>
        private void CheckErrorBudgetBurn(OpsSnapshot s, MutePolicy m, HealthReport r)
        {
            if (m.MuteErrorBudget) return;

            // projected burn vs daily budget
            var projected = s.ProjectedDailyErrorRatio;
            var burnRate = ERROR_BUDGET_DAILY == 0m ? 0m : (projected / ERROR_BUDGET_DAILY);
            if (burnRate >= BURN_RATE_ALERT)
                AddAlert(r, AlertSeverity.Critical, AlertCode.ErrorBudget, $"Error budget burn {burnRate:0.0}x", AutoEscalate(s));
        }
        /// <summary>
        /// Redundancy durumunu ve paging policy koşullarını kontrol ederek
        /// tek node, no redundancy ve paging yönetimi hakkında uyarı üretir.
        /// </summary>
        private void CheckRedundancyPaging(OpsSnapshot s, MutePolicy m, HealthReport r)
        {
            if (m.MuteRedundancy) return;

            if (s.RedundancyState == RedundancyState.SingleNode && s.PagingPolicy == PagingPolicy.PagePrimaryOnSingleNode)
                AddAlert(r, AlertSeverity.Warning, AlertCode.Redundancy, "Running single-node redundancy", false);

            if (s.RedundancyState == RedundancyState.NoRedundancy && s.PagingPolicy != PagingPolicy.Suppress)
                AddAlert(r, AlertSeverity.Critical, AlertCode.Redundancy, "No redundancy", true);
        }

        #endregion

        #region Helpers
        /// <summary>
        /// Redundancy durumunu ve paging policy koşullarını kontrol ederek
        /// tek node, no redundancy ve paging yönetimi hakkında uyarı üretir.
        /// </summary>
        private static decimal ErrorRatio(OpsSnapshot s)
        {
            var total = s.RequestsSucceeded + s.RequestsFailed;
            return total == 0 ? 0m : (decimal)s.RequestsFailed / total;
        }
        /// <summary>
        /// Sürekli ihlal durumu (breach) yaşanıyorsa veya paging policy
        /// ForceEscalation ise Alert’lerin otomatik olarak daha üst seviyeye
        /// yönlendirilmesini (escalate) tetikler.
        /// </summary>
        private bool AutoEscalate(OpsSnapshot s) =>
            s.MinutesSinceFirstBreach >= AUTO_ESCALATE_AFTER_MIN || s.PagingPolicy == PagingPolicy.ForceEscalation;
        /// <summary>
        /// Üretilen Alert nesnesini HealthReport içine ekler, aynı zamanda
        /// IAlertGateway aracılığıyla sisteme (persist, dashboard, vs.)
        /// kayıt eder. Eskalasyon bayrağını da taşıyabilir.
        /// </summary>
        private void AddAlert(HealthReport r, AlertSeverity sev, AlertCode code, string msg, bool escalate)
        {
            var alert = new Alert { Severity = sev, Code = code, Message = msg, Escalate = escalate };
            r.Alerts.Add(alert);
            _alerts.Record(alert);
        }

        #endregion

        #endregion
    } 
}
