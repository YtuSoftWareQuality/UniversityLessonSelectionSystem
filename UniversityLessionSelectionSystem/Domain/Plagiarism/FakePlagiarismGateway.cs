using System;
using System.Collections.Generic;
using UniversityLessonSelectionSystem.Domain.Enums;
using UniversityLessonSelectionSystem.Domain.Plagiarism;
using UniversityLessonSelectionSystem.Ports;
using UniversityLessonSelectionSystem.Ports.Plagiarism;

namespace University.Lms.Infrastructure
{
    /// <summary>
    /// Intihal kontrolünü simüle eden gateway: verilen submission için basit bir skor döner.
    /// Gerçek sistemde burada 3. parti bir servise HTTP çağrısı yapılır.
    /// </summary>
    public sealed class FakePlagiarismGateway : IPlagiarismGateway
    {
        public IList<EngineScore> RunAll(PlagiarismContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            return new List<EngineScore>();
        }
    }

    
}
