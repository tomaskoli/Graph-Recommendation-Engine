namespace Recommendation.Api.Features.Products.Contracts;

public record ProductDetailDto(
    int ProductId,
    string ProductName,
    string? ProductDescription,
    int? BrandId,
    string? BrandName,
    int? CategoryId,
    IReadOnlyList<ParameterDto> Parameters);

