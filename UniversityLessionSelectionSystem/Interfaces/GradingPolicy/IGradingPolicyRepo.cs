using System; 

namespace UniversityLessonSelectionSystem.Ports.GradingPolicy
{
    public interface IGradingPolicyRepo
    {
        int IncompleteMaxDays();
        bool WithdrawAllowedAfter(DateTime deadlineUtc);
        bool ApprovalChainSatisfied(string changeId);
    }
}
