using System.Collections.Generic;
using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessonSelectionSystem.Domain.AttendanceAnomalyDetector
{
    public sealed class AttendanceContext
    {
        public IList<AttendanceLog> Logs { get; set; } = new List<AttendanceLog>();
        public CourseDifficulty CourseDifficulty { get; set; }
        public InstructorStrictness InstructorStrictness { get; set; }
        public IList<int> Holidays { get; set; } = new List<int>(); // week indices
        public StudentProfile Profile { get; set; } = new StudentProfile();
    }
}
