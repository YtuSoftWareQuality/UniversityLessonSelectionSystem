using System;
using University.Lms.Ports;
using UniversityLessionSelectionSystem.Domain;
using UniversityLessonSelectionSystem.Domain.Enums;  
using UniversityLessonSelectionSystem.Ports.SLAAndHealthMonitor;
using UniversityLessonSelectionSystem.Domain.SLAAndHealthMonitor;
using UniversityLessonSelectionSystem.Ports.EnrollmentEligibility;

namespace University.Lms.Services
{
    /// <summary>
    /// Öğrencinin dönem içi "engagement / katılım" düzeyini çoklu sinyaller üzerinden hesaplayan servistir:
    /// - Ders bazlı yoklama (attendance) oranları,
    /// - LMS giriş sıklığı ve son giriş zamanı,
    /// - Ödev teslim oranı ve geç teslim davranışı,
    /// - Sınavlara katılım oranı,
    /// - Danışman/öğretim elemanı ile iletişim (mesajlaşma/randevu) yoğunluğu,
    /// - Akademik risk bandı (AcademicRiskBand) ve akademik durum (Standing),
    /// - Program türü (lisans/lisansüstü) ve ders yükü,
    /// gibi faktörleri ayrı katmanlarda değerlendirir; her katman skor üzerinde pozitif/negatif
    /// düzeltmeler yapar, sonunda 0–100 arası bir engagement skoru ve Low/Medium/High/Critical
    /// engagement bandı üretir. Gerekli durumlarda Alert üreterek erken uyarı sağlar.
    /// Yüksek karar dallanmasına sahip, ancak küçük, saf kurallara bölünmüş ve DI ile rahatça
    /// test edilebilir olacak şekilde tasarlanmıştır.
    /// </summary>
    public sealed class StudentEngagementScoringService
    {
        #region Fields

        private readonly ILogger _logger;
        private readonly IStudentRepo _students;
        private readonly IAlertGateway _alerts;

        #endregion

        #region Policy Constants

        private const int BASE_SCORE_DEFAULT = 50;

        private const int ATTENDANCE_HIGH_BONUS = 15;
        private const int ATTENDANCE_MEDIUM_BONUS = 5;
        private const int ATTENDANCE_LOW_PENALTY = -15;
        private const int ATTENDANCE_CRITICAL_PENALTY = -25;

        private const int LMS_ACTIVE_BONUS = 10;
        private const int LMS_RARE_PENALTY = -10;
        private const int LMS_INACTIVE_PENALTY = -20;

        private const int ASSIGNMENT_FULL_BONUS = 15;
        private const int ASSIGNMENT_MEDIUM_BONUS = 5;
        private const int ASSIGNMENT_LOW_PENALTY = -10;
        private const int ASSIGNMENT_MISSING_PENALTY = -20;
        private const int ASSIGNMENT_LATE_PENALTY = -5;

        private const int EXAM_FULL_BONUS = 10;
        private const int EXAM_PARTIAL_PENALTY = -10;
        private const int EXAM_MISSING_PENALTY = -25;

        private const int COMMUNICATION_ACTIVE_BONUS = 10;
        private const int COMMUNICATION_NONE_PENALTY = -5;

        private const int ACADEMIC_RISK_HIGH_PENALTY = -10;
        private const int ACADEMIC_RISK_CRITICAL_PENALTY = -20;
        private const int ACADEMIC_RISK_RECOVERY_BONUS = 5;

        private const int PROGRAM_GRAD_SCALING_BONUS = 5;

        private const int SCORE_MIN = 0;
        private const int SCORE_MAX = 100;

        private const int BAND_HIGH_MIN = 70;
        private const int BAND_MEDIUM_MIN = 40;
        private const int BAND_CRITICAL_MAX = 20;

        private const int ALERT_ENGAGEMENT_LOW_THRESHOLD = 30;
        private const int ALERT_ENGAGEMENT_CRITICAL_THRESHOLD = 15;

        #endregion

        #region Constructor

        /// <summary>
        /// Logger, öğrenci deposu ve alert gateway bağımlılıklarını alarak,
        /// öğrencinin dönem içi engagement skorunu hesaplayacak servis örneğini oluşturur.
        /// </summary>
        public StudentEngagementScoringService(
            ILogger logger,
            IStudentRepo students,
            IAlertGateway alerts)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _students = students ?? throw new ArgumentNullException(nameof(students));
            _alerts = alerts ?? throw new ArgumentNullException(nameof(alerts));
        }

        #endregion

        #region Public API

