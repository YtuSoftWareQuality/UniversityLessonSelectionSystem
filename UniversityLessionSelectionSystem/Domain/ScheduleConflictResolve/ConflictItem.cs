using System.Collections.Generic;
using UniversityLessonSelectionSystem.Domain.Enums;


namespace UniversityLessonSelectionSystem.Domain.ScheduleConflictResolve
{
    public sealed class ConflictItem
    {
        public string SectionA { get; set; }
        public string SectionB { get; set; }
        public IList<ConflictType> Types { get; set; }
    }
}
