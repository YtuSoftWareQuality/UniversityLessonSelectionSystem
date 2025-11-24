using System;
using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessonSelectionSystem.Domain.EnrollmentEligibility
{
    /// <summary>
    /// Represents an academic term (e.g., Fall 2025) with phase information to drive time-based rules.
    /// </summary>
    public sealed class Term
    {
        public string Id { get; set; }
        public TermPhase Phase { get; set; }
        public DateTime UtcNowSnapshot { get; set; }
    }
}
