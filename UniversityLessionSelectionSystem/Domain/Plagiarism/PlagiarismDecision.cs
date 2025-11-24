using System.Collections.Generic;
using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessonSelectionSystem.Domain.Plagiarism
{
    /// <summary>Overall plagiarism decision returned by the orchestration layer.</summary>
    public sealed class PlagiarismDecision
    {
        public PlagiarismLevel Level { get; set; } = PlagiarismLevel.Clean;
        public decimal AggregatedScore { get; set; } // 0..1
        public IList<string> Reasons { get; } = new List<string>();
        public IList<PlagiarismAction> RequiredActions { get; } = new List<PlagiarismAction>();
    }
}
