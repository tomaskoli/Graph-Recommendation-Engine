using Recommendation.Api.Common.Contracts;

namespace Recommendation.Api.Tests.Common;

public class PaginatedResultTests
{
    [Fact]
    public void Create_WithItems_CalculatesTotalPagesCorrectly()
    {
        var items = new List<string> { "a", "b", "c" };

        var result = PaginatedResult.Create(items, totalCount: 25, page: 1, pageSize: 10);

        Assert.Equal(3, result.TotalPages);
        Assert.True(result.HasMore);
    }

    [Fact]
    public void Create_OnLastPage_HasMoreIsFalse()
    {
        var items = new List<string> { "a" };

        var result = PaginatedResult.Create(items, totalCount: 21, page: 3, pageSize: 10);

        Assert.Equal(3, result.TotalPages);
        Assert.False(result.HasMore);
    }

    [Fact]
    public void Create_WithEmptyItems_ReturnsEmptyResult()
    {
        var items = new List<string>();

        var result = PaginatedResult.Create(items, totalCount: 0, page: 1, pageSize: 10);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
        Assert.False(result.HasMore);
    }

    [Fact]
    public void Create_WithSinglePage_HasMoreIsFalse()
    {
        var items = new List<string> { "a", "b" };

        var result = PaginatedResult.Create(items, totalCount: 2, page: 1, pageSize: 10);

        Assert.Equal(1, result.TotalPages);
        Assert.False(result.HasMore);
    }

    [Fact]
    public void Create_ExactlyFillsPage_CalculatesCorrectly()
    {
        var items = Enumerable.Range(1, 10).Select(i => i.ToString()).ToList();

        var result = PaginatedResult.Create(items, totalCount: 20, page: 1, pageSize: 10);

        Assert.Equal(2, result.TotalPages);
        Assert.True(result.HasMore);
    }
}

