using System.Collections.Generic; 

namespace UniversityLessonSelectionSystem.Domain.ScheduleConflictResolve
{
    /// <summary>Detailed conflict report with pairwise entries.</summary>
    public sealed class ConflictReport
    {
        public IList<ConflictItem> Items { get; } = new List<ConflictItem>();
    }
}
