namespace UniversityLessonSelectionSystem.Domain.CapacityWaitlistAllocator
{
    /// <summary>
    /// Bir bekleme listesi kaydını temsil eder; sporcu/burslu/onur programı durumu, mezuniyete kalan kredi,
    /// finansal ve kimlik onayı gibi öncelik hesaplamasında kullanılan ipuçlarını içerir.
    /// </summary>
    public sealed class WaitlistEntry
    {
        public string StudentId { get; set; }
        public bool IsAthlete { get; set; }
        public bool IsScholarship { get; set; }
        public bool IsHonors { get; set; }
        public int CreditsToGraduate { get; set; }
        public bool HasAdvisorApproval { get; set; }
        public bool HasFinancialClearance { get; set; }
        public bool HasIdentityVerified { get; set; }
        public int PositionTimestampUnix { get; set; }
        public int ExpiryUnix { get; set; } // 0 = none
    }
}
