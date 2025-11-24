using System.Collections.Generic; 

namespace UniversityLessonSelectionSystem.Domain.SLAAndHealthMonitor
{
    public sealed class HealthReport
    {
        public IList<Alert> Alerts { get; } = new List<Alert>();
    }
}
