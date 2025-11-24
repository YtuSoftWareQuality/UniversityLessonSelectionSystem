using System;
using System.Collections.Generic;
using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessionSelectionSystem.Domain.AdvisorApproval
{

    public sealed class WorkflowSnapshot
    {
        public WorkflowState State { get; set; }
        public DateTime LastUpdateUtc { get; set; }
        public DateTime? ReapplyAfterUtc { get; set; }
        public IList<string> Events { get; } = new List<string>();
    }
}
