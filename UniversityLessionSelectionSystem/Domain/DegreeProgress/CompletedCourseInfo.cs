using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessionSelectionSystem.Domain
{
    public sealed class CompletedCourseInfo
    {
        public string CourseId { get; set; }
        public GradeBand Grade { get; set; }
    }
}
