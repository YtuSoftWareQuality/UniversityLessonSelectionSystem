using System.Collections.Generic;
using UniversityLessonSelectionSystem.Domain.Plagiarism;

namespace UniversityLessonSelectionSystem.Ports.Plagiarism
{

    /// <summary>Runs all configured plagiarism engines and returns per-engine scores.</summary>
    public interface IPlagiarismGateway
    {
        IList<EngineScore> RunAll(PlagiarismContext ctx);
    }
}
