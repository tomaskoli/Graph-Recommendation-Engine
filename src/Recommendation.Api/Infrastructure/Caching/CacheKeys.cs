namespace Recommendation.Api.Infrastructure.Caching;

public static class CacheKeys
{
    private const string RecommendationsPrefix = "recs";

    public static string Recommendations(int productId, int page, int pageSize)
        => $"{RecommendationsPrefix}:{productId}:{page}:{pageSize}";

    public static string RecommendationsPattern(int productId)
        => $"{RecommendationsPrefix}:{productId}:*";

    public static string AllRecommendationsPattern()
        => $"{RecommendationsPrefix}:*";
}

