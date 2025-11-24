using System.Collections.Generic;
using UniversityLessonSelectionSystem.Domain.EnrollmentEligibility;


namespace UniversityLessonSelectionSystem.Domain.SampleProgramPlanner
{
    public sealed class ProgramPlan
    {
        public int TargetCredits { get; set; }
        public int TotalCredits { get; set; }
        public IList<Section> Sections { get; } = new List<Section>();
    }
}
