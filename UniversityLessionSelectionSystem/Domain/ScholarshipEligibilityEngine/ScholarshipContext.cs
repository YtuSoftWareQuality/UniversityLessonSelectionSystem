using System.Collections.Generic;
using UniversityLessonSelectionSystem.Domain.Enums;


namespace UniversityLessonSelectionSystem.Domain.ScholarshipEligibilityEngine
{
    public sealed class ScholarshipContext
    {
        public string TermId { get; set; }
        public ProgramType Program { get; set; }
        public StudentStanding Standing { get; set; }
        public ResidencyStatus Residency { get; set; }
        public Department MajorDepartment { get; set; }
        public int CreditLoad { get; set; }
        public decimal Gpa { get; set; }
        public int NeedIndex { get; set; } // 0..100
        public int DisciplinaryFlags { get; set; }
        public int RepeatCredits { get; set; }
        public int RemedialCredits { get; set; }
        public ISet<Department> AllowedDepartments { get; set; } = new HashSet<Department>();
    }
}
