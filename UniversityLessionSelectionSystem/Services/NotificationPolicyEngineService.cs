using System;
using System.Collections.Generic;
using System.Linq;

using University.Lms.Ports;
using UniversityLessonSelectionSystem.Domain.Enums;
using UniversityLessonSelectionSystem.Domain.NotificationPolicy;

namespace University.Lms.Services
{
    /// <summary>
    /// LMS olayları için hangi bildirim kanalının (SMS, e-posta, uygulama içi) kullanılacağını
    /// öğrenci durumu, dönem fazı, etkinlik türü, sessiz saatler, deduplikasyon pencereleri,
    /// kanal bazlı hız limitleri ve ikamet/yerel politika kısıtları üzerinden katmanlı olarak değerlendiren;
    /// her kanal için izin ver / kıs / reddet ve tekrar deneme süresi üreten bildirim politika motorudur.
    /// </summary>
    public sealed class NotificationPolicyEngineService
    {
        #region Fields
        private readonly IClock _clock;
        private readonly ILogger _logger;
        #endregion

        #region Policy Constants

        private const int QUIET_START_LOCAL_HOUR = 22;
        private const int QUIET_END_LOCAL_HOUR = 7;

        private const int DEDUP_MINUTES_GRADE = 30;
        private const int DEDUP_MINUTES_ENROLL = 15;
        private const int DEDUP_MINUTES_FINANCE = 60;
        private const int DEDUP_MINUTES_ADVISOR = 10;

        private const int SMS_WINDOW_SEC = 60;
        private const int EMAIL_WINDOW_SEC = 20;
        private const int APP_WINDOW_SEC = 5;

        private const int SMS_LIMIT_PER_WINDOW = 1;
        private const int EMAIL_LIMIT_PER_WINDOW = 3;
        private const int APP_LIMIT_PER_WINDOW = 5;

        #endregion

        #region Constructor
        public NotificationPolicyEngineService(IClock clock, ILogger logger)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Verilen bildirim bağlamına göre, etkinleştirilmiş her kanal için
        /// izin/kısıtlama/red ve tekrar deneme süresi içeren bir bildirim planı üretir;
        /// gerekirse tüm kanallar engellendiğinde uygulama içi kanalı eskalasyon fallback olarak devreye alır.
        /// </summary>
        public NotificationPlan Decide(NotificationContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            var plan = new NotificationPlan();

            foreach (var ch in ctx.EnabledChannels)
            {
                var decision = DecideForChannel(ctx, ch);
                plan.Decisions.Add(decision);
            }

            // Escalation: if all denied/throttled, try enabling at least app as fallback if policy allows
            if (!plan.Decisions.Any(d => d.Action == NotifyAction.Allow) &&
                ctx.AllowEscalationFallback &&
                ctx.EnabledChannels.Contains(NotificationChannel.App))
            {
                var app = plan.Decisions.FirstOrDefault(d => d.Channel == NotificationChannel.App);
                if (app == null)
                {
                    plan.Decisions.Add(new NotificationDecision
                    {
                        Channel = NotificationChannel.App,
                        Action = NotifyAction.Allow,
                        RetryAfterSeconds = 0,
                        Reason = "Escalation fallback"
                    });
                }
                else if (app.Action != NotifyAction.Allow)
                {
                    app.Action = NotifyAction.Allow;
                    app.RetryAfterSeconds = 0;
                    app.Reason = "Escalation fallback override";
                }
            }

            _logger.Info($"NotificationPolicyEngine produced {plan.Decisions.Count} decisions.");
            return plan;
        }
        #endregion

        #region Per-channel decision
        /// <summary>
        /// Tek bir bildirim kanalı için, öğrenci seviyesi, etkinlik türü, ikamet, sessiz saat
        /// ve hız/deduplikasyon pencereleri gibi tüm kural katmanlarını sırayla çalıştırarak
        /// bu kanalın izin verilip verilmeyeceğine ve gerekiyorsa hangi süreyle kısıtlanacağına karar verir.
        /// </summary>
        private NotificationDecision DecideForChannel(NotificationContext c, NotificationChannel ch)
        {
            // Start pessimistic → allow as we pass rules
            var decision = new NotificationDecision
            {
                Channel = ch,
                Action = NotifyAction.Deny,
                RetryAfterSeconds = 0,
                Reason = "Default deny"
            };

            if (!StudentTierAllows(c, ch))
                return Deny(decision, "Student tier disallows this channel.");

            if (!EventTypeCompliant(c, ch))
                return Deny(decision, "Event type not allowed on this channel.");

            if (!ResidencyAllows(c, ch))
                return Deny(decision, "Residency policy disallows this channel.");

            var localHour = c.LocalHour;
            if (IsQuietHour(localHour) && !IsQuietHourException(c, ch))
                return Throttle(decision, SecondsUntilQuietOver(localHour), "Quiet hours");

            // dedup horizon per event
            int dedupMin = DedupMinutes(c.EventType);
            if (WithinDedupWindow(c, ch, dedupMin))
                return Throttle(decision, SecondsUntilDedupOver(c, ch, dedupMin), "Dedup window");

            // rate window per channel
            int windowSec = WindowSeconds(ch);
            int limit = WindowLimit(ch, c.StudentStanding);
            int used = CountUsed(ch, c.History, c.UtcNowUnix, windowSec);

            if (used >= limit)
                return Throttle(decision, RetryAfter(ch, c.History, c.UtcNowUnix, windowSec), "Rate limited");

            // If we got here → allow
            decision.Action = NotifyAction.Allow;
            decision.Reason = "Allowed";
            decision.RetryAfterSeconds = 0;
            return decision;
        }

