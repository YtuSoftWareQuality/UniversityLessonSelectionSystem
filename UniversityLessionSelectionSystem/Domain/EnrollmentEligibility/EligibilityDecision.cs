using System.Collections.Generic; 
using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessonSelectionSystem.Domain.EnrollmentEligibility
{
    /// <summary>
    /// Rich decision object for enrollment eligibility.
    /// </summary>
    public sealed class EligibilityDecision
    {
        public ApprovalOutcome Outcome { get; set; } = ApprovalOutcome.None;
        public IList<string> Reasons { get; } = new List<string>();
        public IList<string> Warnings { get; } = new List<string>();
        public IList<string> MissingPrereqs { get; } = new List<string>();
        public IList<RequiredAction> RequiredActions { get; } = new List<RequiredAction>();
    }
}
