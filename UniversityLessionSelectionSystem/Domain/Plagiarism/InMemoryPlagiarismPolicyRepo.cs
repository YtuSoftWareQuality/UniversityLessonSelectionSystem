using UniversityLessonSelectionSystem.Domain.Enums;
using UniversityLessonSelectionSystem.Ports;

namespace UniversityLessonSelectionSystem.Domain.Plagiarism
{
    /// <summary>
    /// Plagiarism level eşiklerini tutan basit repo.
    /// Servis bu repo üzerinden "Low/Medium/High" kararını verebilir.
    /// </summary>
    public sealed class InMemoryPlagiarismPolicyRepo : IPlagiarismPolicyRepo
    {
        private decimal _warningThreshold = 0.25m;
        private decimal _hardThreshold = 0.40m;

        public EngineAllowance EngineAllowance(Department dept, AssignmentType type)
        {
            if (dept == Department.EE) return new EngineAllowance(){ AllowAnyEngine = true};
            if (type == AssignmentType.Homework) return new EngineAllowance() { AllowAnyEngine = true };
            return new EngineAllowance() { AllowAnyEngine = false };
        }  
    }
}
