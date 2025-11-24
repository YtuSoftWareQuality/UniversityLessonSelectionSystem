using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessionSelectionSystem.Domain
{
    public sealed class RegistrationThrottleDecision
    {
        public string UserId { get; set; }
        public Role Role { get; set; }
        public RegistrationThrottleLevel Level { get; set; }
        public bool RequireCaptcha { get; set; }
        public int DelaySeconds { get; set; }
    }
}
