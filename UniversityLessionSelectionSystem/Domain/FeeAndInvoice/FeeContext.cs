using System.Collections.Generic;
using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessonSelectionSystem.Domain.FeeAndInvoice
{
    public sealed class FeeContext
    {
        public ProgramType Program { get; set; }
        public ResidencyStatus Residency { get; set; }
        public TermPhase TermPhase { get; set; }
        public bool IsLateRegistration { get; set; }
        public int CreditLoad { get; set; }
        public IList<Department> EnrolledDepartments { get; set; } = new List<Department>();
        public ScholarshipTier ScholarshipTier { get; set; } = ScholarshipTier.None;
        public WaiverPolicy WaiverPolicy { get; set; } = WaiverPolicy.None;
        public PaymentPlan Plan { get; set; } = PaymentPlan.None;
        public bool IncludeInternationalSupport { get; set; }
        public bool HasComplianceHold { get; set; }
    }
}
