using System;
using System.Collections.Generic;
using System.Linq;
using University.Lms.Ports;
using UniversityLessonSelectionSystem.Domain.AttendanceAnomalyDetector;
using UniversityLessonSelectionSystem.Domain.Enums;

namespace University.Lms.Services
{
    /// <summary>
    /// Flags attendance anomalies for a student using multi-signal scoring:
    ///  - Absence streaks vs rolling baseline
    ///  - Sudden drop relative to personal trend
    ///  - Course difficulty weighting & instructor strictness index
    ///  - Holiday/exception day dampening
    ///  - Multi-course correlation (concurrent dips)
    ///  - Accessibility/athlete adjustments
    /// Devamsızlık verilerinde anormallik tespit edip skorlar/etiketler üretir.
    /// Returns a score and categorized flags; high CC via layered rules.
    /// </summary>
    public sealed class AttendanceAnomalyDetectorService
    {
        #region Fields

        private readonly ILogger _logger;
        #endregion

        #region Constants

        private const int STREAK_ABSENCE_THRESHOLD = 3;
        private const decimal DROP_PERCENT_STRONG = 0.25m;  // 25% sudden drop
        private const decimal DROP_PERCENT_MEDIUM = 0.15m;

        private const decimal WEIGHT_DIFFICULTY_STRICT = 1.20m;
        private const decimal WEIGHT_DIFFICULTY_EASY = 0.90m;

        private const decimal WEIGHT_ACCESSIBILITY = 0.85m; // reduce impact
        private const decimal WEIGHT_ATHLETE_IN_SEASON = 0.80m;

        private const int CORRELATION_MIN_COURSES = 2;
        private const decimal CORRELATION_BONUS = 0.10m;

        private const int ALERT_SCORE_HARD = 80;
        private const int ALERT_SCORE_SOFT = 60;

        #endregion

