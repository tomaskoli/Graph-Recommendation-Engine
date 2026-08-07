namespace Recommendation.Api.Common.Constants;

public static class RecommendationConstants
{
    public const double MinSimilarityScore = 0.8;

    // Hybrid strategy weights (doc/SPARK-PIPELINE.md Stage 4) — nobody tunes these, so no config knob.
    public const double ContentWeight = 0.5;
    public const double BehavioralWeight = 0.5;
}

