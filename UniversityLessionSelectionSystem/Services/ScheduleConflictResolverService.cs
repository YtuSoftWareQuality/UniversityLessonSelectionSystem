using System;
using System.Collections.Generic;
using System.Linq; 
using University.Lms.Ports;
using UniversityLessionSelectionSystem.Ports.ExamScheduling;
using UniversityLessonSelectionSystem.Domain.EnrollmentEligibility;
using UniversityLessonSelectionSystem.Domain.Enums;
using UniversityLessonSelectionSystem.Domain.ScheduleConflictResolve;
using UniversityLessonSelectionSystem.Ports.ExamScheduling;

namespace University.Lms.Services
{
    /// <summary>
    /// Bir öğrencinin (veya bir grubun) dönemlik aday section listesi arasında ders çakışmalarını tespit eden servistir;
    /// her section çifti için:
    /// - Gün bazında doğrudan/zaman örtüşmesi (overlap ve kısmi overlap),
    /// - Eğitmen uygunluğu (takvimdeki müsaitlik),
    /// - Sınıf uygunluğu (oda kullanımı & tip uyuşmazlığı),
    /// - Binalar arası yürüme süresi ve tampon gereksinimi,
    /// - Ardışık slotlar için minimum ara (adjacent buffer) kuralı
    /// gibi karar katmanlarını çalıştırarak çatışma tiplerini belirler ve detaylı bir conflict raporu üretir.
    /// Eğitim amaçlı olarak, her kural katmanı bağımsız private metotlara bölünmüş ve test edilebilir tutulmuştur.
    /// </summary>
    public sealed class ScheduleConflictResolverService
    {
        #region Fields
        private readonly ICalendarGateway _calendar;
        private readonly ICampusMapGateway _map;
        private readonly ILogger _logger;
        #endregion

        #region Policy Constants

        private const int MIN_TRAVEL_MINUTES = 8;
        private const int ADJACENT_BUFFER_MINUTES = 5;

        #endregion