        /// <summary>
        /// Verilen öğrenci engagement bağlamına göre (attendance, LMS, assignment, sınav, iletişim, risk bandı),
        /// katmanlı skor düzeltmelerini uygulayarak 0–100 arası bir engagement skoru ve bandı üretir;
        /// gerekli durumlarda Alert gönderir ve karar nesnesini döner.
        /// </summary>
        public StudentEngagementDecision Evaluate(StudentEngagementContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            var student = _students.GetById(ctx.StudentId);
            int score = BASE_SCORE_DEFAULT;

            score = ApplyAttendanceLayer(score, ctx);
            score = ApplyLmsActivityLayer(score, ctx);
            score = ApplyAssignmentLayer(score, ctx);
            score = ApplyExamLayer(score, ctx);
            score = ApplyCommunicationLayer(score, ctx);
            score = ApplyAcademicRiskLayer(score, ctx);
            score = ApplyProgramScaling(score, student.Program);

            score = ClampScore(score);

            var band = MapScoreToBand(score);
            var decision = new StudentEngagementDecision
            {
                StudentId = ctx.StudentId,
                Score = score,
                Band = band
            };

            MaybeEmitAlert(ctx, decision);

            _logger.Info($"Engagement for {ctx.StudentId}: Score={score}, Band={band}");
            return decision;
        }

        #endregion

        #region Private - Layer rules

        /// <summary>
        /// Öğrencinin yoklama oranını (attendance ratio) bandlara ayırarak skor üzerinde
        /// pozitif/negatif düzeltme yapar; çok düşük attendance durumunda güçlü ceza uygular.
        /// </summary>
        private int ApplyAttendanceLayer(int score, StudentEngagementContext ctx)
        {
            if (ctx.AttendanceRatio >= 0.9m)
            {
                score += ATTENDANCE_HIGH_BONUS;
            }
            else if (ctx.AttendanceRatio >= 0.7m)
            {
                score += ATTENDANCE_MEDIUM_BONUS;
            }
            else if (ctx.AttendanceRatio >= 0.4m)
            {
                score += ATTENDANCE_LOW_PENALTY;
            }
            else
            {
                score += ATTENDANCE_CRITICAL_PENALTY;
            }

            return score;
        }

        /// <summary>
        /// LMS giriş sıklığı ve son giriş tarihine göre öğrencinin çevrimiçi aktivitesini değerlendirir;
        /// sık ve yakın tarihli girişler bonus, nadir veya çok eski girişler ceza olarak skora yansır.
        /// </summary>
        private int ApplyLmsActivityLayer(int score, StudentEngagementContext ctx)
        {
            if (ctx.WeeklyLmsLoginCount >= ctx.LmsExpectedWeeklyLogins &&
                (ctx.DaysSinceLastLmsLogin <= ctx.LmsRecentDaysThreshold))
            {
                score += LMS_ACTIVE_BONUS;
            }
            else if (ctx.WeeklyLmsLoginCount == 0 ||
                     ctx.DaysSinceLastLmsLogin > ctx.LmsInactiveDaysThreshold)
            {
                score += LMS_INACTIVE_PENALTY;
            }
            else
            {
                score += LMS_RARE_PENALTY;
            }

            return score;
        }

        /// <summary>
        /// Ödev teslim oranı, eksik ödev sayısı ve geç teslim oranını kullanarak;
        /// yüksek teslim oranında bonus, eksik veya sistematik geç teslimde ceza uygular ve
        /// skoru buna göre günceller.
        /// </summary>
        private int ApplyAssignmentLayer(int score, StudentEngagementContext ctx)
        {
            if (ctx.AssignmentSubmissionRatio >= 0.95m && ctx.LateSubmissionRatio <= 0.05m)
            {
                score += ASSIGNMENT_FULL_BONUS;
            }
            else if (ctx.AssignmentSubmissionRatio >= 0.75m)
            {
                score += ASSIGNMENT_MEDIUM_BONUS;
            }
            else if (ctx.AssignmentSubmissionRatio <= 0.4m && ctx.MissingAssignmentsCount > 0)
            {
                score += ASSIGNMENT_MISSING_PENALTY;
            }
            else
            {
                score += ASSIGNMENT_LOW_PENALTY;
            }

            if (ctx.LateSubmissionRatio > 0.3m)
            {
                score += ASSIGNMENT_LATE_PENALTY;
            }

            return score;
        }

        /// <summary>
        /// Sınavlara katılım oranını (exam attendance) değerlendirir; tüm sınavlara girilen
        /// durumlarda bonus, kısmi katılım veya hiç katılmama durumunda yüksek ceza uygular.
        /// </summary>
        private int ApplyExamLayer(int score, StudentEngagementContext ctx)
        {
            if (ctx.ExamAttendanceRatio >= 0.95m)
            {
                score += EXAM_FULL_BONUS;
            }
            else if (ctx.ExamAttendanceRatio >= 0.6m)
            {
                score += EXAM_PARTIAL_PENALTY;
            }
            else
            {
                score += EXAM_MISSING_PENALTY;
            }

            return score;
        }

