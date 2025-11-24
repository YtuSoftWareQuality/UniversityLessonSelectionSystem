

using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessonSelectionSystem.Ports.ScholarshipEligibilityEngine
{
    public interface IFinanceRepo
    {
        int AwardsUsedTotal(string termId);
        int AwardsUsedByDepartment(string termId, Department dept);
    }
}
