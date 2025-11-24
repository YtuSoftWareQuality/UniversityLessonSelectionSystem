using System;
using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessonSelectionSystem.Domain.AdvisorApproval
{
    public sealed class ApprovalRequest
    {
        public string RequestId { get; set; }
        public decimal Gpa { get; set; }
        public WorkflowState State { get; set; } = WorkflowState.PendingAdvisor;
        public StepDecision Decision { get; set; } = StepDecision.None;

        public DateTime SubmittedUtc { get; set; }
        public DateTime LastStepUtc { get; set; }
    }
}
