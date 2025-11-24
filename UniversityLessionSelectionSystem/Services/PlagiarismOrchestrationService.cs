using System;
using System.Collections.Generic;
using System.Linq; 
using University.Lms.Ports;
using UniversityLessonSelectionSystem.Domain.Enums;
using UniversityLessonSelectionSystem.Domain.Plagiarism;
using UniversityLessonSelectionSystem.Ports;
using UniversityLessonSelectionSystem.Ports.Plagiarism;

namespace University.Lms.Services
{
    /// <summary> 
    /// Çoklu tespit motoru skorlarını birleştirerek intihal düzeyini hesaplar ve eskalasyon kararları oluşturur.
    /// </summary>
    public sealed class PlagiarismOrchestrationService
    {
        private readonly ILogger _logger;
        private readonly IPlagiarismGateway _gateway;
        private readonly IPlagiarismPolicyRepo _policy;

        #region Thresholds & Policy Constants

        private const int MIN_TOKENS_GENERIC = 200;
        private const int MIN_TOKENS_CODE = 80;

        private const decimal TIER_REVIEW = 0.35m;
        private const decimal TIER_FLAG = 0.55m;

        private const decimal SELF_ALLOW_RATIO = 0.20m;
        private const decimal GROUP_ALLOW_RATIO = 0.30m;
        private const decimal CITATION_DAMPENING = 0.15m;
        private const decimal TEMPLATE_DAMPENING = 0.10m;
        private const decimal RUBRIC_ALIGNMENT_BONUS = 0.05m;

        private const decimal CONFIDENCE_MIN_TEXT = 0.40m;
        private const decimal CONFIDENCE_MIN_CODE = 0.35m;
        private const decimal CONFIDENCE_MIN_AI = 0.50m;

        private const decimal CATEGORY_WEIGHT_TEXT = 0.50m;
        private const decimal CATEGORY_WEIGHT_CODE = 0.30m;
        private const decimal CATEGORY_WEIGHT_AI = 0.20m;

        private const decimal SPREAD_SANITY_MAX = 0.50m;   // if largest-smallest > 0.50 → suspect aggregation
        private const decimal RECENCY_PENALTY = 0.05m;     // very recent resubmission
        private const decimal PRIOR_FLAG_PENALTY = 0.07m;
        private const decimal PRIOR_CLEAR_DAMPEN = 0.05m;

        private const int RESUBMIT_WINDOW_DAYS = 5;

        #endregion

        #region Constructor
        public PlagiarismOrchestrationService(ILogger logger, IPlagiarismGateway gateway, IPlagiarismPolicyRepo policy)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Aggregates detectors and outputs a decision with reasons and required actions.
        /// </summary>
        public PlagiarismDecision Evaluate(PlagiarismContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            var decision = new PlagiarismDecision();

            if (!LanguageTokenFloorOk(ctx)) return Clean(decision, "Below language-specific token floor.");

            if (!DepartmentAllowsEngines(ctx.Department, ctx.AssignmentType))
                return Clean(decision, "Department policy excludes engines for this assignment type.");

            var rawScores = _gateway.RunAll(ctx);
            if (rawScores == null || rawScores.Count == 0)
                return Clean(decision, "No engine scores available.");

            var filtered = EnforceConfidenceFloors(rawScores);
            if (filtered.Count == 0) return Clean(decision, "All engines below confidence floor.");

            var normalized = NormalizeByLanguage(filtered, ctx.Language);

            var catAgg = AggregateByCategory(normalized);

            var baseScore = WeightedAggregate(catAgg);

            var adjusted1 = ApplyAllowances(baseScore, ctx);
            var adjusted2 = ApplyHistorical(adjusted1, ctx.HistoryField);
            var adjusted3 = ApplyRecency(adjusted2, ctx);
            var adjusted4 = ApplyRubricAlignment(adjusted3, ctx.RubricAlignmentField);

            var sanityOk = ConfidenceSpreadOk(normalized);
            var finalScore = sanityOk ? adjusted4 : Math.Min(1m, adjusted4 * 0.9m);

            var level = LevelFrom(finalScore);
            decision.Level = level.level;
            decision.AggregatedScore = finalScore;
            foreach (var item in level.reasons)
            { 
                decision.Reasons.Add(item);
            }

            var risk = RiskClassify(decision.Level, ctx.StudentStanding, ctx.AssignmentType);
            foreach (var item in ActionsFrom(risk))
            {
                decision.RequiredActions.Add(item);
            }
            

            _logger.Info($"Plagiarism: lvl={decision.Level} score={finalScore:0.00} risk={risk}");
            return decision;
        }
        #endregion

        #region Private Methods

        /// <summary>
        /// Gönderimin diline göre minimum token eşiğinin karşılanıp karşılanmadığını kontrol eder.
        /// Kod dili için daha düşük, doğal dil için genel bir minimum token değeri uygular.
        /// </summary>
        private bool LanguageTokenFloorOk(PlagiarismContext c)
        {
            if (c.Language.Equals(SubmissionLanguage.Code))
                return c.TokenCount >= MIN_TOKENS_CODE;
            return c.TokenCount >= MIN_TOKENS_GENERIC;
        }

