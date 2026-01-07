namespace Recommendation.Api.Features.Categories.Contracts;

public record CategoryDto(
    int CategoryId,
    string CategoryName,
    int? ParentCategoryId,
    int? CatalogSegmentId,
    string? CatalogSegmentName,
    IReadOnlyList<CategoryDto>? SubCategories = null);
