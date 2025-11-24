using System;
using System.Collections.Generic;
using System.Linq;
using UniversityLessonSelectionSystem.Domain.Enums;
using UniversityLessonSelectionSystem.Ports.ExamScheduling;

namespace University.Lms.Domain
{
    /// <summary>
    /// Sınav politikaları (blackout günleri, izin verilen day-part’lar, gözetmen
    /// müsaitliği vb.) için basit in-memory implementasyon.
    /// </summary>
    public sealed class InMemoryExamPolicyRepo : IExamPolicyRepo
    {
        private readonly HashSet<(DaySlot day, TimeSpan start, TimeSpan end)> _blackouts
            = new HashSet<(DaySlot, TimeSpan, TimeSpan)>();

        private readonly IDictionary<Department, IList<DayPart>> _allowedParts
            = new Dictionary<Department, IList<DayPart>>();

        private readonly HashSet<(Department dept, DaySlot day)> _proctorBlocks
            = new HashSet<(Department, DaySlot)>();

        public InMemoryExamPolicyRepo()
        {
            _allowedParts[Department.CS] = new List<DayPart> { DayPart.Morning, DayPart.Afternoon };
            _allowedParts[Department.BUS] = new List<DayPart> { DayPart.Afternoon, DayPart.Evening };
        }

        public bool IsBlackout(DaySlot day, TimeSpan start, TimeSpan end)
        {
            return _blackouts.Contains((day, start, end));
        }

        public IList<DayPart> AllowedDayParts(Department dept)
        {
            IList<DayPart> parts;
            if (_allowedParts.TryGetValue(dept, out parts))
                return parts.ToList();

            // default tüm part’lar
            return new List<DayPart> { DayPart.Morning, DayPart.Afternoon, DayPart.Evening };
        }

        public bool ProctorAvailable(Department dept, DaySlot day, TimeSpan start, TimeSpan end)
        {
            return !_proctorBlocks.Contains((dept, day));
        }

        public void AddBlackout(DaySlot day, TimeSpan start, TimeSpan end)
        {
            _blackouts.Add((day, start, end));
        }

        public void BlockProctor(Department dept, DaySlot day)
        {
            _proctorBlocks.Add((dept, day));
        }

        public void SetAllowedDayParts(Department dept, IList<DayPart> parts)
        {
            _allowedParts[dept] = parts ?? new List<DayPart>();
        }
    }
}