        /// <summary>
        /// Danışman ve öğretim elemanları ile iletişim (mesajlaşma, randevu) yoğunluğuna göre
        /// skor üzerinde düzeltme yapar; hiç iletişim yoksa hafif ceza, dengeli iletişim varsa bonus verir.
        /// </summary>
        private int ApplyCommunicationLayer(int score, StudentEngagementContext ctx)
        {
            if (ctx.AdvisorContactCount + ctx.InstructorContactCount == 0)
            {
                score += COMMUNICATION_NONE_PENALTY;
            }
            else if (ctx.AdvisorContactCount >= ctx.AdvisorContactTarget ||
                     ctx.InstructorContactCount >= ctx.InstructorContactTarget)
            {
                score += COMMUNICATION_ACTIVE_BONUS;
            }

            return score;
        }

        /// <summary>
        /// Öğrencinin akademik risk bandını (AcademicRiskBand) ve önceki dönem akademik durumunu
        /// dikkate alarak engagement skoruna risk bazlı düzeltme uygular; yüksek riskte ceza,
        /// risk düşüşünde (recovery) bonus verir.
        /// </summary>
        private int ApplyAcademicRiskLayer(int score, StudentEngagementContext ctx)
        {
            if (ctx.AcademicRiskBand == AcademicRiskBand.Critical)
            {
                score += ACADEMIC_RISK_CRITICAL_PENALTY;
            }
            else if (ctx.AcademicRiskBand == AcademicRiskBand.High)
            {
                score += ACADEMIC_RISK_HIGH_PENALTY;
            }
            else if (ctx.AcademicRiskBand == AcademicRiskBand.Low &&
                     ctx.PreviousStanding == StudentStanding.Probation)
            {
                score += ACADEMIC_RISK_RECOVERY_BONUS;
            }

            return score;
        }

        /// <summary>
        /// Program türüne (lisans/lisansüstü) göre küçük bir ölçeklendirme düzeltmesi uygular;
        /// lisansüstü programların yüksek iş yükü varsayımı ile hafif bonus verebilir.
        /// </summary>
        private int ApplyProgramScaling(int score, ProgramType program)
        {
            if (program == ProgramType.Graduate)
            {
                score += PROGRAM_GRAD_SCALING_BONUS;
            }

            return score;
        }

        #endregion

        #region Private - Score & band helpers

        /// <summary>
        /// Skoru tanımlı minimum ve maksimum aralığa (0–100) sıkıştırır;
        /// alt veya üst limitleri aşan değerleri kırparak normalize eder.
        /// </summary>
        private int ClampScore(int score)
        {
            if (score < SCORE_MIN) return SCORE_MIN;
            if (score > SCORE_MAX) return SCORE_MAX;
            return score;
        }

        /// <summary>
        /// Nihai engagement skorunu eşik değerlerine göre bandlara (Low/Medium/High/Critical)
        /// dönüştürür; en düşük skorlar Critical, orta skorlar Medium, yüksek skorlar High bandına atanır.
        /// </summary>
        private StudentEngagementBand MapScoreToBand(int score)
        {
            if (score <= BAND_CRITICAL_MAX)
            {
                return StudentEngagementBand.Critical;
            }

            if (score < BAND_MEDIUM_MIN)
            {
                return StudentEngagementBand.Low;
            }

            if (score < BAND_HIGH_MIN)
            {
                return StudentEngagementBand.Medium;
            }

            return StudentEngagementBand.High;
        }

        /// <summary>
        /// Engagement skoru kritik eşiklerin altına düştüğünde (özellikle Critical veya çok düşük Low),
        /// IAlertGateway üzerinden erken uyarı (Alert) üretir; yüksek riskte escalasyon bayrağını aktif eder.
        /// </summary>
        private void MaybeEmitAlert(StudentEngagementContext ctx, StudentEngagementDecision decision)
        {
            if (decision.Score > ALERT_ENGAGEMENT_LOW_THRESHOLD) return;

            var alert = new Alert
            {
                Id = $"ENG-{ctx.StudentId}-{ctx.TermId}",
                Code = AlertCode.StudentEngagement,
                Message = $"EngagementScore={decision.Score}, Band={decision.Band}, Student={ctx.StudentId}",
                Severity = decision.Score <= ALERT_ENGAGEMENT_CRITICAL_THRESHOLD
                    ? AlertSeverity.Critical
                    : AlertSeverity.Warning,
                Escalate = decision.Score <= ALERT_ENGAGEMENT_CRITICAL_THRESHOLD
            };

            _alerts.Record(alert);

            if (alert.Escalate)
            {
                _alerts.Escalate(alert);
            }
        }

        #endregion
    }
} 
