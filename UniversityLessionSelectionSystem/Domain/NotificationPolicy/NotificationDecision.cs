using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessonSelectionSystem.Domain.NotificationPolicy
{
    public sealed class NotificationDecision
    {
        public NotificationChannel Channel { get; set; }
        public NotifyAction Action { get; set; }
        public int RetryAfterSeconds { get; set; }
        public string Reason { get; set; }
    }
}
