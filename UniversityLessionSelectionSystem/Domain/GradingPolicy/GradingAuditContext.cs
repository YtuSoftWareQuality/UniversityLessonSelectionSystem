using System;
using System.Collections.Generic;
 
using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessonSelectionSystem.Domain.GradingPolicy
{
    public sealed class GradingAuditContext
    {
        public Department Department { get; set; }
        public IList<LetterGrade> Grades { get; set; } = new List<LetterGrade>();
        public IDictionary<int, int> LatePenaltyByDay { get; set; } = new Dictionary<int, int>(); // day → points penalty
        public int TotalPoints { get; set; }
        public int ExtraCreditPoints { get; set; }
        public IList<IList<RetakeRecord>> RetakeBundles { get; set; } = new List<IList<RetakeRecord>>();
        public IList<SpecialMarkRecord> SpecialMarks { get; set; } = new List<SpecialMarkRecord>();
        public IList<GradeChangeRecord> GradeChanges { get; set; } = new List<GradeChangeRecord>();
        public DateTime WithdrawDeadlineUtc { get; set; }
        public DateTime Now { get; set; }
    }
}
