using System.Collections.Generic; 
using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessonSelectionSystem.Domain.ScholarshipEligibilityEngine
{
    public sealed class ScholarshipDecision
    {
        public ScholarshipStatus Status { get; set; }
        public ScholarshipTier Tier { get; set; }
        public IList<string> Reasons { get; } = new List<string>();
    }
}
