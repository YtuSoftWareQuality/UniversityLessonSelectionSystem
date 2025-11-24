using System.Collections.Generic;
using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessonSelectionSystem.Domain.PrerequisiteEvaluation
{
    /// <summary>
    /// Rich outcome for prerequisite evaluation.
    /// </summary>
    public sealed class PrereqDecision
    {
        public PrereqStatus Status { get; set; } = PrereqStatus.Unknown;
        public IList<string> Reasons { get; } = new List<string>();
        public IList<string> Missing { get; } = new List<string>();
        public IList<PrereqRequiredAction> RequiredActions { get; } = new List<PrereqRequiredAction>();
    }
}
