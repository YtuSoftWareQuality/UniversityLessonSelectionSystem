using System;
using System.Collections.Generic;
using System.Linq; 
using University.Lms.Ports;
using UniversityLessionSelectionSystem.Ports.ExamScheduling;
using UniversityLessonSelectionSystem.Domain.Enums;
using UniversityLessonSelectionSystem.Domain.ExamScheduling;
using UniversityLessonSelectionSystem.Ports.ExamScheduling;

namespace University.Lms.Services
{
    /// <summary>
    /// Section sınavlarını, aday salon ve zaman pencereleri arasına yerleştirirken;
    /// salon kapasitesi ve tür uyumu, eğitmen ve gözetmen uygunluğu, aynı bölümdeki öğrenciler için çakışma önleme,
    /// gün içi dilimlerin dengeli dağılımı ve blackout pencereleri, binalar arası yürüme süreleri,
    /// erişilebilirlik ve özel ihtiyaçlar ile bölüm bazlı prime day-part adalet rotasyonunu
    /// katmanlı kurallar hâlinde uygulayan sınav yerleştirme/optimizasyon servisidir.
    /// Yüksek cyclomatic complexity eğitim amacıyla, bağımsız ve saf kural katmanları üzerinden sağlanmıştır.
    /// </summary>
    public sealed class ExamSchedulingOptimizerService
    {
        #region Fields
        private readonly ICalendarGateway _calendar;
        private readonly ICampusMapGateway _map;
        private readonly IExamPolicyRepo _policies;
        private readonly ILogger _logger;
        #endregion

        #region Policy Constants

        private const int MIN_TRAVEL_MINUTES = 8;
        private const int BUFFER_BEFORE_EXAM_MINUTES = 5;
        private const int BUFFER_AFTER_EXAM_MINUTES = 5;

        private const int FAIRNESS_ROTATION_SPAN = 3; // rotate prime slots across departments every N exams
        private const int MAX_TRIES_PER_REQUEST = 12;

        private static readonly HashSet<RoomType> AllowedLargeExamRooms = new HashSet<RoomType>
        {
            RoomType.Auditorium, RoomType.Standard, RoomType.Online
        };

        #endregion

