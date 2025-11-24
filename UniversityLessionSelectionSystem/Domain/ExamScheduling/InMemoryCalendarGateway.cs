using System;
using System.Collections.Generic;
using UniversityLessonSelectionSystem.Domain.Enums;
using UniversityLessonSelectionSystem.Ports.ExamScheduling;

namespace University.Lms.Domain
{
    /// <summary>
    /// Sınav/ders zamanlamasında kullanılan oda/eğitmen müsaitlik kontrollerini basit kurallarla simüle eder.
    /// Varsayılan olarak her şey müsait, istenirse blok kayıtları eklenebilir.
    /// </summary>
    public sealed class InMemoryCalendarGateway : ICalendarGateway
    {
        private readonly HashSet<string> _blockedRoomSlots = new HashSet<string>();
        private readonly HashSet<string> _blockedInstructorSlots = new HashSet<string>(); 
        public bool RoomAvailable(string sectionId, DaySlot day, TimeSpan start, TimeSpan end)
        {
            var key = $"ROOM:{sectionId}:{day}:{start}-{end}";
            return !_blockedRoomSlots.Contains(key);
        }

        public bool InstructorAvailable(string sectionId, DaySlot day, TimeSpan start, TimeSpan end)
        {
            var key = $"INSTR:{sectionId}:{day}:{start}-{end}";
            return !_blockedInstructorSlots.Contains(key);
        } 
    }
}
