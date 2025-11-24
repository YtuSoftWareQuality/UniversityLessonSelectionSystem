using System.Collections.Generic;
 
using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessonSelectionSystem.Domain.NotificationPolicy
{
    public sealed class NotificationContext
    {
        public string StudentId { get; set; }
        public StudentStanding StudentStanding { get; set; }
        public ResidencyStatus Residency { get; set; }
        public TermPhase TermPhase { get; set; }
        public NotificationEventType EventType { get; set; }
        public int LocalHour { get; set; }
        public int UtcNowUnix { get; set; }
        public IList<NotificationChannel> EnabledChannels { get; set; } = new List<NotificationChannel>();
        public IList<SentNotification> History { get; set; } = new List<SentNotification>();
        public bool AllowEscalationFallback { get; set; }
    }
}
