using UniversityLessonSelectionSystem.Domain.AdvisorApproval;
using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessonSelectionSystem.Ports.AdvisorApproval
{
    public interface IApprovalGateway
    {
        void RecordAudit(string requestId, string entry);
        void NotifyNextActor(WorkflowState next, ApprovalRequest request);
        void NotifyCompletion(ApprovalRequest request, bool approved, string reasonIfAny);
    }
}
