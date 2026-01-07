namespace Recommendation.Api.Features.Search.Contracts;

public record SearchResultDto(
    IReadOnlyList<SearchProductResultDto> Products,
    IReadOnlyList<SearchCategoryResultDto> Categories,
    IReadOnlyList<SearchBrandResultDto> Brands);

public record SearchProductResultDto(
    int ProductId,
    string ProductName,
    string? BrandName);

public record SearchCategoryResultDto(
    int CategoryId,
    string CategoryName,
    string? ParentCategoryName);

public record SearchBrandResultDto(
    int BrandId,
    string BrandName,
    int ProductCount);