        #region Constructor
        public AttendanceAnomalyDetectorService(IClock clock, ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #endregion

        #region Public Methods
        /// <summary>
        /// Computes anomaly flags and score from attendance signals.
        /// </summary>
        public AttendanceAnomalyResult Analyze(AttendanceContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            var result = new AttendanceAnomalyResult();

            int streak = LongestAbsenceStreak(ctx.Logs);
            decimal baseline = RollingBaseline(ctx.Logs);
            decimal recent = RecentPeriodAttendance(ctx.Logs);

            var flags = new HashSet<AttendanceFlag>();

            if (streak >= STREAK_ABSENCE_THRESHOLD) flags.Add(AttendanceFlag.AbsenceStreak);
            if (IsSuddenDrop(baseline, recent, DROP_PERCENT_STRONG)) flags.Add(AttendanceFlag.SuddenDropStrong);
            else if (IsSuddenDrop(baseline, recent, DROP_PERCENT_MEDIUM)) flags.Add(AttendanceFlag.SuddenDropModerate);

            if (MultiCourseCorrelation(ctx.Logs)) flags.Add(AttendanceFlag.MultiCourseCorrelation);

            // score assembly with weights
            decimal score = BaseScoreFromFlags(flags);
            score = ApplyDifficultyAndStrictness(score, ctx.CourseDifficulty, ctx.InstructorStrictness);

            score = ApplyHolidayDampening(score, ctx.Holidays);
            score = ApplyProfileAdjustments(score, ctx.Profile);

            // normalize 0..100
            int final = Clamp(0, 100, (int)Math.Round(score, 0));

            result.Score = final;
            result.Flags = flags.ToList();
            result.Alert = final >= ALERT_SCORE_HARD ? AttendanceAlert.Hard :
                final >= ALERT_SCORE_SOFT ? AttendanceAlert.Soft : AttendanceAlert.None;

            _logger.Info($"Attendance anomaly: score={result.Score} flags={string.Join(",", result.Flags)}");
            return result;
        }

        #endregion

        #region Private Methods

        #region Signals
        /// <summary>
        /// Öğrencinin devamsızlık kayıtlarında, art arda gerçekleşen en uzun devamsızlık
        /// (absent) serisini hesaplar. En uzun kesintisiz yokluk sayısını döner.
        /// </summary>
        private static int LongestAbsenceStreak(IList<AttendanceLog> logs)
        {
            int best = 0, cur = 0;
            foreach (var l in logs.OrderBy(x => x.WeekIndex))
            {
                if (!l.Present) { cur++; best = Math.Max(best, cur); }
                else cur = 0;
            }
            return best;
        }
        /// <summary>
        /// Öğrencinin ilk dönemi temsil eden log kayıtlarında ortalama katılım oranını hesaplar.
        /// Bu oran, öğrencinin normal katılım eğilimini (baseline) gösterir.
        /// </summary>
        private static decimal RollingBaseline(IList<AttendanceLog> logs)
        {
            // baseline = mean attendance over first half
            var firstHalf = logs.Where(l => l.WeekIndex <= logs.Count / 2);
            int attended = firstHalf.Count(l => l.Present);
            int total = firstHalf.Count();
            return total == 0 ? 1m : (decimal)attended / total;
        }
        /// <summary>
        /// Dönemin son yarısındaki katılım oranını hesaplar. Bu değer,
        /// son dönem katılım performansını gösterir.
        /// </summary>
        private static decimal RecentPeriodAttendance(IList<AttendanceLog> logs)
        {
            var lastHalf = logs.Where(l => l.WeekIndex > logs.Count / 2);
            int attended = lastHalf.Count(l => l.Present);
            int total = lastHalf.Count();
            return total == 0 ? 1m : (decimal)attended / total;
        }
        /// <summary>
        /// Verilen eşik (örneğin %15 veya %25) değerine göre,
        /// başlangıç katılım oranı ile son katılım oranı arasındaki düşüşün,
        /// ani ve anlamlı bir düşüş olup olmadığını kontrol eder.
        /// </summary>
        private static bool IsSuddenDrop(decimal baseline, decimal recent, decimal threshold)
        {
            if (baseline == 0m) return false;
            return (baseline - recent) / baseline >= threshold;
        }
        /// <summary>
        /// Öğrencinin birden fazla derste aynı dönemde düşüş yaşayıp yaşamadığını tespit eder.
        /// Eğer birden fazla derste düşüş varsa, çoklu ders korelasyonu bayrağı (flag) üretir.
        /// </summary>
        private static bool MultiCourseCorrelation(IList<AttendanceLog> logs)
        {
            // If multiple courseIds show the same down-trend within recent period
            var byCourse = logs.GroupBy(l => l.CourseId);
            int coursesDown = 0;

            foreach (var g in byCourse)
            {
                var arr = g.OrderBy(x => x.WeekIndex).ToList();
                var baseP = arr.Take(arr.Count / 2).Count(x => x.Present);
                var recP = arr.Skip(arr.Count / 2).Count(x => x.Present);
                if (baseP > 0 && recP < baseP) coursesDown++;
            }

            return coursesDown >= CORRELATION_MIN_COURSES;
        }

        #endregion

        #region Weighting
        /// <summary>
        /// Tespit edilen flaglere göre (AbsenceStreak, SuddenDrop vb.)
        /// başlangıç anomalilik skorunu temel olarak hesaplar.
        /// Her flagin kendine ait bir puan katkısı vardır.
        /// </summary>
        private static decimal BaseScoreFromFlags(HashSet<AttendanceFlag> flags)
        {
            decimal score = 0m;
            if (flags.Contains(AttendanceFlag.AbsenceStreak)) score += 50m;
            if (flags.Contains(AttendanceFlag.SuddenDropStrong)) score += 40m;
            if (flags.Contains(AttendanceFlag.SuddenDropModerate)) score += 25m;
            if (flags.Contains(AttendanceFlag.MultiCourseCorrelation)) score += 20m;
            return score;
        }
        /// <summary>
        /// Ders zorluk derecesi ve eğitmenin katılık seviyesine göre
        /// başlangıç skorunu çarpanlarla (weight) yeniden hesaplar.
        /// Zor ders ve katı eğitmen kombinasyonu skoru artırabilir.
        /// </summary>
        private static decimal ApplyDifficultyAndStrictness(decimal score, CourseDifficulty diff, InstructorStrictness strict)
        {
            if (diff == CourseDifficulty.Hard && strict == InstructorStrictness.Strict) score *= WEIGHT_DIFFICULTY_STRICT;
            else if (diff == CourseDifficulty.Easy && strict == InstructorStrictness.Lenient) score *= WEIGHT_DIFFICULTY_EASY;
            return score;
        }
        /// <summary>
        /// Resmî tatil veya istisna haftaları varsa, bunların devamsızlık yorumlamasına olan
        /// etkisini hafifleterek (dampen) toplam skoru düşürür.
        /// </summary>
        private static decimal ApplyHolidayDampening(decimal score, IList<int> holidayWeekIndices)
        {
            if (holidayWeekIndices == null || holidayWeekIndices.Count == 0) return score;
            // dampen by small correlation bonus to counter false positives
            return score * (1m - CORRELATION_BONUS);
        }
        /// <summary>
        /// Öğrencinin profil özelliklerine (erişilebilirlik ihtiyacı, sporcu sezonu vb.)
        /// göre skor üzerinde ayarlamalar yapar. Bu profil faktörleri skoru azaltır.
        /// </summary>
        private static decimal ApplyProfileAdjustments(decimal score, StudentProfile profile)
        {
            if (profile.HasAccessibilityNeeds) score *= WEIGHT_ACCESSIBILITY;
            if (profile.IsAthleteInSeason) score *= WEIGHT_ATHLETE_IN_SEASON;
            return score;
        }
        /// <summary>
        /// Hesaplanan skoru 0 ile 100 arasında sınırlandırır.
        /// Minimum değerin altına inmesine veya maksimum değeri aşmasına izin vermez.
        /// </summary>

        private static int Clamp(int min, int max, int v) => Math.Min(max, Math.Max(min, v));

        #endregion

        #endregion
    }
}
