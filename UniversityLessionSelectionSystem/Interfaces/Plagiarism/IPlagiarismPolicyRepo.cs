using UniversityLessonSelectionSystem.Domain;
using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessonSelectionSystem.Ports
{
    public interface IPlagiarismPolicyRepo
    {
        EngineAllowance EngineAllowance(Department dept, AssignmentType type);
    }
}
