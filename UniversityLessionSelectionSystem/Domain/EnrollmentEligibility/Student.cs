using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessonSelectionSystem.Domain.EnrollmentEligibility
{
    /// <summary>
    /// Student entity with key lifecycle signals used by services.
    /// </summary>
    public sealed class Student
    {
        public string Id { get; set; }
        public ProgramType Program { get; set; }
        public StudentStanding Standing { get; set; }
        public decimal Gpa { get; set; }
        public int EarnedCredits { get; set; }
        public bool HasFinancialHold { get; set; }
        public bool HasDisciplinaryHold { get; set; }
        public bool IsAthlete { get; set; }
        public bool HasAccessibilityNeeds { get; set; }
        public ResidencyStatus ResidencyStatus { get; set; }
    }

}
