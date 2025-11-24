using System.Collections.Generic; 
using UniversityLessonSelectionSystem.Domain.PrerequisiteEvaluation;

namespace UniversityLessonSelectionSystem.Ports.PrerequisiteEvaluation
{
    /// <summary>Equivalency and transfer-substitution lookup.</summary>
    public interface IEquivalencyRepo
    {
        IList<EquivalentCourse> GetEquivalents(string courseId);
        IList<EquivalentCourse> GetTransferSubstitutions(string courseId);
    }
}
