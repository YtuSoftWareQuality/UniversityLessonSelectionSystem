using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessonSelectionSystem.Domain.GradingPolicy
{
    public sealed class RetakeRecord
    {
        public LetterGrade Grade { get; set; }
        public int TermOrder { get; set; } // higher = more recent
    }
}
