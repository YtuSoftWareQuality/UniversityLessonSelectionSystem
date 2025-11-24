using System; 
using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessonSelectionSystem.Domain.GradingPolicy
{
    public sealed class SpecialMarkRecord
    {
        public SpecialMark Mark { get; set; }
        public DateTime TimestampUtc { get; set; }
    }
}
