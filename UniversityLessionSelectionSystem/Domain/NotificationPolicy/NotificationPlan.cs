using System.Collections.Generic;  

namespace UniversityLessonSelectionSystem.Domain.NotificationPolicy
{
    public sealed class NotificationPlan
    {
        public IList<NotificationDecision> Decisions { get; } = new List<NotificationDecision>();
    }
}
