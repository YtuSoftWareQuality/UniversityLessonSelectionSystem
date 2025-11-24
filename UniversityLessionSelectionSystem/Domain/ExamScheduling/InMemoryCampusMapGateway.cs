using System.Collections.Generic;
using UniversityLessionSelectionSystem.Ports.ExamScheduling;
using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessionSelectionSystem.Domain
{
    /// <summary>
    /// Kampüs binaları arasındaki yürüme sürelerini sabit bir tablo üzerinden hesaplayan basit gateway.
    /// </summary>
    public sealed class InMemoryCampusMapGateway : ICampusMapGateway
    {
        private readonly IDictionary<(BuildingCode from, BuildingCode to), int> _travelMinutes
            = new Dictionary<(BuildingCode, BuildingCode), int>();

        public InMemoryCampusMapGateway()
        {
            SeedDefault();
        }

        public int TravelMinutesBetween(BuildingCode from, BuildingCode to)
        {
            if (from == to) return 0;

            int value;
            if (_travelMinutes.TryGetValue((from, to), out value)) return value;
            if (_travelMinutes.TryGetValue((to, from), out value)) return value;

            // default fallback
            return 10;
        }

        public void SeedTravel(BuildingCode from, BuildingCode to, int minutes)
        {
            _travelMinutes[(from, to)] = minutes;
        }

        private void SeedDefault()
        {
            SeedTravel(BuildingCode.LAW, BuildingCode.ONLINE, 7);
            SeedTravel(BuildingCode.ENG, BuildingCode.SCI, 4);
            SeedTravel(BuildingCode.ART, BuildingCode.LIB, 6);
        }
    }
}
