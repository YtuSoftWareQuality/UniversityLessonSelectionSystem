using System.Collections.Generic; 

namespace UniversityLessonSelectionSystem.Domain.ExamScheduling
{
    public sealed class ExamSchedule
    {
        public IList<ExamPlacement> Items { get; } = new List<ExamPlacement>();
        public IList<string> Unplaced { get; } = new List<string>();
    }
}
