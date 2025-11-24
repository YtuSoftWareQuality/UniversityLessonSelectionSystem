using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessonSelectionSystem.Domain.SLAAndHealthMonitor
{
    public sealed class DependencySnapshot
    {
        public DependencyKind Kind { get; set; }
        public DependencyState State { get; set; }
        public DependencyCriticality Criticality { get; set; }
    }
}
