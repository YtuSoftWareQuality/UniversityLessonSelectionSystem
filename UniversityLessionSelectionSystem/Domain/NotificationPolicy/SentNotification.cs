using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessonSelectionSystem.Domain.NotificationPolicy
{
    public sealed class SentNotification
    {
        public NotificationChannel Channel { get; set; }
        public NotificationEventType EventType { get; set; }
        public int TimestampUnix { get; set; }
    }
}
