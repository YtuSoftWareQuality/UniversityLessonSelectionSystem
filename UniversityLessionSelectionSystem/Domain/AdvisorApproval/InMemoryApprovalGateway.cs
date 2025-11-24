using System;
using System.Collections.Generic;
using University.Lms.Ports;
using UniversityLessonSelectionSystem.Domain.AdvisorApproval;
using UniversityLessonSelectionSystem.Domain.Enums;
using UniversityLessonSelectionSystem.Ports.AdvisorApproval;

namespace University.Lms.Domain
{
    /// <summary>
    /// IApprovalGateway için basit in-memory implementasyon:
    /// - Audit kayıtlarını bellekte tutar
    /// - Sonraki aktöre ve tamamlanma bildirimlerini log'lar ve listelerde saklar.
    /// Testlerde bu listeler üzerinden doğrulama yapılabilir.
    /// </summary>
    public sealed class InMemoryApprovalGateway : IApprovalGateway
    {
        private readonly ILogger _logger;

        public IList<string> AuditLog { get; } = new List<string>();
        public IList<string> NextActorNotifications { get; } = new List<string>();
        public IList<string> CompletionNotifications { get; } = new List<string>();

        public InMemoryApprovalGateway(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void RecordAudit(string requestId, string message)
        {
            var line = $"[{DateTime.UtcNow:u}] Request={requestId} Message={message}";
            AuditLog.Add(line);
            _logger.Info(line);
        }

        public void NotifyNextActor(WorkflowState nextState, ApprovalRequest request)
        {
            var msg = $"NextActor: Request={request.RequestId} State={nextState}";
            NextActorNotifications.Add(msg);
            _logger.Info(msg);
        }

        public void NotifyCompletion(ApprovalRequest request, bool approved, string reason)
        {
            var msg = $"Completion: Request={request.RequestId} Approved={approved} Reason={reason}";
            CompletionNotifications.Add(msg);
            _logger.Info(msg);
        }
    }
}