        /// <summary>
        /// İlgili bölüm ve ödev tipine göre bölüm politikasının bu değerlendirmede motor kullanımına izin verip vermediğini döner.
        /// </summary>
        private bool DepartmentAllowsEngines(Department d, AssignmentType t)
        {
            var cfg = _policy.EngineAllowance(d, t);
            return cfg.AllowAnyEngine; // fine-grained filters are inside aggregation via confidence floors
        }


        #region Engine Processing
        /// <summary>
        /// Her motor için kategoriye özgü minimum güven eşiğini uygular ve bu eşiğin altında kalan skorları listeden çıkarır.
        /// </summary>
        private IList<EngineScore> EnforceConfidenceFloors(IList<EngineScore> scores)
        {
            var kept = new List<EngineScore>();
            foreach (var s in scores)
            {
                var min = s.Category == EngineCategory.Text ? CONFIDENCE_MIN_TEXT :
                          s.Category == EngineCategory.Code ? CONFIDENCE_MIN_CODE :
                          CONFIDENCE_MIN_AI;
                if (s.Confidence >= min) kept.Add(s);
            }
            return kept;
        }

        /// <summary>
        /// Gönderim diline göre motor skorlarını normalize eder; özellikle kod motorları için küçük düzeltmeler yapar.
        /// </summary>
        private IList<EngineScore> NormalizeByLanguage(IList<EngineScore> scores, SubmissionLanguage lang)
        {
            // Example: code engines over-score short snippets; soften a bit for code language
            var list = new List<EngineScore>(scores.Count);
            foreach (var s in scores)
            {
                decimal n = s.Score;
                if (lang == SubmissionLanguage.Code && s.Category == EngineCategory.Code)
                    n = Math.Max(0m, n - 0.03m);
                list.Add(new EngineScore { Score = n, Confidence = s.Confidence, Category = s.Category });
            }
            return list;
        }

        /// <summary>
        /// Motor skorlarını kategorilere (Text, Code, AI) göre ağırlıklı ortalama ile gruplayarak kategori bazlı toplam skorlar üretir.
        /// </summary>
        private CategoryAggregate AggregateByCategory(IList<EngineScore> scores)
        {
            decimal textSum = 0m, textW = 0m;
            decimal codeSum = 0m, codeW = 0m;
            decimal aiSum = 0m, aiW = 0m;

            foreach (var s in scores)
            {
                if (s.Category == EngineCategory.Text)
                {
                    textSum += s.Score * s.Confidence; textW += s.Confidence;
                }
                else if (s.Category == EngineCategory.Code)
                {
                    codeSum += s.Score * s.Confidence; codeW += s.Confidence;
                }
                else
                {
                    aiSum += s.Score * s.Confidence; aiW += s.Confidence;
                }
            }

            return new CategoryAggregate
            {
                Text = textW == 0m ? 0m : textSum / textW,
                Code = codeW == 0m ? 0m : codeSum / codeW,
                AI = aiW == 0m ? 0m : aiSum / aiW
            };
        }

        /// <summary>
        /// Metin, kod ve yapay zekâ kategorileri için tanımlanmış kategori ağırlıklarına göre tek bir temel toplu skor hesaplar.
        /// </summary>
        private decimal WeightedAggregate(CategoryAggregate agg)
        {
            return (agg.Text * CATEGORY_WEIGHT_TEXT)
                 + (agg.Code * CATEGORY_WEIGHT_CODE)
                 + (agg.AI * CATEGORY_WEIGHT_AI);
        }

        /// <summary>
        /// Motor skorları arasındaki farkın belirlenen maksimum yayılım eşiğini aşıp aşmadığını kontrol ederek skora güvenilirlik testi uygular.
        /// </summary>
        private bool ConfidenceSpreadOk(IList<EngineScore> scores)
        {
            if (scores.Count == 0) return true;
            var max = scores.Max(s => s.Score);
            var min = scores.Min(s => s.Score);
            return (max - min) <= SPREAD_SANITY_MAX;
        }

        #endregion

        #region Dampening / Penalties
        /// <summary>
        /// Self-plagiarism, grup çalışması toleransı, kaynakça oranı ve onaylı şablon kullanımı gibi faktörleri dikkate alarak temel skoru düşüren veya sınırlayan izin/dampening katmanını uygular.
        /// </summary>
        private decimal ApplyAllowances(decimal baseScore, PlagiarismContext c)
        {
            var score = baseScore;

            if (c.SelfOverlapRatio > 0m)
                score -= Math.Min(c.SelfOverlapRatio, SELF_ALLOW_RATIO);

            if (c.IsGroupAssignment && c.GroupOverlapRatio > 0m)
                score -= Math.Min(c.GroupOverlapRatio, GROUP_ALLOW_RATIO);

            if (c.CitationToContentRatio > 0m)
                score -= Math.Min(c.CitationToContentRatio, CITATION_DAMPENING);

            if (c.UsesApprovedTemplate)
                score -= TEMPLATE_DAMPENING;

            return Clamp01(score);
        }

