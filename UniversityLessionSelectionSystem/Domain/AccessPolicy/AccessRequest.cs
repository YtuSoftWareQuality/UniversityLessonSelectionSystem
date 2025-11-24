using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessonSelectionSystem.Domain.AccessPolicy
{
    public sealed class AccessRequest
    {
        public Role Role { get; set; }
        public Operation Operation { get; set; }
        public AccessContext Context { get; set; }
        public Department Department { get; set; }
        public TermPhase TermPhase { get; set; }
        public int LocalHour { get; set; }
        public bool BreakGlassRequested { get; set; }
    }
}
