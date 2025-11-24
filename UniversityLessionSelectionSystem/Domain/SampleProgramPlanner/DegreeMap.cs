using System.Collections.Generic;
using UniversityLessonSelectionSystem.Domain.Enums;


namespace UniversityLessonSelectionSystem.Domain.SampleProgramPlanner
{
    /// <summary>
    /// Degree map constraints supplied by department/advisor for program planning.
    /// </summary>
    public sealed class DegreeMap
    {
        public int TotalPlannedCredits { get; set; }
        public IList<string> MandatoryCourseIds { get; set; } = new List<string>();
        public IList<string> ElectiveCourseIds { get; set; } = new List<string>();
        public Dictionary<Department, int> MaxPerDepartment { get; set; } = new Dictionary<Department, int>();
    }
}
