namespace UniversityLessonSelectionSystem.Domain.Plagiarism
{
    /// <summary>Prior behavior used for small penalties/dampening.</summary>
    public sealed class PlagiarismHistory
    {
        public int PriorFlags { get; set; }        // number of prior confirmed flags
        public int PriorCleanAudits { get; set; }  // number of prior clean results
    }

}
