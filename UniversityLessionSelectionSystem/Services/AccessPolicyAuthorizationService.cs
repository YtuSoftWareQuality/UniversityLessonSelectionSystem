using System;
using System.Collections.Generic;

using University.Lms.Ports;
using UniversityLessonSelectionSystem.Domain.AccessPolicy;
using UniversityLessonSelectionSystem.Domain.Enums;
using UniversityLessonSelectionSystem.Ports.AccessPolicy;

namespace University.Lms.Services
{
    /// <summary>
    /// Decides whether a principal is authorized for a protected operation under:
    ///  - Role permissions (student/advisor/instructor/registrar/admin)
    ///  - Context matches (ownership, course membership, department scope)
    ///  - Term phase gates (e.g., transcript locked during Finals/Closed)
    ///  - After-hours limits & emergency overrides
    ///  - FERPA-like constraints (grade privacy, PII access)
    ///  - Break-glass flows with audit requirement
    /// Rol, bağlam ve gizlilik kurallarına göre erişim kararları verir (allow/deny/conditional/throttle).
    /// </summary>
    public sealed class AccessPolicyAuthorizationService
    {
        #region Fields

        private readonly ILogger _logger;
        private readonly IAccessPolicyRepo _repo;
        #endregion

        #region Constants

        private const int AFTER_HOURS_START = 22; // local hour
        private const int AFTER_HOURS_END = 6;

        private static readonly HashSet<Role> RolesAllowingBreakGlass =
            new HashSet<Role> { Role.Registrar, Role.Admin };

        private static readonly HashSet<Operation> FinalsLockedOps =
            new HashSet<Operation> { Operation.EditTranscript, Operation.PublishGrades };

        #endregion

        #region Constructor
        public AccessPolicyAuthorizationService(ILogger logger, IAccessPolicyRepo repo)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        }

        #endregion

