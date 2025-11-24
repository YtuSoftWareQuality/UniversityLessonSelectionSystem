using System.Runtime.InteropServices.ComTypes;
using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessonSelectionSystem.Domain.SLAAndHealthMonitor
{
    public sealed class Alert
    {
        public string Id { get; set; }
        public AlertSeverity Severity { get; set; }
        public AlertCode Code { get; set; }
        public string Message { get; set; }
        public bool Escalate { get; set; }
    }
}
