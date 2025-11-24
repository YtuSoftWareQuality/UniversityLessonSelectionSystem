using System.Collections.Generic;

namespace UniversityLessionSelectionSystem.Domain.CapacityWaitlistAllocator
{
    /// <summary>
    /// Bekleme listesinden terfi işleminin sonucunu temsil eder; terfi ettirilen öğrencilerin
    /// kimliklerini ve kontrol kapılarından geçemediği için atlanan öğrencilerin kimliklerini listeler.
    /// </summary>
    public sealed class AllocationPlan
    {
        public IList<string> Promoted { get; } = new List<string>();
        public IList<string> Skipped { get; } = new List<string>();
    }
}
