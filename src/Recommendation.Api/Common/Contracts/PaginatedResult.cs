namespace Recommendation.Api.Common.Contracts;

public record PaginatedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages,
    bool HasMore);

public static class PaginatedResult
{
    public static PaginatedResult<T> Create<T>(IReadOnlyList<T> items, int totalCount, int page, int pageSize)
    {
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        return new PaginatedResult<T>(
            items,
            totalCount,
            page,
            pageSize,
            totalPages,
            page < totalPages);
    }
}
