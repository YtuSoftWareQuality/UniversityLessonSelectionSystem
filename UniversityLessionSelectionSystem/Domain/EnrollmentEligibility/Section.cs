using System.Collections.Generic;
using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessonSelectionSystem.Domain.EnrollmentEligibility
{

    /// <summary>
    /// A scheduled section instance of a course for a term.
    /// </summary>
    public sealed class Section
    {
        public string Id { get; set; }
        public string CourseId { get; set; }
        public Department Department { get; set; }
        public int Capacity { get; set; }
        public int Enrolled { get; set; }
        public PriorityTier PriorityTier { get; set; }
        public bool RequiresAdvisorApproval { get; set; }
        public bool IsCrossListed { get; set; }
        public RoomType RoomType { get; set; }
        public IList<ScheduleSlot> Slots { get; set; } = new List<ScheduleSlot>();
        public BuildingCode Building { get; set; }
    }
}
