using System.Collections.Generic;
using UniversityLessonSelectionSystem.Domain.Enums;
using UniversityLessonSelectionSystem.Ports.FeeAndInvoice; 

namespace University.Lms.Domain
{
    /// <summary>
    /// Ücret/lisans kataloğunu sabit tablolardan okuyan basit in-memory implementasyon.
    /// </summary>
    public sealed class InMemoryFeeCatalogRepo : IFeeCatalogRepo
    {
        private readonly IDictionary<(ProgramType program, CreditBand band), decimal> _ratePerCredit
            = new Dictionary<(ProgramType, CreditBand), decimal>();

        private readonly IDictionary<ProgramType, decimal> _programFees
            = new Dictionary<ProgramType, decimal>();

        private readonly IDictionary<Department, decimal> _labSurcharge
            = new Dictionary<Department, decimal>();

        private readonly IDictionary<Department, decimal> _studioSurcharge
            = new Dictionary<Department, decimal>();

        private readonly IDictionary<(ScholarshipTier tier, ProgramType program, CreditBand band), decimal> _scholarshipAwards
            = new Dictionary<(ScholarshipTier, ProgramType, CreditBand), decimal>();

        private readonly IDictionary<(WaiverPolicy policy, ProgramType program), decimal> _waivers
            = new Dictionary<(WaiverPolicy, ProgramType), decimal>();

        private decimal _internationalDifferential = 500m;

        public InMemoryFeeCatalogRepo()
        {
            _ratePerCredit[(ProgramType.Undergraduate, CreditBand.PartTime)] = 500m;
            _ratePerCredit[(ProgramType.Undergraduate, CreditBand.FullTime)] = 450m;
            _ratePerCredit[(ProgramType.Graduate, CreditBand.PartTime)] = 700m;
            _ratePerCredit[(ProgramType.Graduate, CreditBand.FullTime)] = 650m;

            _programFees[ProgramType.Undergraduate] = 250m;
            _programFees[ProgramType.Graduate] = 400m;

            _labSurcharge[Department.CS] = 75m;
            _labSurcharge[Department.EE] = 100m;
            _studioSurcharge[Department.ART] = 150m;

            _scholarshipAwards[(ScholarshipTier.A, ProgramType.Undergraduate, CreditBand.FullTime)] = 4000m;
            _scholarshipAwards[(ScholarshipTier.B, ProgramType.Undergraduate, CreditBand.FullTime)] = 2500m;

            _waivers[(WaiverPolicy.Veteran, ProgramType.Undergraduate)] = 3000m;
            _waivers[(WaiverPolicy.Staff, ProgramType.Undergraduate)] = 2000m;
        }

        public decimal RatePerCredit(ProgramType program, CreditBand band)
        {
            decimal val;
            if (_ratePerCredit.TryGetValue((program, band), out val)) return val;
            return 0m;
        }

        public decimal ProgramFee(ProgramType program)
        {
            decimal val;
            if (_programFees.TryGetValue(program, out val)) return val;
            return 0m;
        }

        public decimal LabSurcharge(Department dept)
        {
            decimal val;
            if (_labSurcharge.TryGetValue(dept, out val)) return val;
            return 0m;
        }

        public decimal StudioSurcharge(Department dept)
        {
            decimal val;
            if (_studioSurcharge.TryGetValue(dept, out val)) return val;
            return 0m;
        }

        public decimal InternationalDifferential()
        {
            return _internationalDifferential;
        }

        public decimal ScholarshipAward(ScholarshipTier tier, ProgramType program, CreditBand band)
        {
            decimal val;
            if (_scholarshipAwards.TryGetValue((tier, program, band), out val)) return val;
            return 0m;
        }

        public decimal WaiverAmount(WaiverPolicy policy, ProgramType program)
        {
            decimal val;
            if (_waivers.TryGetValue((policy, program), out val)) return val;
            return 0m;
        }
    }

   
}
