using System; 
using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessonSelectionSystem.Domain.Plagiarism
{
    /// <summary>Full context the orchestration needs to evaluate a submission.</summary>
    public sealed class PlagiarismContext
    {
        // Submission meta
        public string SubmissionId { get; set; }
        public int TokenCount { get; set; }

        // Assignment & policy dimensions
        public AssignmentType AssignmentType { get; set; }
        public SubmissionLanguage Language { get; set; } = SubmissionLanguage.Natural;
        public Department Department { get; set; }

        // Student profile
        public StudentStanding StudentStanding { get; set; } = StudentStanding.Good;

        // Allowance inputs
        public bool IsGroupAssignment { get; set; }
        public decimal SelfOverlapRatio { get; set; }      // 0..1
        public decimal GroupOverlapRatio { get; set; }     // 0..1
        public decimal CitationToContentRatio { get; set; }// 0..1
        public bool UsesApprovedTemplate { get; set; }

        // Rubric signal (for bonus/dampening)
        public RubricAlignment RubricAlignmentField { get; set; } = RubricAlignment.Neutral;

        // History and timing
        public PlagiarismHistory HistoryField { get; set; } = new PlagiarismHistory();
        public DateTime? SubmittedUtc { get; set; }
        public DateTime? ResubmittedUtc { get; set; }
    }
}
