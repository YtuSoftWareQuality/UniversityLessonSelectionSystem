
namespace UniversityLessonSelectionSystem.Domain.Enums
{
    public enum AccessOutcome { Allow, Deny, Conditional, Throttle }
    public enum Role { Student, Advisor, Instructor, Registrar, Admin }
    public enum Operation { ViewGrades, PublishGrades, EditTranscript, ViewPII, EditEnrollment, ViewOwnEnrollment, ViewRoster, ViewAdviseeEnrollment, EditOwnEnrollment, EditAdviseeEnrollment, EditGrades, ViewTranscript }
    public enum AccessContext { StudentOwnRecord, AdviseeRecord, CourseRoster, DepartmentWide, UniversityWide }
    public enum Obligation { AuditLog, ManagerApproval }
    public enum WorkflowState
    {
        PendingAdvisor,
        PendingDepartment,
        PendingRegistrar,
        CompletedApproved,
        CompletedDenied
    }
    public enum StepDecision { None, Approve, Reject }
    public enum AttendanceFlag { AbsenceStreak, SuddenDropStrong, SuddenDropModerate, MultiCourseCorrelation }
    public enum AttendanceAlert { None, Soft, Hard }
    public enum CourseDifficulty { Easy, Medium, Hard }
    public enum InstructorStrictness { Lenient, Moderate, Strict }
    public enum ApprovalOutcome { None, Approved, Rejected, Conditional, Waitlist }
    public enum RequiredAction { AdvisorApproval, AddCorequisite, AutoWaitlist, OverloadForm }
    public enum DayPart { Morning, Afternoon, Evening }
    public enum FeeComponentType
    {
        Tuition, Program, LabSurcharge, StudioSurcharge,
        ResidencyDifferential, LateRegistration, Scholarship, Waiver,
        Installment, InternationalSupport, HoldPenalty
    }
    public enum InvoiceKind { Debit, Credit }
    public enum CreditBand { None, PartTime, FullTime }
    public enum WaiverPolicy { None, Veteran, Staff, NeedBased }
    public enum PaymentPlan { None, Installments, InstallmentsWithFinancing }
    public enum LetterGrade { A, B, C, D, F }
    public enum SpecialMark { Incomplete, Withdraw }
    public enum NotificationEventType { Grade, Enrollment, Finance, Advisor }
    public enum NotificationChannel { SMS, Email, App }
    public enum NotifyAction { Allow, Throttle, Deny }
    public enum PrereqStatus { Unknown, Satisfied, Conditional, Rejected }
    public enum PrereqRequiredAction { AdvisorConsent }
    public enum DeptExpiryPolicy { Strict, Lenient }

    public enum ScholarshipStatus { Approved, Rejected }
    public enum ScholarshipTier { A, B, C, D, None }
    public enum NeedBand { Low, Medium, High }
    public enum GpaTier { None, Bronze, Silver, Gold }
    public enum AcademicPhase { Normal, Registration, AddDrop }

    public enum AlertCode
    {
        RegistrationLatency, WaitlistBacklog, EmailQueue, ErrorRatio, TrafficSurge,
        DbLag, CacheHit, AuthFailures, ThirdParty, Flapping, ErrorBudget, Redundancy, AcademicRisk, RegistrationThrottle, StudentEngagement
    }

    public enum DependencyKind { EmailProvider, PaymentGateway, PlagiarismAPI, SmsGateway }
    public enum DependencyState { Healthy, Degraded, Down }
    public enum DependencyCriticality { Low, Medium, Critical }
    public enum RedundancyState { HealthyMultiNode, SingleNode, NoRedundancy }
    public enum PagingPolicy { Suppress, PagePrimaryOnSingleNode, ForceEscalation }
    public enum AlertSeverity { Warning, Critical }
    public enum EngineCategory { Text, Code, AI }
    public enum RiskClass { None, Low, Medium, High }

    public enum RubricAlignment { Weak, Neutral, Strong }
    public enum PlagiarismLevel { Clean, Review, Flag }
    public enum PlagiarismAction { ManualReview, EscalateToCommittee }
    public enum AssignmentType { Essay, LabReport, Homework, FinalProject }
    public enum SubmissionLanguage { Natural, Code }
    public enum ProgramType { Undergraduate, Graduate, Executive }
    public enum StudentStanding { Good, Probation, Suspension }
    public enum ResidencyStatus { Domestic, International }
    public enum CourseLevel { Introductory, Intermediate, Advanced, GraduateOnly }
    public enum Department { CS, EE, ME, BIO, BUS, ART, LAW }
    public enum TermPhase { PreRegistration, Registration, AddDrop, Finals, Closed }
    public enum PriorityTier { Standard, Honors, Athlete, Scholarship }
    public enum RoomType { Standard, Lab, Studio, Auditorium, Online }
    public enum DaySlot { Mon, Tue, Wed, Thu, Fri, Sat }
    public enum ConflictType { None, DirectOverlap, PartialOverlap, BuildingTravel, InstructorUnavailable, RoomTypeMismatch, BufferBreach }    /// <summary>
    
    /// Identifies campus buildings for travel time and scheduling logic.
    /// Enum-only (no string literals) to maintain clean comparisons.
    /// </summary>
    public enum BuildingCode
    {
        ENG,    // Engineering complex
        SCI,    // Science center
        LIB,    // Main library
        BUS,    // Business school
        ART,    // Fine arts
        LAW,    // Law building
        ONLINE  // Virtual/off-campus
    }

    // Extra enums for ports
    public enum GradeBand { Pass, C, B, A,D }
    public enum AcademicRiskBand
    {
        Low = 0,
        Medium = 1,
        High = 2,
        Critical = 3
    }

    public enum StandingAction
    {
        LimitCredits,
        BlockNewEnrollment,
        RequireAdvisorMeeting,
        RequireDeanMeeting,
        OfferTutoring,
        FlagForEarlyAlert,
        ReviewIncompleteContracts,
        NotifyStudentAffairs
    }

    public enum TermType
    {
        Regular,
        Summer
    }
    public enum RegistrationThrottleLevel
    {
        Allow = 0,
        SoftThrottle = 1,
        HardThrottle = 2,
        Block = 3
    }
    public enum DegreeProgressRiskBand
    {
        Low = 0,
        Medium = 1,
        High = 2,
        Critical = 3
    }

    /// <summary>
    /// Öğrenci engagement bandlarını temsil eder (Low/Medium/High/Critical).
    /// </summary>
    public enum StudentEngagementBand
    {
        Low = 0,
        Medium = 1,
        High = 2,
        Critical = 3
    }
}
