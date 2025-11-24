using System.Collections.Generic;
using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessonSelectionSystem.Domain.AccessPolicy
{
    public sealed class AccessDecision
    {
        public AccessOutcome Outcome { get; set; }
        public string Reason { get; set; }
        public int RetryAfterSeconds { get; set; }
        public IList<Obligation> Obligations { get; } = new List<Obligation>();
    }
}
