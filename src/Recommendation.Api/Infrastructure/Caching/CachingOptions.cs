namespace Recommendation.Api.Infrastructure.Caching;

public class CachingOptions
{
    public const string SectionName = "Caching";

    public bool Enabled { get; set; } = true;
    public int RecommendationsTtlMinutes { get; set; } = 30;
}