        #region Public Methods
        /// <summary>
        /// Evaluates an access request and returns a rich decision with obligations.
        /// </summary>
        public AccessDecision Authorize(AccessRequest req)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));

            var dec = new AccessDecision();

            if (!RoleAllows(req)) return Deny(dec, "Role not permitted.");
            if (IsFinalsLocked(req)) return Deny(dec, "Operation locked during Finals/Closed.");

            if (!ContextMatches(req))
            {
                if (CanBreakGlass(req))
                {
                    return Conditional(dec, "Break-glass required.", new[] { Obligation.AuditLog, Obligation.ManagerApproval });
                }
                return Deny(dec, "Context mismatch.");
            }

            if (!FerpaPrivacyOk(req))
            {
                if (CanBreakGlass(req))
                    return Conditional(dec, "FERPA guard; break-glass allowed.", new[] { Obligation.AuditLog, Obligation.ManagerApproval });
                return Deny(dec, "FERPA privacy constraint.");
            }

            if (IsAfterHours(req) && !_repo.AfterHoursAllowed(req.Role, req.Operation))
            {
                if (CanBreakGlass(req))
                    return Conditional(dec, "After-hours; break-glass allowed.", new[] { Obligation.AuditLog });
                return Throttle(dec, "After-hours restriction.", retrySeconds: SecondsUntilOpen(req));
            }

            dec.Outcome = AccessOutcome.Allow;
            _logger.Info("Access allowed.");
            return dec;
        }



        #endregion

        #region Private Methods
        /// <summary>
        /// Verilen rol ve işlem için erişim politikasının Rol bazında izin verip vermediğini
        /// IAccessPolicyRepo üzerinden kontrol eder.
        /// </summary>
        private bool RoleAllows(AccessRequest r) => _repo.RoleCan(r.Role, r.Operation);

        /// <summary>
        /// Dönem fazı Finals veya Closed olduğunda,
        /// bazı hassas işlemlerin (ör. transkript düzenleme, not yayınlama) kilitli olup olmadığını kontrol eder.
        /// </summary>
        private static bool IsFinalsLocked(AccessRequest r)
        {
            if (r.TermPhase == TermPhase.Finals || r.TermPhase == TermPhase.Closed)
                return FinalsLockedOps.Contains(r.Operation);
            return false;
        }
        /// <summary>
        /// Erişim bağlamını (öğrenci kendi kaydı, danışman, ders listesi, bölüm/genel) ve rolü birlikte değerlendirerek
        /// bağlam açısından erişimin uygun olup olmadığını belirler.
        /// </summary>
        private bool ContextMatches(AccessRequest r)
        {
            // Ownership or department scope or course roster membership
            if (r.Context == AccessContext.StudentOwnRecord && r.Role == Role.Student) return true;
            if (r.Context == AccessContext.AdviseeRecord && r.Role == Role.Advisor) return true;
            if (r.Context == AccessContext.CourseRoster && r.Role == Role.Instructor) return true;
            if (r.Context == AccessContext.DepartmentWide && _repo.DepartmentScopeAllowed(r.Role, r.Department)) return true;
            if (r.Context == AccessContext.UniversityWide && r.Role == Role.Admin) return true;
            return false;
        }
        /// <summary>
        /// FERPA benzeri gizlilik kurallarına göre,
        /// not ve PII gibi hassas verilerin sadece uygun roller tarafından erişilebilir olmasını sağlar.
        /// </summary>
        private static bool FerpaPrivacyOk(AccessRequest r)
        {
            // PII & grades guarded: only the appropriate contexts/roles
            if (r.Operation == Operation.ViewGrades)
                return r.Role == Role.Student || r.Role == Role.Instructor || r.Role == Role.Registrar || r.Role == Role.Admin;

            if (r.Operation == Operation.ViewPII)
                return r.Role == Role.Registrar || r.Role == Role.Admin;

            return true;
        }
        /// <summary>
        /// Talebin yerel saate göre mesai saatleri dışında olup olmadığını kontrol eder
        /// (örneğin 22:00–06:00 aralığı after-hours kabul edilir).
        /// </summary>
        private bool IsAfterHours(AccessRequest r)
        {
            var hour = r.LocalHour;
            return hour >= AFTER_HOURS_START || hour < AFTER_HOURS_END;
        }
        /// <summary>
        /// Talebin break-glass (acil durum) işaretli olup olmadığını ve rolün bu modu kullanmaya yetkili olup olmadığını kontrol eder.
        /// </summary>
        private static bool CanBreakGlass(AccessRequest r) =>
            r.BreakGlassRequested && RolesAllowingBreakGlass.Contains(r.Role);
        /// <summary>
        /// Mesai saatleri dışında erişim için, sistemin yeniden açılmasına kaç saniye kaldığını hesaplar
        /// ve throttle yanıtlarında kullanılacak bekleme süresini üretir.
        /// </summary>
        private int SecondsUntilOpen(AccessRequest r)
        {
            var hour = r.LocalHour;
            if (!IsAfterHours(r)) return 0;
            int h = hour >= AFTER_HOURS_START ? (24 - hour) + AFTER_HOURS_END : (AFTER_HOURS_END - hour);
            return h * 3600;
        }
        /// <summary>
        /// Erişim kararını reddedilmiş (Deny) olarak işaretler, reddetme gerekçesini ayarlar
        /// ve değiştirilen kararı döner.
        /// </summary>
        private AccessDecision Deny(AccessDecision d, string reason)
        {
            d.Outcome = AccessOutcome.Deny;
            d.Reason = reason;
            return d;
        }
        /// <summary>
        /// Erişimi geçici olarak kısıtlanmış (Throttle) duruma getirir,
        /// sebebini ve tekrar deneme süresini (RetryAfterSeconds) birlikte ayarlar.
        /// </summary>
        private AccessDecision Throttle(AccessDecision d, string reason, int retrySeconds)
        {
            d.Outcome = AccessOutcome.Throttle;
            d.Reason = reason;
            d.RetryAfterSeconds = retrySeconds;
            return d;
        }
        /// <summary>
        /// Erişimi koşullu (Conditional) olarak işaretler; ilgili açıklama metnini ve yerine getirilmesi gereken yükümlülükleri (audit, yönetici onayı vb.) ekler.
        /// </summary>
        private AccessDecision Conditional(AccessDecision d, string reason, IEnumerable<Obligation> obligations)
        {
            d.Outcome = AccessOutcome.Conditional;
            d.Reason = reason;
            foreach (var o in obligations) d.Obligations.Add(o);
            return d;
        }

        #endregion 
    }
}
