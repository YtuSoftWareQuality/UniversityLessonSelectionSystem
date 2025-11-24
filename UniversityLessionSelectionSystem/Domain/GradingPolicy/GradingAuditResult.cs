using System.Collections.Generic;

namespace UniversityLessonSelectionSystem.Domain.GradingPolicy
{
    public sealed class GradingAuditResult
    {
        public IList<string> Violations { get; } = new List<string>();
        public IList<string> Warnings { get; } = new List<string>();
    }
}
