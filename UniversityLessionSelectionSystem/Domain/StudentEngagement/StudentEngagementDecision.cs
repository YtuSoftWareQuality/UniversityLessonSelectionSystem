using UniversityLessonSelectionSystem.Domain.Enums; 

namespace UniversityLessionSelectionSystem.Domain
{
    /// <summary>
    /// Engagement hesaplamasının sonucunu tutan nesnedir: skor, band ve öğrenci kimliği.
    /// </summary>
    public sealed class StudentEngagementDecision
    {
        public string StudentId { get; set; }
        public int Score { get; set; }
        public StudentEngagementBand Band { get; set; }
    }
}
