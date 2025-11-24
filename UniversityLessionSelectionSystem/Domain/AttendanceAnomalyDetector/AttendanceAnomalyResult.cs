using System.Collections.Generic;
using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessonSelectionSystem.Domain.AttendanceAnomalyDetector
{
    public sealed class AttendanceAnomalyResult
    {
        public int Score { get; set; }
        public IList<AttendanceFlag> Flags { get; set; } = new List<AttendanceFlag>();
        public AttendanceAlert Alert { get; set; }
    }
}
