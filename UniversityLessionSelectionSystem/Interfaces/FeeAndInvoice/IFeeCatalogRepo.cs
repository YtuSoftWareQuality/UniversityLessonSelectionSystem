using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessonSelectionSystem.Ports.FeeAndInvoice
{
    public interface IFeeCatalogRepo
    {
        decimal RatePerCredit(ProgramType program, CreditBand band);
        decimal ProgramFee(ProgramType program);
        decimal LabSurcharge(Department dept);
        decimal StudioSurcharge(Department dept);
        decimal InternationalDifferential();
        decimal ScholarshipAward(ScholarshipTier tier, ProgramType program, CreditBand band);
        decimal WaiverAmount(WaiverPolicy policy, ProgramType program);
    }
}
