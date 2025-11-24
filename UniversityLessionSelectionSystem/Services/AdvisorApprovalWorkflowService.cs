using System;
using University.Lms.Ports;
using UniversityLessionSelectionSystem.Domain.AdvisorApproval;
using UniversityLessonSelectionSystem.Domain.AdvisorApproval;
using UniversityLessonSelectionSystem.Domain.Enums;
using UniversityLessonSelectionSystem.Ports.AdvisorApproval;

namespace University.Lms.Services
{
    /// <summary>
    /// Runs a multi-step approval workflow for enrollments and overloads:
    ///  - Steps: Advisor → Department → Registrar
    ///  - Timeouts & escalation timers
    ///  - Conditional auto-approve/deny policies by GPA/standing/term phase
    ///  - Rejections with re-apply windows
    ///  - Audit trail recording and notification hooks
    /// Danışman bazlı çok adımlı onay akışlarını, zaman aşımı ve eskalasyonla yürütür.
    /// </summary>
    public sealed class AdvisorApprovalWorkflowService
    {
        #region Fields

        private readonly IClock _clock;
        private readonly ILogger _logger;
        private readonly IApprovalGateway _gateway;

        #endregion

        #region Constants

        private const int ADVISOR_TIMEOUT_HOURS = 24;
        private const int DEPT_TIMEOUT_HOURS = 24;
        private const int REG_TIMEOUT_HOURS = 24;

        private const decimal GPA_AUTO_APPROVE = 3.80m;
        private const decimal GPA_AUTO_DENY = 1.80m;

        private const int REAPPLY_WINDOW_DAYS = 7;

        #endregion

        #region Constructor
        public AdvisorApprovalWorkflowService(IClock clock, ILogger logger, IApprovalGateway gateway)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        }

        #endregion

        #region Public Methods
        /// <summary>
        /// Processes a workflow tick (idempotent), advancing state as needed and returning snapshot.
        /// </summary>
        public WorkflowSnapshot Tick(ApprovalRequest req)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));

            var snapshot = new WorkflowSnapshot
            {
                State = req.State,
                LastUpdateUtc = _clock.UtcNow
            };

            // Auto decisions
            if (req.Gpa >= GPA_AUTO_APPROVE && req.State == WorkflowState.PendingAdvisor)
            {
                snapshot.Events.Add("Auto-approve by GPA threshold.");
                return Advance(snapshot, req, WorkflowState.PendingDepartment);
            }
            if (req.Gpa <= GPA_AUTO_DENY)
            {
                snapshot.Events.Add("Auto-deny by low GPA threshold.");
                return Deny(snapshot, req, "GPA below threshold.");
            }

            // Timeouts & steps
            switch (req.State)
            {
                case WorkflowState.PendingAdvisor:
                    if (TimedOut(req.SubmittedUtc, ADVISOR_TIMEOUT_HOURS))
                        return Escalate(snapshot, req, WorkflowState.PendingDepartment, "Advisor timeout escalation.");
                    if (req.Decision == StepDecision.Approve)
                        return Advance(snapshot, req, WorkflowState.PendingDepartment);
                    if (req.Decision == StepDecision.Reject)
                        return Deny(snapshot, req, "Advisor rejected.");
                    break;

                case WorkflowState.PendingDepartment:
                    if (TimedOut(req.LastStepUtc, DEPT_TIMEOUT_HOURS))
                        return Escalate(snapshot, req, WorkflowState.PendingRegistrar, "Department timeout escalation.");
                    if (req.Decision == StepDecision.Approve)
                        return Advance(snapshot, req, WorkflowState.PendingRegistrar);
                    if (req.Decision == StepDecision.Reject)
                        return Deny(snapshot, req, "Department rejected.");
                    break;

                case WorkflowState.PendingRegistrar:
                    if (TimedOut(req.LastStepUtc, REG_TIMEOUT_HOURS))
                        return Escalate(snapshot, req, WorkflowState.CompletedDenied, "Registrar timeout auto-deny.");
                    if (req.Decision == StepDecision.Approve)
                        return Complete(snapshot, req, true);
                    if (req.Decision == StepDecision.Reject)
                        return Complete(snapshot, req, false);
                    break;

                case WorkflowState.CompletedApproved:
                case WorkflowState.CompletedDenied:
                    snapshot.Events.Add("No-op (terminal).");
                    break;
            }

            return snapshot;
        }

        #endregion

        #region Private Methods
        /// <summary>
        /// Verilen başlangıç zamanından itibaren belirli saat sınırının aşılıp aşılmadığını kontrol ederek
        /// adımın zaman aşımına uğrayıp uğramadığını döner.
        /// </summary>
        private bool TimedOut(DateTime since, int hours) =>
            (_clock.UtcNow - since).TotalHours >= hours;

        /// <summary>
        /// Onay akışı bir sonraki duruma geçtiğinde,
        /// hem workflow durumunu günceller hem de IApprovalGateway üzerinden audit kaydı ve sonraki aktöre bildirim gönderir.
        /// </summary>
        private WorkflowSnapshot Advance(WorkflowSnapshot s, ApprovalRequest r, WorkflowState next)
        {
            s.Events.Add($"Advance {r.State} → {next}");
            s.State = next;
            _gateway.RecordAudit(r.RequestId, $"Advance to {next}");
            _gateway.NotifyNextActor(next, r);
            return s;
        }

        /// <summary>
        /// Belirli bir adım zaman aşımına uğradığında,
        /// isteği bir üst seviyeye (örneğin danışmandan bölüme veya registrara) eskale eder
        /// ve audit ile bildirimleri kaydeder.
        /// </summary>
        private WorkflowSnapshot Escalate(WorkflowSnapshot s, ApprovalRequest r, WorkflowState next, string reason)
        {
            s.Events.Add($"Escalate to {next}: {reason}");
            s.State = next;
            _gateway.RecordAudit(r.RequestId, $"Escalation: {reason}");
            _gateway.NotifyNextActor(next, r);
            return s;
        }

        /// <summary>
        /// Onay talebini gerekçesiyle birlikte reddedilmiş duruma getirir,
        /// yeniden başvuru için tarih belirler ve IApprovalGateway üzerinden audit ile tamamlanma bildirimini gönderir.
        /// </summary>
        private WorkflowSnapshot Deny(WorkflowSnapshot s, ApprovalRequest r, string reason)
        {
            s.Events.Add($"Denied: {reason}");
            s.State = WorkflowState.CompletedDenied;
            s.ReapplyAfterUtc = _clock.UtcNow.AddDays(REAPPLY_WINDOW_DAYS);
            _gateway.RecordAudit(r.RequestId, $"Denied: {reason}");
            _gateway.NotifyCompletion(r, false, reason);
            return s;
        }

        /// <summary>
        /// Akışı onaylanmış veya reddedilmiş terminal durumlardan birine taşır,
        /// gerekiyorsa yeniden başvuru süresini belirler ve tamamlanma bildirimini yollar.
        /// </summary>
        private WorkflowSnapshot Complete(WorkflowSnapshot s, ApprovalRequest r, bool approved)
        {
            s.Events.Add(approved ? "Approved." : "Denied.");
            s.State = approved ? WorkflowState.CompletedApproved : WorkflowState.CompletedDenied;
            if (!approved) s.ReapplyAfterUtc = _clock.UtcNow.AddDays(REAPPLY_WINDOW_DAYS);
            _gateway.RecordAudit(r.RequestId, approved ? "Approved" : "Denied");
            _gateway.NotifyCompletion(r, approved, approved ? null : "Registrar rejected.");
            return s;
        }

        #endregion
    }
}
