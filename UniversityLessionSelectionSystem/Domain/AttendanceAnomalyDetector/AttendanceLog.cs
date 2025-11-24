
namespace UniversityLessonSelectionSystem.Domain.AttendanceAnomalyDetector
{
    public sealed class AttendanceLog
    {
        public int WeekIndex { get; set; }
        public string CourseId { get; set; }
        public bool Present { get; set; }
    }
}