        #endregion

        #region Rule layers
        /// <summary>
        /// Öğrencinin akademik durumu (standing) ve seviyesi, ilgili kanalı kullanmaya uygun mu diye kontrol eder;
        /// örneğin uzaklaştırma (suspension) durumunda tüm kanalları engeller.
        /// </summary>
        private static bool StudentTierAllows(NotificationContext c, NotificationChannel ch)
        {
            // Example: probation students cannot receive marketing email; here we only gate by standing minimally
            if (c.StudentStanding == StudentStanding.Suspension)
                return false;

            // Honors/Athlete/Scholarship handled upstream via event routing, we keep minimal general gate here
            return true;
        }
        /// <summary>
        /// Bildirim türü ile kanalın gizlilik/uygunluk kurallarına uyup uymadığını kontrol eder;
        /// örneğin not bildirimlerinin SMS ile gitmesini engeller, finans bildirimlerinin sadece uygulama içi kalmasını önler.
        /// </summary>
        private static bool EventTypeCompliant(NotificationContext c, NotificationChannel ch)
        {
            // Example: grades cannot go via SMS for privacy; finance avoids app-only
            if (c.EventType == NotificationEventType.Grade && ch == NotificationChannel.SMS) return false;
            if (c.EventType == NotificationEventType.Finance && ch == NotificationChannel.App) return false;
            return true;
        }
        /// <summary>
        /// Öğrencinin ikamet durumuna göre (örneğin uluslararası öğrenci),
        /// ilgili bildirim kanalının (özellikle SMS) kullanılmasına izin verilip verilmediğini kontrol eder.
        /// </summary>
        private static bool ResidencyAllows(NotificationContext c, NotificationChannel ch)
        {
            // Example: International SMS disabled (cost/compliance), allow email & app
            if (c.Residency == ResidencyStatus.International && ch == NotificationChannel.SMS)
                return false;
            return true;
        }
        /// <summary>
        /// Verilen yerel saatin tanımlı sessiz saat aralığına (gece 22 - sabah 7) denk gelip gelmediğini kontrol eder.
        /// </summary>
        private static bool IsQuietHour(int hour) =>
            hour >= QUIET_START_LOCAL_HOUR || hour < QUIET_END_LOCAL_HOUR;
        /// <summary>
        /// Sessiz saat kuralı için özel istisna olup olmadığını kontrol eder;
        /// örneğin not açıklama bildirimlerinin sessiz saatlerde e-posta ile yine de gönderilmesine izin verir.
        /// </summary>
        private static bool IsQuietHourException(NotificationContext c, NotificationChannel ch)
        {
            // Grades released during quiet hours still allowed via email (compliance)
            if (c.EventType == NotificationEventType.Grade && ch == NotificationChannel.Email) return true;
            return false;
        }
        /// <summary>
        /// Bildirim türüne göre (not, kayıt, finans, danışman) tekrar eden bildirimler arasında
        /// uygulanacak deduplikasyon süresini (dakika cinsinden) döner.
        /// </summary>
        private static int DedupMinutes(NotificationEventType t)
        {
            switch (t)
            {
                case NotificationEventType.Grade:
                    return DEDUP_MINUTES_GRADE;
                case NotificationEventType.Enrollment:
                    return DEDUP_MINUTES_ENROLL;
                case NotificationEventType.Finance:
                    return DEDUP_MINUTES_FINANCE;
                default:
                    return DEDUP_MINUTES_ADVISOR;
            }
        }
        /// <summary>
        /// Kanal bazlı hız limit penceresinin (rate window) uzunluğunu saniye cinsinden döner;
        /// SMS için en uzun, uygulama için en kısa zaman penceresini kullanır.
        /// </summary>
        private static int WindowSeconds(NotificationChannel ch)
        {
            switch (ch)
            {
                case NotificationChannel.SMS:
                    return SMS_WINDOW_SEC;
                case NotificationChannel.Email:
                    return EMAIL_WINDOW_SEC;
                default:
                    return APP_WINDOW_SEC;
            }
        }
        /// <summary>
        /// Kanal türüne ve öğrencinin akademik durumuna göre,
        /// belirli bir zaman penceresi içinde gönderilebilecek maksimum bildirim sayısını hesaplar;
        /// probation durumunda limitleri biraz daha sıkılaştırır.
        /// </summary>
        private static int WindowLimit(NotificationChannel ch, StudentStanding standing)
        {
            int baseLimit = ch == NotificationChannel.SMS ? SMS_LIMIT_PER_WINDOW :
                            ch == NotificationChannel.Email ? EMAIL_LIMIT_PER_WINDOW :
                            APP_LIMIT_PER_WINDOW;

            // Slightly stricter for probation
            if (standing == StudentStanding.Probation && baseLimit > 1) return baseLimit - 1;
            return baseLimit;
        }
        /// <summary>
        /// Belirli bir kanal için, tanımlı zaman penceresi içinde (windowSec) geçmiş gönderim sayısını,
        /// gönderim geçmişine bakarak hesaplar; hız limiti ve kısıtlama kararlarında kullanılır.
        /// </summary>
        private static int CountUsed(NotificationChannel ch, IList<SentNotification> history, int nowUnix, int windowSec)
        {
            int start = nowUnix - windowSec;
            int count = 0;
            foreach (var e in history)
                if (e.Channel == ch && e.TimestampUnix >= start && e.TimestampUnix <= nowUnix) count++;
            return count;
        }
        /// <summary>
        /// Aynı kanal ve etkinlik türü için, tanımlı deduplikasyon süresi içinde
        /// zaten bir bildirim gönderilip gönderilmediğini kontrol eder; varsa dedup penceresinde kabul eder.
        /// </summary>
        private static bool WithinDedupWindow(NotificationContext c, NotificationChannel ch, int minutes)
        {
            int start = c.UtcNowUnix - minutes * 60;
            foreach (var e in c.History)
                if (e.Channel == ch && e.EventType == c.EventType && e.TimestampUnix >= start)
                    return true;
            return false;
        }
        /// <summary>
        /// Deduplikasyon penceresi içinde tespit edilen en eski ilgili gönderim zamanına göre,
        /// dedup süresinin bitmesine kalan saniyeyi hesaplar; kısıtlama (throttle) için retry-after değeri üretir.
        /// </summary>
        private static int SecondsUntilDedupOver(NotificationContext c, NotificationChannel ch, int minutes)
        {
            int start = c.UtcNowUnix - minutes * 60;
            int earliest = c.UtcNowUnix;
            foreach (var e in c.History)
                if (e.Channel == ch && e.EventType == c.EventType && e.TimestampUnix >= start)
                    earliest = Math.Min(earliest, e.TimestampUnix);
            int retry = (earliest + minutes * 60) - c.UtcNowUnix;
            return retry < 0 ? 0 : retry;
        }
        /// <summary>
        /// Belirli bir kanal için hız penceresi (windowSec) içinde atılan en eski gönderimi bularak,
        /// pencerenin sona ermesine kalan süreyi saniye cinsinden hesaplar; rate limit durumunda tekrar deneme süresini döner.
        /// </summary>
        private static int RetryAfter(NotificationChannel ch, IList<SentNotification> history, int nowUnix, int windowSec)
        {
            int start = nowUnix - windowSec;
            int earliest = nowUnix;
            foreach (var e in history)
                if (e.Channel == ch && e.TimestampUnix >= start && e.TimestampUnix <= nowUnix)
                    earliest = Math.Min(earliest, e.TimestampUnix);
            int retry = (earliest + windowSec) - nowUnix;
            return retry < 0 ? 0 : retry;
        }
        /// <summary>
        /// Eğer içinde bulunulan yerel saat sessiz saat aralığındaysa,
        /// sessiz saatlerin bitimine (QUIET_END_LOCAL_HOUR) kalan süreyi saniye cinsinden hesaplar;
        /// değilse 0 döner.
        /// </summary>
        private static int SecondsUntilQuietOver(int localHour)
        {
            if (!IsQuietHour(localHour)) return 0;
            // Compute remaining minutes until QUIET_END_LOCAL_HOUR
            // Assume whole hours granularity for the teaching example
            if (localHour >= QUIET_START_LOCAL_HOUR)
            {
                // until midnight + QUIET_END_LOCAL_HOUR
                int hours = (24 - localHour) + QUIET_END_LOCAL_HOUR;
                return hours * 3600;
            }
            else
            {
                int hours = QUIET_END_LOCAL_HOUR - localHour;
                return hours * 3600;
            }
        }
        /// <summary>
        /// Verilen kararı "Deny" (reddet) durumuna getirir, tekrar deneme süresini sıfırlar
        /// ve reddetme gerekçesini ayarlar; değiştirilen kararı döner.
        /// </summary>
        private static NotificationDecision Deny(NotificationDecision d, string reason)
        {
            d.Action = NotifyAction.Deny;
            d.RetryAfterSeconds = 0;
            d.Reason = reason;
            return d;
        }
        /// <summary>
        /// Verilen kararı "Throttle" (kısıtla) durumuna getirir, tekrar deneme süresini (saniye)
        /// ve bu kısıtlamanın gerekçesini ayarlar; değiştirilen kararı döner.
        /// </summary>
        private static NotificationDecision Throttle(NotificationDecision d, int retry, string reason)
        {
            d.Action = NotifyAction.Throttle;
            d.RetryAfterSeconds = retry;
            d.Reason = reason;
            return d;
        }

        #endregion
    } 
}
