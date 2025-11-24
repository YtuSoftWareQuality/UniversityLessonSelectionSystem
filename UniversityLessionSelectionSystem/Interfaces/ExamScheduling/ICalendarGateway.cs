using System;
using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessonSelectionSystem.Ports.ExamScheduling
{
    /// <summary>Calendar/gaps/utilities for schedule services.</summary>
    public interface ICalendarGateway
    {
        bool InstructorAvailable(string sectionId, DaySlot day, TimeSpan start, TimeSpan end);
        bool RoomAvailable(string sectionId, DaySlot day, TimeSpan start, TimeSpan end);
    }
}
