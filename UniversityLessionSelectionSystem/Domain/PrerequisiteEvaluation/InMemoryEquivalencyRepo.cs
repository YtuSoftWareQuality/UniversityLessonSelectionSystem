using System;
using System.Collections.Generic;
using System.Linq;
using UniversityLessonSelectionSystem.Domain.PrerequisiteEvaluation; 
using UniversityLessonSelectionSystem.Ports.PrerequisiteEvaluation;

namespace University.Lms.Domain
{
    /// <summary>
    /// Ders eşdeğerlikleri (equivalents ve transfer substitutions) için basit repo.
    /// </summary>
    public sealed class InMemoryEquivalencyRepo : IEquivalencyRepo
    {
        private readonly IList<EquivalentCourse> _equivalents = new List<EquivalentCourse>();
        private readonly IList<EquivalentCourse> _transfers = new List<EquivalentCourse>();

        public IList<EquivalentCourse> GetEquivalents(string courseId)
        {
            if (courseId == null) throw new ArgumentNullException(nameof(courseId));
            return _equivalents.Where(e => e.CourseId == courseId).ToList();
        } 
        public IList<EquivalentCourse> GetTransferSubstitutions(string prerequisiteCourseId)
        {
            if (prerequisiteCourseId == null) throw new ArgumentNullException(nameof(prerequisiteCourseId));
            return _transfers.Where(e => e.CourseId == prerequisiteCourseId).ToList();
        }

        public void SeedEquivalent(EquivalentCourse e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            _equivalents.Add(e);
        }

        public void SeedTransfer(EquivalentCourse e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            _transfers.Add(e);
        }
    }
}