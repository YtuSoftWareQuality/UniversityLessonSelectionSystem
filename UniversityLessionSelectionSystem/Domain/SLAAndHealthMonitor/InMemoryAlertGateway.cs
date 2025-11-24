using System;
using System.Collections.Generic; 
using University.Lms.Ports; // ILogger
using UniversityLessonSelectionSystem.Domain.Enums;
using UniversityLessonSelectionSystem.Domain.SLAAndHealthMonitor; // Alert
using UniversityLessonSelectionSystem.Ports.SLAAndHealthMonitor;  // IAlertGateway

namespace University.Lms.Domain
{
    /// <summary>
    /// IAlertGateway için in-memory implementasyon:
    /// - Üretilen tüm Alert nesnelerini bellekte saklar,
    /// - Record / RecordBatch ile listeyi doldurur,
    /// - Acknowledge edilen alert ID'lerini takip eder,
    /// - Escalate çağrılarında log üzerinden “escalation” simüle eder.
    /// 
    /// SLAAndHealthMonitorService gibi servislerin ürettiği uyarıları
    /// demo / test ortamında gözlemlemek için uygundur.
    /// </summary>
    public sealed class InMemoryAlertGateway : IAlertGateway
    {
        private readonly ILogger _logger;

        private readonly List<Alert> _alerts = new List<Alert>();
        private readonly HashSet<string> _acknowledged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Bellekte tutulan tüm alert kayıtlarını dışarıya salt-okunur olarak verir.
        /// Unit testlerde doğrulama için kullanılabilir.
        /// </summary>
        public IReadOnlyList<Alert> Alerts => _alerts.AsReadOnly();

        /// <summary>
        /// Acknowledge edilmiş alert ID'lerinin listesini döner.
        /// </summary>
        public IReadOnlyCollection<string> AcknowledgedAlertIds => _acknowledged;

        public InMemoryAlertGateway(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Tek bir alert'i belleğe kaydeder ve log'a yazar.
        /// </summary>
        public void Record(Alert alert)
        {
            if (alert == null) throw new ArgumentNullException(nameof(alert));

            _alerts.Add(alert);

            var prefix = alert.Severity.ToString().ToUpperInvariant();
            var msg = $"[ALERT:{prefix}] Code={alert.Code} Message={alert.Message} Escalate={alert.Escalate}";

            // Severity'e göre Info/Warn ayrımı yapılabilir
            if (alert.Severity == AlertSeverity.Critical)
                _logger.Warn(msg);
            else
                _logger.Info(msg);
        }

        /// <summary>
        /// Birden çok alert'i toplu olarak kaydeder.
        /// Varsayılan implementasyon tek tek Record çağırır.
        /// </summary>
        public void RecordBatch(IEnumerable<Alert> alerts)
        {
            if (alerts == null) throw new ArgumentNullException(nameof(alerts));

            // Basit default: tek tek işliyoruz
            foreach (var alert in alerts)
            {
                Record(alert);
            }
        }

        /// <summary>
        /// Verilen ID'deki alert için acknowledge (handle edildi) bilgisini kaydeder
        /// ve log'a kim tarafından işlendiğini yazar.
        /// </summary>
        public void Acknowledge(string alertId, string handledBy)
        {
            if (string.IsNullOrWhiteSpace(alertId)) throw new ArgumentNullException(nameof(alertId));

            _acknowledged.Add(alertId);

            var handler = string.IsNullOrWhiteSpace(handledBy) ? "unknown" : handledBy;
            _logger.Info($"Alert acknowledged: Id={alertId} HandledBy={handler}");
        }

        /// <summary>
        /// Kritik alert'ler için escalation mekanizmasını simüle eder;
        /// gerçek sistemde burası paging / incident yönetim sistemine giden adapter olur.
        /// </summary>
        public void Escalate(Alert alert)
        {
            if (alert == null) throw new ArgumentNullException(nameof(alert));

            // Eğer henüz kaydedilmediyse, önce kaydedelim
            if (!_alerts.Contains(alert))
            {
                Record(alert);
            }

            _logger.Warn($"[ESCALATE] Code={alert.Code} Severity={alert.Severity} Message={alert.Message}");
        }
    }
}