        #region Constructor
        /// <summary>
        /// Takvim, kampüs haritası ve log bağımlılıklarını alarak
        /// program çakışma tespiti yapacak ScheduleConflictResolverService örneğini oluşturur.
        /// </summary>
        public ScheduleConflictResolverService(ICalendarGateway calendar, ICampusMapGateway map, ILogger logger)
        {
            _calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Verilen section listesindeki tüm section çiftlerini tarar;
        /// her çift için çakışma değerlendirmesi yaparak ortaya çıkan conflict tiplerini toplar
        /// ve en az bir conflict bulunan çiftleri ConflictReport içinde raporlar.
        /// </summary>
        public ConflictReport Resolve(IList<Section> sections)
        {
            if (sections == null) throw new ArgumentNullException(nameof(sections));

            var report = new ConflictReport();

            for (int i = 0; i < sections.Count; i++)
                for (int j = i + 1; j < sections.Count; j++)
                {
                    var a = sections[i];
                    var b = sections[j];

                    var types = EvaluatePair(a, b);
                    if (types.Count > 0)
                    {
                        report.Items.Add(new ConflictItem
                        {
                            SectionA = a.Id,
                            SectionB = b.Id,
                            Types = types
                        });
                    }
                }

            _logger.Info($"ScheduleConflictResolver found {report.Items.Count} conflict pair(s).");
            return report;
        }

        #endregion

        #region Private Methods Pair Evaluation
        /// <summary>
        /// Tek bir section çifti (a, b) için, aynı güne düşen tüm slot kombinasyonlarını dolaşır;
        /// zaman örtüşmesi, eğitmen uygunluğu, oda uygunluğu, binalar arası seyahat süresi
        /// ve ardışık slot buffer kurallarını kontrol ederek tespit ettiği tüm conflict tiplerini toplar
        /// ve tekrar eden tipleri tekilleştirerek döner.
        /// </summary>
        private IList<ConflictType> EvaluatePair(Section a, Section b)
        {
            var conflicts = new List<ConflictType>();

            foreach (var da in a.Slots)
                foreach (var db in b.Slots.Where(s => s.Day == da.Day))
                {
                    if (Overlaps(da, db))
                        conflicts.Add(ConflictType.DirectOverlap);
                    else if (PartiallyOverlaps(da, db))
                        conflicts.Add(ConflictType.PartialOverlap);

                    if (!InstructorOk(a, da) || !InstructorOk(b, db))
                        conflicts.Add(ConflictType.InstructorUnavailable);

                    if (!RoomOk(a, da) || !RoomOk(b, db))
                        conflicts.Add(ConflictType.RoomTypeMismatch);

                    if (!TravelOk(a.Building, b.Building, da, db))
                        conflicts.Add(ConflictType.BuildingTravel);

                    if (!AdjacentBufferOk(da, db))
                        conflicts.Add(ConflictType.BufferBreach);
                }

            // de-dup per pair
            return conflicts.Distinct().ToList();
        }
        /// <summary>
        /// İki zaman aralığının (slot x ve slot y) başlangıç-bitiş değerlerine göre
        /// birbirleriyle doğrudan zaman çakışması (tam/klasik overlap) olup olmadığını kontrol eder.
        /// </summary>
        private static bool Overlaps(ScheduleSlot x, ScheduleSlot y) =>
            x.Start < y.End && y.Start < x.End;
        /// <summary>
        /// İki zaman aralığı arasında sıfırdan büyük fakat her iki slotun da tam süresinden kısa
        /// bir kesişim (partial overlap) olup olmadığını hesaplar; kısmi örtüşme varsa true döner.
        /// Tam örtüşme veya hiç örtüşmeme durumlarında false döner.
        /// </summary>
        private static bool PartiallyOverlaps(ScheduleSlot x, ScheduleSlot y)
        {
            var overlap = (x.End <= y.Start || y.End <= x.Start) ? TimeSpan.Zero
                : (x.End < y.End ? x.End - y.Start : y.End - x.Start);
            return overlap > TimeSpan.Zero && overlap < (x.End - x.Start) && overlap < (y.End - y.Start);
        }
        /// <summary>
        /// İlgili section ve slot için, eğitmenin bu gün ve saat aralığında
        /// takvimde (ICalendarGateway) müsait olup olmadığını kontrol eder; müsait değilse false döner.
        /// </summary>
        private bool InstructorOk(Section s, ScheduleSlot slot) =>
            _calendar.InstructorAvailable(s.Id, slot.Day, slot.Start, slot.End);
        /// <summary>
        /// İlgili section ve slot için, kullanılan odanın bu zaman aralığında
        /// takvimde başka bir rezervasyonla çakışıp çakışmadığını kontrol eder;
        /// oda uygun değilse (meşgulse veya erişilemezse) false döner.
        /// </summary>
        private bool RoomOk(Section s, ScheduleSlot slot) =>
            _calendar.RoomAvailable(s.Id, slot.Day, slot.Start, slot.End);
        /// <summary>
        /// İki farklı binada gerçekleşen slotlar arasında,
        /// bir slotun bitişi ile diğerinin başlangıcı arasındaki sürenin;
        /// kampüs haritasına göre hesaplanan minimum yürüme süresini (ve belirlenen alt limiti)
        /// karşılayıp karşılamadığını kontrol eder. Yetersiz süre varsa false döner.
        /// Zaman aralıkları zaten çakışıyorsa, bu kontrolü pas geçer (overlap başka yerde işlenir).
        /// </summary>
        private bool TravelOk(BuildingCode from, BuildingCode to, ScheduleSlot a, ScheduleSlot b)
        {
            if (from == to) return true;

            int travel = _map.TravelMinutesBetween(from, to);

            // whichever ends first → travel to the other's start
            if (a.End <= b.Start)
                return (b.Start - a.End).TotalMinutes >= Math.Max(travel, MIN_TRAVEL_MINUTES);

            if (b.End <= a.Start)
                return (a.Start - b.End).TotalMinutes >= Math.Max(travel, MIN_TRAVEL_MINUTES);

            return true; // overlapping handled elsewhere
        }
        /// <summary>
        /// İki slotun tam olarak peş peşe (adjacent) olması durumunda,
        /// aradaki boşluğun (gap) tanımlı minimum buffer süresinden (ADJACENT_BUFFER_MINUTES) uzun olup olmadığını kontrol eder;
        /// yeterli ara yoksa false döner, aksi halde veya adjacent değilse true döner.
        /// </summary>
        private static bool AdjacentBufferOk(ScheduleSlot a, ScheduleSlot b)
        {
            if (a.End == b.Start || b.End == a.Start)
            {
                var gap = a.End == b.Start ? (b.Start - a.End) : (a.Start - b.End);
                return gap.TotalMinutes >= ADJACENT_BUFFER_MINUTES;
            }
            return true;
        }

        #endregion
    }
}
