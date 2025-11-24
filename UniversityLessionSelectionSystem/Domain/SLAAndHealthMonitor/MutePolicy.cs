 

namespace UniversityLessonSelectionSystem.Domain.SLAAndHealthMonitor
{
    public sealed class MutePolicy
    {
        public bool MuteLatency { get; set; }
        public bool MuteWaitlistBacklog { get; set; }
        public bool MuteEmailQueue { get; set; }
        public bool MuteErrors { get; set; }
        public bool MuteSurges { get; set; }
        public bool MuteDbLag { get; set; }
        public bool MuteCache { get; set; }
        public bool MuteAuth { get; set; }
        public bool MuteThirdParty { get; set; }
        public bool MuteFlapping { get; set; }
        public bool MuteErrorBudget { get; set; }
        public bool MuteRedundancy { get; set; }
    }
}
