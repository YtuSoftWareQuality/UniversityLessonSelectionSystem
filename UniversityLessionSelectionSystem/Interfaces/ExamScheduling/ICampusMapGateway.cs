using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessionSelectionSystem.Ports.ExamScheduling
{
    /// <summary>Campus map & travel-time heuristics.</summary>
    public interface ICampusMapGateway
    {
        /// <summary>Returns required walking minutes between buildings.</summary>
        int TravelMinutesBetween(BuildingCode from, BuildingCode to);
    }
}
