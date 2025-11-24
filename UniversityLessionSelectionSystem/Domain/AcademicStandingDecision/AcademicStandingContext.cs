using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessionSelectionSystem.Domain
{
    public sealed class AcademicStandingContext
    {
        public string StudentId { get; set; }
        public string TermId { get; set; }
        public StudentStanding PreviousStanding { get; set; }
        public TermType TermType { get; set; }

        public decimal TermGpa { get; set; }
        public decimal CumulativeGpa { get; set; }

        public int FailedCredits { get; set; }
        public int IncompleteCount { get; set; }
        public int MisconductIncidents { get; set; }
        public int ConsecutiveProbationTerms { get; set; }
    }
}
