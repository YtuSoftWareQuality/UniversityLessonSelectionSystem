using System;
using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessonSelectionSystem.Domain.EnrollmentEligibility
{
    /// <summary>
    /// Weekly meeting time info.
    /// </summary>
    public sealed class ScheduleSlot
    {
        public DaySlot Day { get; set; }
        public TimeSpan Start { get; set; }
        public TimeSpan End { get; set; }
    }
}
