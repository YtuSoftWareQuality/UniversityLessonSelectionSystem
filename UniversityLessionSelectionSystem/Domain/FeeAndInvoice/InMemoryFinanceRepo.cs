using System.Collections.Generic;
using UniversityLessonSelectionSystem.Domain.Enums;
using UniversityLessonSelectionSystem.Ports.ScholarshipEligibilityEngine;

namespace UniversityLessonSelectionSystem.Domain
{
    /// <summary>
    /// Burs/ödül bütçe kullanımlarını basit sayaçlarla tutan finance repository.
    /// ScholarshipEligibilityEngineService gibi servisler buradan okur.
    /// </summary>
    public sealed class InMemoryFinanceRepo : IFinanceRepo
    {
        private readonly IDictionary<string, int> _termTotals = new Dictionary<string, int>();
        private readonly IDictionary<(string termId, Department dept), int> _deptTotals
            = new Dictionary<(string, Department), int>();

        public int AwardsUsedTotal(string termId)
        {
            int v;
            if (_termTotals.TryGetValue(termId, out v)) return v;
            return 0;
        }

        public int AwardsUsedByDepartment(string termId, Department dept)
        {
            int v;
            if (_deptTotals.TryGetValue((termId, dept), out v)) return v;
            return 0;
        }

        public void IncrementAward(string termId, Department dept)
        {
            _termTotals[termId] = AwardsUsedTotal(termId) + 1;
            _deptTotals[(termId, dept)] = AwardsUsedByDepartment(termId, dept) + 1;
        }
    }
}
