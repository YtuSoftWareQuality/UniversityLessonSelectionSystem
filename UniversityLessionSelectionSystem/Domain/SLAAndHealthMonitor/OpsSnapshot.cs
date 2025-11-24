using System.Collections.Generic;
using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessonSelectionSystem.Domain.SLAAndHealthMonitor
{
    public sealed class OpsSnapshot
    {
        public int RegistrationP95Ms { get; set; }
        public int WaitlistBacklog { get; set; }
        public int EmailQueueDepth { get; set; }
        public int RequestsSucceeded { get; set; }
        public int RequestsFailed { get; set; }
        public decimal SurgeMultiplier { get; set; }
        public int MinutesSinceFirstBreach { get; set; }

        public bool MaintenanceActive { get; set; }
        public bool AllowAlertsDuringMaintenance { get; set; }

        public AcademicPhase Phase { get; set; }

        public int DbReplicationLagSec { get; set; }
        public decimal CacheHitRatio { get; set; }          // 0..1
        public decimal LoginFailureRatio { get; set; }      // 0..1

        public int BreachTogglesLastMinutes { get; set; }
        public int FlappingWindowMinutes { get; set; }

        public decimal ProjectedDailyErrorRatio { get; set; }

        public IList<DependencySnapshot> Dependencies { get; set; } = new List<DependencySnapshot>();

        public RedundancyState RedundancyState { get; set; }
        public PagingPolicy PagingPolicy { get; set; }
    }
}
