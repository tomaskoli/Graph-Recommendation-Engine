using Recommendation.Api.Infrastructure.Caching;

namespace Recommendation.Api.Tests.Infrastructure.Caching;

public class CacheKeysTests
{
    [Theory]
    [InlineData(123, 1, 10, "recs:123:1:10")]
    [InlineData(456, 2, 20, "recs:456:2:20")]
    [InlineData(1, 100, 50, "recs:1:100:50")]
    public void Recommendations_GeneratesCorrectKey(int productId, int page, int pageSize, string expected)
    {
        var key = CacheKeys.Recommendations(productId, page, pageSize);

        Assert.Equal(expected, key);
    }

    [Theory]
    [InlineData(123, "recs:123:*")]
    [InlineData(456, "recs:456:*")]
    public void RecommendationsPattern_GeneratesCorrectPattern(int productId, string expected)
    {
        var pattern = CacheKeys.RecommendationsPattern(productId);

        Assert.Equal(expected, pattern);
    }

    [Fact]
    public void AllRecommendationsPattern_GeneratesCorrectPattern()
    {
        var pattern = CacheKeys.AllRecommendationsPattern();

        Assert.Equal("recs:*", pattern);
    }
}

