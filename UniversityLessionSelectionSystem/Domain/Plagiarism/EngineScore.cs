using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessonSelectionSystem.Domain.Plagiarism
{
    // Extend EngineScore with category (if not already)
    public sealed class EngineScore
    {
        public decimal Score { get; set; }      // 0..1
        public decimal Confidence { get; set; } // 0..1
        public EngineCategory Category { get; set; }
    }

}
