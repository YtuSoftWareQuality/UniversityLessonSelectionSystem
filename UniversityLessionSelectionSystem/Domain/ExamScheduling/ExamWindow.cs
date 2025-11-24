using System;
using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessonSelectionSystem.Domain.ExamScheduling
{
    public sealed class ExamWindow
    {
        public DaySlot Day { get; set; }
        public DayPart DayPart { get; set; }
        public TimeSpan Start { get; set; }
        public TimeSpan End { get; set; }
    }
}