        #region Constructor
        /// <summary>
        /// Takvim, kampüs haritası, sınav politikası ve log bağımlılıklarını enjekte ederek
        /// sınav planlama/optimizasyon servisini oluşturur.
        /// </summary>
        public ExamSchedulingOptimizerService(
            ICalendarGateway calendar,
            ICampusMapGateway map,
            IExamPolicyRepo policies,
            ILogger logger)
        {
            _calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _policies = policies ?? throw new ArgumentNullException(nameof(policies));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Verilen sınav istekleri listesini, aday salonlar ve gün içi zaman pencereleri üzerinde
        /// kural katmanlarını uygulayarak yerleştirir; yerleştirilen sınavları ve yerleştirilemeyen section kimliklerini
        /// içeren bir sınav programı (ExamSchedule) üretir.
        /// Büyük grupları önce yerleştirmek için istekleri beklenen öğrenci sayısına göre sıralar.
        /// </summary>
        public ExamSchedule Plan(IList<ExamRequest> requests, IList<ExamRoom> rooms, IList<ExamWindow> windows)
        {
            if (requests == null) throw new ArgumentNullException(nameof(requests));
            if (rooms == null) throw new ArgumentNullException(nameof(rooms));
            if (windows == null) throw new ArgumentNullException(nameof(windows));

            var schedule = new ExamSchedule();

            // deterministic ordering: largest cohorts first
            var orderedReqs = requests
                .OrderByDescending(r => r.ExpectedHeadcount)
                .ThenBy(r => r.Department)
                .ToList();

            var fairnessLedger = new Dictionary<Department, int>();

            foreach (var req in orderedReqs)
            {
                var candidateWindows = RankWindows(req, windows, fairnessLedger);
                var candidateRooms = RankRooms(req, rooms);

                bool placed = false;
                int tries = 0;

                foreach (var w in candidateWindows)
                {
                    foreach (var room in candidateRooms)
                    {
                        if (++tries > MAX_TRIES_PER_REQUEST) break;

                        if (!RoomFits(req, room)) continue;
                        if (!RoomAvailable(room, w)) continue;
                        if (!InstructorAndProctorOk(req, w)) continue;
                        if (!AvoidsCrossCourseConflicts(req, w, schedule)) continue;
                        if (!TravelOkForBackToBack(req, room, w, schedule)) continue;

                        // place
                        schedule.Items.Add(new ExamPlacement
                        {
                            SectionId = req.SectionId,
                            CourseId = req.CourseId,
                            Department = req.Department,
                            Room = room.RoomId,
                            Building = room.Building,
                            Day = w.Day,
                            Start = w.Start,
                            End = w.Start.Add(req.Duration),
                            DayPart = w.DayPart
                        });

                        UpdateFairness(fairnessLedger, req.Department, w.DayPart);
                        placed = true;
                        break;
                    }
                    if (placed) break;
                }

                if (!placed)
                {
                    schedule.Unplaced.Add(req.SectionId);
                    _logger.Warn($"Exam not placed: Section={req.SectionId}");
                }
            }

            _logger.Info($"ExamScheduling: placed={schedule.Items.Count}, unplaced={schedule.Unplaced.Count}");
            return schedule;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Verilen sınav isteği için, tüm aday sınav pencerelerini (windows) politika uyumu ve fairness rotasyonu gibi
        /// kurallara göre filtreleyip skorlar; en yüksek skordan düşük skora doğru sıralı bir pencere listesi döner.
        /// </summary>
        private IList<ExamWindow> RankWindows(ExamRequest req, IList<ExamWindow> windows, IDictionary<Department, int> fairness)
        {
            // Higher score → earlier in list
            return windows
                .Where(w => WindowFitsPolicy(req, w))
                .OrderByDescending(w => ScoreWindow(req, w, fairness))
                .ToList();
        }
        /// <summary>
        /// Verilen sınav isteği için, aday salon listesini (rooms) yalnızca sınav için uygun türdeki salonlarla
        /// sınırlayıp kapasite, donanım ve erişilebilirlik gibi kriterlerle skorlar; yüksek skordan düşüğe sıralı salon listesi döner.
        /// </summary>
        private IList<ExamRoom> RankRooms(ExamRequest req, IList<ExamRoom> rooms)
        {
            return rooms
                .Where(r => AllowedLargeExamRooms.Contains(r.RoomType))
                .OrderByDescending(r => ScoreRoom(req, r))
                .ToList();
        }
        
        /// <summary>
        /// Verilen department anahtarının ledger içerisinde kaç kez prime day-part kullandığını döndürür;
        /// kayıt yoksa varsayılan değeri (defaultValue) döner.
        /// </summary>
        private static int GetValueOrDefault(IDictionary<Department, int> dictionary, Department key, int defaultValue = 0)
        {
            return dictionary.TryGetValue(key, out var value) ? value : defaultValue;
        }
        
        /// <summary>
        /// Belirli bir sınav isteği ve sınav penceresi için,
        /// tercih edilen gün içi dilimi, blackout pencereleri, erişilebilirlik ve bölüm fairness rotasyonunu
        /// dikkate alarak pencere skorunu hesaplar; skor ne kadar yüksekse pencere o kadar tercih edilir.
        /// </summary>
        private int ScoreWindow(ExamRequest r, ExamWindow w, IDictionary<Department, int> fairness)
        {
            int score = 0;

            // prefer requested day-part  
            if (w.DayPart == r.PreferredDayPart) score += 20;

            // avoid blackout  
            if (_policies.IsBlackout(w.Day, w.Start, w.Start.Add(r.Duration))) score -= 40;

            // fairness rotation  
            int used = GetValueOrDefault(fairness, r.Department);
            if (used % FAIRNESS_ROTATION_SPAN == 0 && IsPrimeDayPart(w.DayPart)) score -= 10; // give others a turn  
            else if (IsPrimeDayPart(w.DayPart)) score += 5;

            // accessibility preference  
            if (r.NeedsAccessibility && w.DayPart == DayPart.Morning) score += 8;

            return score;
        }
        /// <summary>
        /// Bir bölümün prime day-part kullanım sayısını,
        /// yerleştirilen sınav prime day-part (Morning/Afternoon) ise bir artırarak fairness ledger'ını günceller.
        /// </summary>
        private static void UpdateFairness(IDictionary<Department, int> ledger, Department dept, DayPart usedPart)
        {
            int used = GetValueOrDefault(ledger, dept, 0);
            ledger[dept] = used + (IsPrimeDayPart(usedPart) ? 1 : 0);
        }
        /// <summary>
        /// Belirli bir sınav isteği için bir salonun kapasite, erişilebilirlik ve bilgisayar gereksinimleri gibi
        /// kriterlere göre ne kadar uygun olduğunu skora dönüştürür; yüksek skor daha uygun salon anlamına gelir.
        /// </summary>
        private int ScoreRoom(ExamRequest r, ExamRoom room)
        {
            int score = 0;

            // capacity headroom
            if (room.Capacity >= r.ExpectedHeadcount) score += Math.Min(30, (room.Capacity - r.ExpectedHeadcount) / 5);

            // special equipment and accessibility
            if (r.NeedsAccessibility && room.IsAccessible) score += 15;
            if (r.RequiresComputers && room.HasComputers) score += 10;

            return score;
        }

        #region Rules
        /// <summary>
        /// Verilen sınav isteği için, sınav penceresinin (ExamWindow) süre bakımından yeterli olup olmadığını,
        /// politika bazlı blackout pencerelerine denk gelip gelmediğini ve ilgili departman için bu day-part'in izinli olup olmadığını kontrol eder.
        /// </summary>
        private bool WindowFitsPolicy(ExamRequest r, ExamWindow w)
        {
            // duration fit
            if (w.Start.Add(r.Duration) > w.End) return false;

            // policy blackout and department preferences
            if (_policies.IsBlackout(w.Day, w.Start, w.Start.Add(r.Duration))) return false;

            // department day-part limits
            if (!_policies.AllowedDayParts(r.Department).Contains(w.DayPart)) return false;

            return true;
        }
        /// <summary>
        /// Sınav isteğinin ihtiyaç duyduğu başlıca fiziksel kısıtları (kapasite, bilgisayar gereksinimi, erişilebilirlik)
        /// salon üzerinde kontrol eder; en az bir kriter sağlanmıyorsa false döner.
        /// </summary>
        private bool RoomFits(ExamRequest r, ExamRoom room)
        {
            if (room.Capacity < r.ExpectedHeadcount) return false;
            if (r.RequiresComputers && !room.HasComputers) return false;
            if (r.NeedsAccessibility && !room.IsAccessible) return false;
            return true;
        }
        /// <summary>
        /// İlgili salonun, verilen sınav penceresinde takvim üzerinde başka bir rezervasyon tarafından kullanılıp kullanılmadığını
        /// ICalendarGateway üzerinden kontrol eder.
        /// </summary>
        private bool RoomAvailable(ExamRoom room, ExamWindow w)
        {
            // room availability via calendar (roomId encoded in sectionId slot for demo)
            return _calendar.RoomAvailable(room.RoomId, w.Day, w.Start, w.End);
        }
        /// <summary>
        /// İlgili section'ın eğitmeni ve gerekli gözetmenlerin, verilen sınav penceresinde
        /// sınava girebilecek durumda (müsait) olup olmadığını takvim ve politika bilgilerine göre kontrol eder.
        /// </summary>
        private bool InstructorAndProctorOk(ExamRequest r, ExamWindow w)
        {
            if (!_calendar.InstructorAvailable(r.SectionId, w.Day, w.Start, w.End)) return false;
            if (!_policies.ProctorAvailable(r.Department, w.Day, w.Start, w.End)) return false;
            return true;
        }
        /// <summary>
        /// Aynı bölümdeki (aynı cohort kabul edilen) mevcut sınav yerleşimleriyle,
        /// yeni yerleştirilecek sınavın aynı gün içinde zaman olarak çakışıp çakışmadığını kontrol eder;
        /// çakışma varsa false döner.
        /// </summary>
        private bool AvoidsCrossCourseConflicts(ExamRequest r, ExamWindow w, ExamSchedule existing)
        {
            // naive cross-conflict: same department cohort marker → avoid same window
            foreach (var placed in existing.Items.Where(x => x.Department == r.Department && x.Day == w.Day))
            {
                if (Overlaps(w.Start, w.Start.Add(r.Duration), placed.Start, placed.End))
                    return false;
            }
            return true;
        }
        /// <summary>
        /// Aynı gün içinde, aynı eğitmenin arka arkaya sınavları varsa,
        /// daha önce yerleştirilmiş sınavın çıkış binası ile yeni sınavın salon binası arasındaki
        /// yolculuk süresini ve sınav öncesi/sonrası tampon sürelerini dikkate alarak
        /// aradaki sürenin yeterli olup olmadığını kontrol eder.
        /// </summary>
        private bool TravelOkForBackToBack(ExamRequest r, ExamRoom room, ExamWindow w, ExamSchedule existing)
        {
            // If same instructor has back-to-back on same day, check travel time
            foreach (var p in existing.Items.Where(x => x.Day == w.Day))
            {
                if (p.SectionId == r.PreviousInstructorSectionId) // marker for previous exam taught by same instructor
                {
                    var end = p.End.Add(TimeSpan.FromMinutes(BUFFER_AFTER_EXAM_MINUTES));
                    var start = w.Start.Add(TimeSpan.FromMinutes(-BUFFER_BEFORE_EXAM_MINUTES));

                    int travel = _map.TravelMinutesBetween(p.Building, room.Building);
                    if ((start - end).TotalMinutes < Math.Max(travel, MIN_TRAVEL_MINUTES))
                        return false;
                }
            }
            return true;
        }
        /// <summary>
        /// Verilen gün içi diliminin (DayPart) prime slot (Morning veya Afternoon) olup olmadığını döner;
        /// prime day-part'ler fairness rotasyonu için sayılır.
        /// </summary>
        private static bool IsPrimeDayPart(DayPart part) => part == DayPart.Morning || part == DayPart.Afternoon;
        /// <summary>
        /// İki zaman aralığının (s1–e1 ve s2–e2) birbirleriyle çakışıp çakışmadığını kontrol eder;
        /// aralıklardan biri diğerinin bitiş noktasından önce başlıyorsa çakışma vardır.
        /// </summary>
        private static bool Overlaps(TimeSpan s1, TimeSpan e1, TimeSpan s2, TimeSpan e2)
            => s1 < e2 && s2 < e1;


        #endregion

        #endregion
    }
}
