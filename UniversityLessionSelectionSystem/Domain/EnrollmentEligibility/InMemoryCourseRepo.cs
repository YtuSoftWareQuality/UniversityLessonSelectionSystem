using System;
using System.Collections.Generic;
using System.Linq; 
using UniversityLessionSelectionSystem.Ports.EnrollmentEligibility;
using UniversityLessonSelectionSystem.Domain.EnrollmentEligibility;
using UniversityLessonSelectionSystem.Domain.Enums;

namespace University.Lms.Domain
{
    /// <summary>
    /// Ders ve section verilerini bellek içi koleksiyonlarda tutan repository.
    /// - GetCourse, GetSection, GetStudentSections metotlarını sağlar.
    /// </summary>
    public sealed class InMemoryCourseRepo : ICourseRepo
    {
        private readonly IDictionary<string, Course> _courses = new Dictionary<string, Course>();
        private readonly IDictionary<string, Section> _sections = new Dictionary<string, Section>();
        private readonly IList<StudentSectionEnrollment> _enrollments = new List<StudentSectionEnrollment>();

        public InMemoryCourseRepo()
        {
            // Basit demo seed
            var cs101 = new Course
            {
                Id = "CS101",
                Department = Department.CS,
                Level = CourseLevel.Introductory,
                Credits = 3
            };
            var cs201 = new Course
            {
                Id = "CS201",
                Department = Department.CS,
                Level = CourseLevel.Intermediate,
                Credits = 4
            };

            _courses[cs101.Id] = cs101;
            _courses[cs201.Id] = cs201;

            var s1 = new Section
            {
                Id = "CS101-1",
                CourseId = "CS101",
                Capacity = 30,
                Enrolled = 10,
                Building = BuildingCode.ENG,
                PriorityTier = PriorityTier.Standard,
                Slots = new List<ScheduleSlot>()
            };

            var s2 = new Section
            {
                Id = "CS201-1",
                CourseId = "CS201",
                Capacity = 25,
                Enrolled = 5,
                Building = BuildingCode.ONLINE,
                PriorityTier = PriorityTier.Honors,
                Slots = new List<ScheduleSlot>()
            };

            _sections[s1.Id] = s1;
            _sections[s2.Id] = s2;
        }

        public Course GetCourse(string courseId)
        {
            if (courseId == null) throw new ArgumentNullException(nameof(courseId));
            Course course;
            if (!_courses.TryGetValue(courseId, out course))
                throw new KeyNotFoundException("Course not found: " + courseId);
            return course;
        }

        public Section GetSection(string sectionId)
        {
            if (sectionId == null) throw new ArgumentNullException(nameof(sectionId));
            Section section;
            if (!_sections.TryGetValue(sectionId, out section))
                throw new KeyNotFoundException("Section not found: " + sectionId);
            return section;
        }

        public IList<Section> GetStudentSections(string studentId, string termId)
        {
            if (studentId == null) throw new ArgumentNullException(nameof(studentId));
            if (termId == null) throw new ArgumentNullException(nameof(termId));

            var sectionIds = _enrollments
                .Where(e => e.StudentId == studentId && e.TermId == termId)
                .Select(e => e.SectionId)
                .Distinct()
                .ToList();

            return sectionIds
                .Where(_sections.ContainsKey)
                .Select(id => _sections[id])
                .ToList();
        }

        public void SeedCourse(Course course)
        {
            if (course == null) throw new ArgumentNullException(nameof(course));
            _courses[course.Id] = course;
        }

        public void SeedSection(Section section)
        {
            if (section == null) throw new ArgumentNullException(nameof(section));
            _sections[section.Id] = section;
        }

        public void SeedEnrollment(StudentSectionEnrollment enrollment)
        {
            if (enrollment == null) throw new ArgumentNullException(nameof(enrollment));
            _enrollments.Add(enrollment);
        }
    }

    /// <summary>Öğrenci-section ilişkisinin basit temsilidir.</summary>
    public sealed class StudentSectionEnrollment
    {
        public string StudentId { get; set; }
        public string SectionId { get; set; }
        public string TermId { get; set; }
    }
}
