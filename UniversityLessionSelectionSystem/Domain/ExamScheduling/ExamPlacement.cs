using System; 
using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessonSelectionSystem.Domain.ExamScheduling
{
    public sealed class ExamPlacement
    {
        public string SectionId { get; set; }
        public string CourseId { get; set; }
        public Department Department { get; set; }
        public string Room { get; set; }
        public BuildingCode Building { get; set; }
        public DaySlot Day { get; set; }
        public DayPart DayPart { get; set; }
        public TimeSpan Start { get; set; }
        public TimeSpan End { get; set; }
    }
}
