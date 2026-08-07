using Recommendation.Api.Features.Recommendations.Contracts;

namespace Recommendation.Api.Infrastructure.Caching;

public static class CacheKeys
{
    private const string RecommendationsPrefix = "recs";

    public static string Recommendations(int productId, RecommendationStrategy strategy, int page, int pageSize)
        => $"{RecommendationsPrefix}:{productId}:{strategy.ToString().ToLowerInvariant()}:{page}:{pageSize}";

    public static string RecommendationsPattern(int productId)
        => $"{RecommendationsPrefix}:{productId}:*";

    public static string AllRecommendationsPattern()
        => $"{RecommendationsPrefix}:*";
}

