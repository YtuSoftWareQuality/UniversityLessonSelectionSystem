using University.Lms.Ports;
using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessonSelectionSystem.Domain.PrerequisiteEvaluation
{
    public sealed class PrereqPolicyHints
    {
        public GradeBand MinGrade { get; set; } = GradeBand.C;
        public bool AllowConcurrentEnrollment { get; set; }
        public bool AllowTransferSubstitution { get; set; }
        public bool ForceExpiryGate { get; set; }
        public DeptExpiryPolicy DeptExpiryPolicy { get; set; } = DeptExpiryPolicy.Strict;
    }
}
