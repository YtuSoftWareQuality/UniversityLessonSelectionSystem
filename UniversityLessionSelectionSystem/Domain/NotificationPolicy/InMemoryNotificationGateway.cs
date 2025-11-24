using System;
using System.Collections.Generic;
using University.Lms.Ports;
using UniversityLessonSelectionSystem.Ports.NotificationPolicy;

namespace University.Lms.Domain
{
    /// <summary>
    /// Bildirimleri (waitlist promotion vs.) sadece bellekte kaydeden ve loglayan gateway.
    /// Testlerde hangi bildirimlerin gönderildiği doğrulanabilir.
    /// </summary>
    public sealed class InMemoryNotificationGateway : INotificationGateway
    {
        private readonly ILogger _logger;

        public IList<string> Notifications { get; } = new List<string>();

        public InMemoryNotificationGateway(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void NotifyWaitlistPromotion(string studentId, string sectionId)
        {
            var msg = $"WaitlistPromotion: Student={studentId} Section={sectionId}";
            Notifications.Add(msg);
            _logger.Info(msg);
        }
    }
}