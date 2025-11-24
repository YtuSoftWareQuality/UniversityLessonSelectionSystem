using System;
using System.Collections.Generic;
using System.Linq; 
using University.Lms.Ports;
using UniversityLessionSelectionSystem.Domain.CapacityWaitlistAllocator;
using UniversityLessonSelectionSystem.Domain.CapacityWaitlistAllocator;
using UniversityLessonSelectionSystem.Domain.EnrollmentEligibility;
using UniversityLessonSelectionSystem.Domain.Enums;
using UniversityLessonSelectionSystem.Ports.NotificationPolicy;

namespace University.Lms.Services
{
    /// <summary> 
    /// Dolu olan bir dersta bekleme listesindeki öğrencileri öncelik kuralları,
    /// dönem fazı kısıtlamaları ve doğrulama kapılarından geçirerek sıralar,
    /// uygun olanları boş kontenjana terfi ettirir ve kabul/atlanma planı döner. 
    /// </summary>
    public sealed class CapacityWaitlistAllocatorService
    {
        #region Fields
        private readonly IClock _clock;
        private readonly ILogger _logger;
        private readonly INotificationGateway _notify;
        #endregion


        #region Policy Constants

        private const int SOFT_BUFFER = 1;
        private const int MAX_PROMOTIONS_PER_RUN = 25;

        private const int PHASE_REGISTRATION_THROTTLE = 10;
        private const int PHASE_ADDDROP_THROTTLE = 20;

        #endregion

        #region Constructor
        public CapacityWaitlistAllocatorService(IClock clock, ILogger logger, INotificationGateway notify)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _notify = notify ?? throw new ArgumentNullException(nameof(notify));
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Computes promotion decisions for a section given current seat inventory and a waitlist.
        /// </summary>
        public AllocationPlan Allocate(Section section, Term term, IList<WaitlistEntry> waitlist)
        {
            if (section == null) throw new ArgumentNullException(nameof(section));
            if (term == null) throw new ArgumentNullException(nameof(term));
            if (waitlist == null) throw new ArgumentNullException(nameof(waitlist));

            var plan = new AllocationPlan();

            int remainingSeats = Math.Max(0, section.Capacity - section.Enrolled - SOFT_BUFFER);
            if (remainingSeats <= 0) return plan;

            int throttle = term.Phase == TermPhase.Registration ? PHASE_REGISTRATION_THROTTLE :
                term.Phase == TermPhase.AddDrop ? PHASE_ADDDROP_THROTTLE : 0;

            var ordered = Prioritize(waitlist, section.PriorityTier)
                .Take(Math.Min(remainingSeats, Math.Min(MAX_PROMOTIONS_PER_RUN, throttle > 0 ? throttle : int.MaxValue)))
                .ToList();

            foreach (var w in ordered)
            {
                if (!PassesGates(w)) { plan.Skipped.Add(w.StudentId); continue; }

                plan.Promoted.Add(w.StudentId);
                _notify.NotifyWaitlistPromotion(w.StudentId, section.Id);
            }

            _logger.Info($"WaitlistAllocator promoted {plan.Promoted.Count} student(s), skipped {plan.Skipped.Count}.");
            return plan;
        }
        #endregion

        #region Private Methods 
        /// <summary>
        /// Bekleme listesindeki tüm öğrencileri, bölümün öncelik katmanı ve
        /// her öğrencinin öncelik özelliklerine göre puanlayarak yüksekten düşüğe sıralar;
        /// puanı eşit olanları da bekleme listesine giriş zamanına göre (ilk gelen önce) sıralar.
        /// </summary>
        private IEnumerable<WaitlistEntry> Prioritize(IList<WaitlistEntry> list, PriorityTier sectionTier)
        {
            // Priority rules (enum-only)
            return list.OrderByDescending(w => Score(w, sectionTier))
                .ThenBy(w => w.PositionTimestampUnix);
        }
        /// <summary>
        /// Tek bir bekleme listesi kaydı için, bölümün öncelik katmanı ile öğrencinin sporcu/burslu/onur durumu,
        /// mezuniyete kalan kredisi, danışman onayı ve finansal/kimlik doğrulama durumlarını birlikte değerlendirerek
        /// sayısal bir öncelik puanı hesaplar; yüksek puan terfi için daha yüksek öncelik anlamına gelir.
        /// </summary>
        private static int Score(WaitlistEntry w, PriorityTier sectionTier)
        {
            int s = 0;
            if (w.IsAthlete && sectionTier == PriorityTier.Athlete) s += 50;
            if (w.IsScholarship && sectionTier == PriorityTier.Scholarship) s += 40;
            if (w.IsHonors && sectionTier == PriorityTier.Honors) s += 30;

            s += (int)Math.Min(20, w.CreditsToGraduate / 5);
            if (w.HasAdvisorApproval) s += 10;

            // penalty if unpaid or missing materials
            if (!w.HasFinancialClearance) s -= 20;
            if (!w.HasIdentityVerified) s -= 10;

            return s;
        }
        /// <summary>
        /// Bir öğrencinin bekleme listesinden derse terfi edebilmesi için,
        /// finansal onayının, kimlik doğrulamasının tamamlanmış olup olmadığını
        /// ve varsa son geçerlilik süresinin (expiry) henüz dolmamış olduğunu kontrol eder;
        /// herhangi bir kapıdan kalırsa false döner.
        /// </summary>
        private bool PassesGates(WaitlistEntry w)
        {
            // simple multi-gate for complexity w/o external calls
            if (!w.HasFinancialClearance) return false;
            if (!w.HasIdentityVerified) return false;
            if (w.ExpiryUnix > 0 && _clock.UtcNow >= UnixToUtc(w.ExpiryUnix)) return false;
            return true;
        }
        /// <summary>
        /// Unix zaman damgasını (saniye cinsinden 1970-01-01 UTC başlangıcına göre verilen tam sayı değeri)
        /// UTC türünde bir <see cref="DateTime"/> nesnesine dönüştürür.
        /// </summary>
        private static DateTime UnixToUtc(int unix) =>
            new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(unix);

        #endregion

    } 
}
