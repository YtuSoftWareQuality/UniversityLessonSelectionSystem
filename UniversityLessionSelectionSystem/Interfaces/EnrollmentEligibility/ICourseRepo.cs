using System.Collections.Generic;
using UniversityLessonSelectionSystem.Domain.EnrollmentEligibility;

namespace UniversityLessionSelectionSystem.Ports.EnrollmentEligibility
{
    /// <summary>Course/Section data access abstraction.</summary>
    public interface ICourseRepo
    {
        Course GetCourse(string courseId);
        Section GetSection(string sectionId);
        IList<Section> GetStudentSections(string studentId, string termId);
    }
}
