using System.Collections.Generic;
using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessionSelectionSystem.Domain
{
    public sealed class AcademicStandingDecision
    {
        public string StudentId { get; set; }
        public StudentStanding PreviousStanding { get; set; }
        public StudentStanding NewStanding { get; set; }
        public AcademicRiskBand RiskBand { get; set; }
        public IList<StandingAction> Actions { get; } = new List<StandingAction>();
    }
}
