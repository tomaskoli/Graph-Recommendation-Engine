namespace Recommendation.Api.Features.Products.Contracts;

public record ProductDto(
    int ProductId,
    string ProductName,
    string? ProductDescription,
    int? BrandId,
    string? BrandName,
    int? CategoryId);

