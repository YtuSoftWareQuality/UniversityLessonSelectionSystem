using System;
using System.Collections.Generic;
using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessonSelectionSystem.Ports.ExamScheduling
{

    /// <summary>Policy queries for exam scheduling.</summary>
    public interface IExamPolicyRepo
    {
        bool IsBlackout(DaySlot day, TimeSpan start, TimeSpan end);
        bool ProctorAvailable(Department department, DaySlot day, TimeSpan start, TimeSpan end);
        IList<DayPart> AllowedDayParts(Department department);
    }
}
