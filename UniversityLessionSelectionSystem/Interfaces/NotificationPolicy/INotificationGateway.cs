namespace UniversityLessonSelectionSystem.Ports.NotificationPolicy
{

    /// <summary>Notifier for side-effect verification in allocator.</summary>
    public interface INotificationGateway
    {
        void NotifyWaitlistPromotion(string studentId, string sectionId);
    }
}