        /// <summary>
        /// Öğrencinin daha önceki intihal geçmişini kullanarak skora küçük ceza veya indirim uygular; tekrar eden vakalarda ceza artar, temiz denetimlerde skor hafifletilir.
        /// </summary>
        private decimal ApplyHistorical(decimal score, PlagiarismHistory h)
        {
            if (h == null) return score;

            if (h.PriorFlags >= 2) score += PRIOR_FLAG_PENALTY * 2;
            else if (h.PriorFlags == 1) score += PRIOR_FLAG_PENALTY;

            if (h.PriorCleanAudits >= 2) score -= PRIOR_CLEAR_DAMPEN * 2;
            else if (h.PriorCleanAudits == 1) score -= PRIOR_CLEAR_DAMPEN;

            return Clamp01(score);
        }

        /// <summary>
        /// Aynı çalışmanın önceki versiyonuna çok kısa süre içinde yeniden gönderilmesi durumunda skora ek bir recency cezası uygular.
        /// </summary>
        private decimal ApplyRecency(decimal score, PlagiarismContext c)
        {
            if (c.ResubmittedUtc.HasValue && c.SubmittedUtc.HasValue)
            {
                var days = (c.SubmittedUtc.Value - c.ResubmittedUtc.Value).TotalDays;
                if (days <= RESUBMIT_WINDOW_DAYS) score += RECENCY_PENALTY;
            }
            return Clamp01(score);
        }

        /// <summary>
        /// Rubrik uyumuna göre (güçlü/zayıf) skoru küçük bir bonus veya ceza ile günceller; sonuç skoru 0..1 aralığında sınırlar.
        /// </summary>
        private decimal ApplyRubricAlignment(decimal score, RubricAlignment a)
        {
            if (a == RubricAlignment.Strong) score -= RUBRIC_ALIGNMENT_BONUS;
            else if (a == RubricAlignment.Weak) score += RUBRIC_ALIGNMENT_BONUS;
            return Clamp01(score);
        }

        #endregion

        #region Decision & Actions
        /// <summary>
        /// Nihai ayarlanmış skora göre intihal seviyesini (Temiz, İnceleme, Bayrak) belirler ve nedenleri açıklayan kısa mesajlar üretir.
        /// </summary>
        private (PlagiarismLevel level, IList<string> reasons) LevelFrom(decimal adjusted)
        {
            var reasons = new List<string>();
            if (adjusted >= TIER_FLAG)
            {
                reasons.Add("Exceeds flag threshold.");
                return (PlagiarismLevel.Flag, reasons);
            }
            if (adjusted >= TIER_REVIEW)
            {
                reasons.Add("Exceeds review threshold.");
                return (PlagiarismLevel.Review, reasons);
            }
            reasons.Add("Below review threshold.");
            return (PlagiarismLevel.Clean, reasons);
        }

        /// <summary>
        /// Intihal seviyesi, öğrencinin akademik durumu ve ödev tipini birlikte değerlendirerek risk sınıfını (yok, düşük, orta, yüksek) hesaplar.
        /// </summary>
        private RiskClass RiskClassify(PlagiarismLevel lvl, StudentStanding standing, AssignmentType type)
        {
            if (lvl == PlagiarismLevel.Flag && standing != StudentStanding.Good) return RiskClass.High;
            if (lvl == PlagiarismLevel.Flag) return RiskClass.Medium;
            if (lvl == PlagiarismLevel.Review && type == AssignmentType.FinalProject) return RiskClass.Medium;
            if (lvl == PlagiarismLevel.Review) return RiskClass.Low;
            return RiskClass.None;
        }

        /// <summary>
        /// Belirlenen risk sınıfına karşılık gelen gerekli aksiyonları (manuel inceleme, komiteye eskalasyon vb.) üretir.
        /// </summary>
        private IEnumerable<PlagiarismAction> ActionsFrom(RiskClass r)
        {
            if (r == RiskClass.High) return new[] { PlagiarismAction.EscalateToCommittee };
            if (r == RiskClass.Medium) return new[] { PlagiarismAction.ManualReview };
            if (r == RiskClass.Low) return new[] { PlagiarismAction.ManualReview };
            return Array.Empty<PlagiarismAction>();
        }

        /// <summary>
        /// Hiçbir motor verisinin kullanılmadığı veya politikalar nedeniyle değerlendirme yapılmadığı durumlarda,
        /// kararı "Temiz" olarak ayarlar, skoru sıfırlar ve açıklama nedeni ekler.
        /// </summary>
        private PlagiarismDecision Clean(PlagiarismDecision d, string reason)
        {
            d.Level = PlagiarismLevel.Clean;
            d.AggregatedScore = 0m;
            d.Reasons.Add(reason);
            return d;
        }

        /// <summary>
        /// Verilen ondalık değeri 0 ile 1 arasına sıkıştırarak sınırlar; alt sınırın altında ise 0, üstünde ise 1 döner.
        /// </summary>
        private static decimal Clamp01(decimal v) => v < 0m ? 0m : (v > 1m ? 1m : v);
        #endregion
        #endregion
    }
   

  


     
}
