using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessonSelectionSystem.Domain.ExamScheduling
{
    public sealed class ExamRoom
    {
        public string RoomId { get; set; }
        public BuildingCode Building { get; set; }
        public RoomType RoomType { get; set; }
        public int Capacity { get; set; }
        public bool HasComputers { get; set; }
        public bool IsAccessible { get; set; }
    }
}
