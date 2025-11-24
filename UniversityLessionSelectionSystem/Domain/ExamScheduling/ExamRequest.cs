using System; 
using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessonSelectionSystem.Domain.ExamScheduling
{
    public sealed class ExamRequest
    {
        public string SectionId { get; set; }
        public string CourseId { get; set; }
        public Department Department { get; set; }
        public int ExpectedHeadcount { get; set; }
        public bool NeedsAccessibility { get; set; }
        public bool RequiresComputers { get; set; }
        public DayPart PreferredDayPart { get; set; }
        public TimeSpan Duration { get; set; }
        public string PreviousInstructorSectionId { get; set; } // for back-to-back travel checks
    }
}
