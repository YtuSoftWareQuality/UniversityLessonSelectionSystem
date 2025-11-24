using System.Collections.Generic;
using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessonSelectionSystem.Domain.EnrollmentEligibility
{
    /// <summary>
    /// Course with structural metadata.
    /// </summary>
    public sealed class Course
    {
        public string Id { get; set; }
        public Department Department { get; set; }
        public CourseLevel Level { get; set; }
        public int Credits { get; set; }
        public bool RequiresInstructorConsent { get; set; }
        public bool HasLab { get; set; }
        public bool HasRecitation { get; set; }
        public IList<string> PrerequisiteCourseIds { get; set; } = new List<string>();
        public IList<string> CorequisiteCourseIds { get; set; } = new List<string>();
    }

}
