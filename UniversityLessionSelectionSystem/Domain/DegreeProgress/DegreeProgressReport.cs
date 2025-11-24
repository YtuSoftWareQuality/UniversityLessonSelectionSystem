using System.Collections.Generic;
using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessionSelectionSystem.Domain
{
    public sealed class DegreeProgressReport
    {
        public string StudentId { get; set; }
        public ProgramType Program { get; set; }

        public IList<string> CompletedMandatory { get; } = new List<string>();
        public IList<string> MissingMandatory { get; } = new List<string>();
        public IList<string> CompletedElectives { get; } = new List<string>();

        public decimal MandatoryCompletionRatio { get; set; }
        public decimal ElectiveCompletionRatio { get; set; }
        public decimal AdvancedCompletionRatio { get; set; }

        public int RemainingCredits { get; set; }
        public int EstimatedRemainingTerms { get; set; }

        public DegreeProgressRiskBand RiskBand { get; set; } = DegreeProgressRiskBand.Low;
        public IList<string> Recommendations { get; } = new List<string>();
    }

}
