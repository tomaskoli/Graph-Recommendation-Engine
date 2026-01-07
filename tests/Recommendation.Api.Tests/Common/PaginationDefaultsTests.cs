using Recommendation.Api.Common;

namespace Recommendation.Api.Tests.Common;

public class PaginationDefaultsTests
{
    [Theory]
    [InlineData(0, 10, 1, 10)]
    [InlineData(-1, 10, 1, 10)]
    [InlineData(5, 10, 5, 10)]
    public void Normalize_Page_ReturnsExpectedValue(int inputPage, int inputPageSize, int expectedPage, int expectedPageSize)
    {
        var (page, pageSize) = PaginationDefaults.Normalize(inputPage, inputPageSize);

        Assert.Equal(expectedPage, page);
        Assert.Equal(expectedPageSize, pageSize);
    }

    [Fact]
    public void Normalize_PageSizeExceedsMax_CapsAtMaxPageSize()
    {
        var (_, pageSize) = PaginationDefaults.Normalize(1, 200);

        Assert.Equal(PaginationDefaults.MaxPageSize, pageSize);
    }

    [Fact]
    public void Normalize_InvalidPageSize_UsesDefault()
    {
        var (_, pageSize) = PaginationDefaults.Normalize(1, 0);

        Assert.Equal(PaginationDefaults.DefaultPageSize, pageSize);
    }

    [Fact]
    public void Normalize_InvalidPageSizeWithCustomDefault_UsesCustomDefault()
    {
        var (_, pageSize) = PaginationDefaults.Normalize(1, 0, defaultPageSize: 15);

        Assert.Equal(15, pageSize);
    }

    [Fact]
    public void Normalize_NegativePageSize_UsesDefault()
    {
        var (_, pageSize) = PaginationDefaults.Normalize(1, -5);

        Assert.Equal(PaginationDefaults.DefaultPageSize, pageSize);
    }
}

