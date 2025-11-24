using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessionSelectionSystem.Domain
{
    /// <summary>
    /// Öğrencinin dönemlik engagement hesaplamasında kullanılan sinyalleri tutan bağlam nesnesidir:
    /// yoklama oranı, LMS aktivitesi, ödev ve sınav katılımı, iletişim sayıları, akademik risk bandı vb.
    /// </summary>
    public sealed class StudentEngagementContext
    {
        public string StudentId { get; set; }
        public string TermId { get; set; }

        public decimal AttendanceRatio { get; set; }

        public int WeeklyLmsLoginCount { get; set; }
        public int LmsExpectedWeeklyLogins { get; set; }
        public int DaysSinceLastLmsLogin { get; set; }
        public int LmsRecentDaysThreshold { get; set; }
        public int LmsInactiveDaysThreshold { get; set; }

        public decimal AssignmentSubmissionRatio { get; set; }
        public decimal LateSubmissionRatio { get; set; }
        public int MissingAssignmentsCount { get; set; }

        public decimal ExamAttendanceRatio { get; set; }

        public int AdvisorContactCount { get; set; }
        public int InstructorContactCount { get; set; }
        public int AdvisorContactTarget { get; set; }
        public int InstructorContactTarget { get; set; }

        public AcademicRiskBand AcademicRiskBand { get; set; }
        public StudentStanding PreviousStanding { get; set; }
    }

}
