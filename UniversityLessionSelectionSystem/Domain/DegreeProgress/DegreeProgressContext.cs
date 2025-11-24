using System.Collections.Generic;

namespace UniversityLessionSelectionSystem.Domain
{
    public sealed class DegreeProgressContext
    {
        public string StudentId { get; set; }

        public IList<CompletedCourseInfo> CompletedCourses { get; set; } = new List<CompletedCourseInfo>();
        public int CompletedCredits { get; set; }
        public int RequiredElectiveCount { get; set; }
        public int RepeatedCourseCount { get; set; }
        public int TransferCredits { get; set; }

        public int TypicalTermCredits { get; set; }
        public int ExpectedRemainingTerms { get; set; }
    }
}
