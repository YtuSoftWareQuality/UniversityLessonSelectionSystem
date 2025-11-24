using System;
using System.Collections.Generic; 
using UniversityLessonSelectionSystem.Ports.GradingPolicy;

namespace University.Lms.Domain
{
    /// <summary>
    /// Not politikası ayarlarını (Incomplete süresi, Withdraw son tarihi, approval chain) tutan basit repo.
    /// </summary>
    public sealed class InMemoryGradingPolicyRepo : IGradingPolicyRepo
    {
        private int _incompleteMaxDays = 30;
        private DateTime _withdrawDeadline = DateTime.UtcNow.AddDays(14);

        private readonly HashSet<string> _approvedGradeChanges = new HashSet<string>();

        public int IncompleteMaxDays()
        {
            return _incompleteMaxDays;
        }

        public bool WithdrawAllowedAfter(DateTime deadlineUtc)
        {
            // Policy: kayıtlı deadline ile karşılaştır
            return deadlineUtc <= _withdrawDeadline;
        }

        public bool ApprovalChainSatisfied(string changeId)
        {
            return _approvedGradeChanges.Contains(changeId);
        }

        public void SeedIncompleteMaxDays(int days)
        {
            _incompleteMaxDays = days;
        }

        public void SeedWithdrawDeadline(DateTime deadlineUtc)
        {
            _withdrawDeadline = deadlineUtc;
        }

        public void SeedApprovedChange(string changeId)
        {
            _approvedGradeChanges.Add(changeId);
        }
    }
}