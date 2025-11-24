using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessionSelectionSystem.Domain
{
    public sealed class RegistrationThrottleContext
    {
        public string UserId { get; set; }
        public string TermId { get; set; }
        public int RequestSequence { get; set; }

        public Role Role { get; set; }
        public AcademicRiskBand AcademicRiskBand { get; set; }
        public TermPhase TermPhase { get; set; }

        public decimal ErrorRatio { get; set; }
        public decimal LatencyMs { get; set; }
        public int BacklogSize { get; set; }

        public int RecentThrottleCount { get; set; }
        public int RequestHourUtc { get; set; }
    }
}
