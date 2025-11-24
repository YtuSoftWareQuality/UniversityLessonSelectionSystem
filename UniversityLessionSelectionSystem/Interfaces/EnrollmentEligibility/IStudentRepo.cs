using University.Lms.Ports;
using UniversityLessonSelectionSystem.Domain.EnrollmentEligibility;
using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessonSelectionSystem.Ports.EnrollmentEligibility
{
    /// <summary>Student data access abstraction.</summary>
    public interface IStudentRepo
    {
        Student GetById(string studentId);
        bool HasCompleted(string studentId, string courseId, GradeBand minimum);
        int CurrentCreditLoad(string studentId, string termId);
        bool HasAdvisorApproval(string studentId, string sectionId);
    }
}
