
using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessonSelectionSystem.Ports.AccessPolicy
{
    public interface IAccessPolicyRepo
    {
        bool RoleCan(Role role, Operation op);
        bool DepartmentScopeAllowed(Role role, Department dept);
        bool AfterHoursAllowed(Role role, Operation op);
    }
}
