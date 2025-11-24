using System;
using System.Collections.Generic;
using System.Linq; 
using UniversityLessonSelectionSystem.Domain.EnrollmentEligibility;
using UniversityLessonSelectionSystem.Domain.Enums;
using UniversityLessonSelectionSystem.Ports.EnrollmentEligibility;

namespace University.Lms.Domain
{
    /// <summary>
    /// Öğrenci temel bilgisi, tamamlanan dersler, kredi yükü ve danışman onaylarını
    /// bellek içi tutan repository implementasyonu.
    /// </summary>
    public sealed class InMemoryStudentRepo : IStudentRepo
    {
        private readonly IDictionary<string, Student> _students = new Dictionary<string, Student>();
        private readonly IList<CompletedCourseRecord> _completed = new List<CompletedCourseRecord>();
        private readonly IList<CreditLoadRecord> _loads = new List<CreditLoadRecord>();
        private readonly IList<AdvisorApprovalRecord> _advisorApprovals = new List<AdvisorApprovalRecord>();

        public InMemoryStudentRepo()
        {
            var stu = new Student
            {
                Id = "S1",
                Program = ProgramType.Undergraduate,
                Standing = StudentStanding.Good,
                Gpa = 3.4m
            };
            _students[stu.Id] = stu;

            _completed.Add(new CompletedCourseRecord
            {
                StudentId = "S1",
                CourseId = "CS101",
                Grade = GradeBand.B
            });

            _loads.Add(new CreditLoadRecord
            {
                StudentId = "S1",
                TermId = "2025FA",
                Credits = 12
            });

            _advisorApprovals.Add(new AdvisorApprovalRecord
            {
                StudentId = "S1",
                SectionId = "CS201-1",
                Approved = true
            });
        }

        public Student GetById(string studentId)
        {
            if (studentId == null) throw new ArgumentNullException(nameof(studentId));
            Student s;
            if (!_students.TryGetValue(studentId, out s))
                throw new KeyNotFoundException("Student not found: " + studentId);
            return s;
        }

        public int CurrentCreditLoad(string studentId, string termId)
        {
            if (studentId == null) throw new ArgumentNullException(nameof(studentId));
            if (termId == null) throw new ArgumentNullException(nameof(termId));

            return _loads
                .Where(x => x.StudentId == studentId && x.TermId == termId)
                .Select(x => x.Credits)
                .DefaultIfEmpty(0)
                .Sum();
        }

        public bool HasCompleted(string studentId, string courseId, GradeBand minGrade)
        {
            if (studentId == null) throw new ArgumentNullException(nameof(studentId));
            if (courseId == null) throw new ArgumentNullException(nameof(courseId));

            return _completed.Any(x =>
                x.StudentId == studentId &&
                x.CourseId == courseId &&
                GradeValue(x.Grade) >= GradeValue(minGrade));
        }

        public bool HasAdvisorApproval(string studentId, string sectionId)
        {
            if (studentId == null) throw new ArgumentNullException(nameof(studentId));
            if (sectionId == null) throw new ArgumentNullException(nameof(sectionId));

            return _advisorApprovals.Any(x =>
                x.StudentId == studentId &&
                x.SectionId == sectionId &&
                x.Approved);
        }

        public void SeedStudent(Student student)
        {
            if (student == null) throw new ArgumentNullException(nameof(student));
            _students[student.Id] = student;
        }

        public void SeedCompletedCourse(string studentId, string courseId, GradeBand grade)
        {
            _completed.Add(new CompletedCourseRecord
            {
                StudentId = studentId,
                CourseId = courseId,
                Grade = grade
            });
        }

        public void SeedCreditLoad(string studentId, string termId, int credits)
        {
            _loads.Add(new CreditLoadRecord
            {
                StudentId = studentId,
                TermId = termId,
                Credits = credits
            });
        }

        public void SeedAdvisorApproval(string studentId, string sectionId, bool approved)
        {
            _advisorApprovals.Add(new AdvisorApprovalRecord
            {
                StudentId = studentId,
                SectionId = sectionId,
                Approved = approved
            });
        }

        private static int GradeValue(GradeBand g)
        {
            switch (g)
            {
                case GradeBand.A:
                    return 4;
                case GradeBand.B:
                    return 3;
                case GradeBand.C:
                    return 2;
                case GradeBand.D:
                    return 1;
                default:
                    return 0;
            }
        }

        private sealed class CompletedCourseRecord
        {
            public string StudentId { get; set; }
            public string CourseId { get; set; }
            public GradeBand Grade { get; set; }
        }

        private sealed class CreditLoadRecord
        {
            public string StudentId { get; set; }
            public string TermId { get; set; }
            public int Credits { get; set; }
        }

        private sealed class AdvisorApprovalRecord
        {
            public string StudentId { get; set; }
            public string SectionId { get; set; }
            public bool Approved { get; set; }
        }
    }
}
