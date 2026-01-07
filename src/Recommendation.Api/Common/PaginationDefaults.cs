namespace Recommendation.Api.Common;

public static class PaginationDefaults
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public static (int Page, int PageSize) Normalize(int page, int pageSize, int? defaultPageSize = null)
    {
        var effectivePage = page > 0 ? page : DefaultPage;
        var effectivePageSize = pageSize > 0 
            ? Math.Min(pageSize, MaxPageSize) 
            : (defaultPageSize ?? DefaultPageSize);

        return (effectivePage, effectivePageSize);
    }
}

