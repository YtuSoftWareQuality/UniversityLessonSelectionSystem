using System; 

namespace UniversityLessonSelectionSystem.Domain.GradingPolicy
{
    public sealed class GradeChangeRecord
    {
        public string ChangeId { get; set; }
        public DateTime TimestampUtc { get; set; }
    }
}
