namespace Recommendation.Api.Features.Products.Contracts;

public record ScoredProductDto(
    int ProductId,
    string ProductName,
    string? ProductDescription,
    int? BrandId,
    string? BrandName,
    double SimilarityScore,
    bool SameBrand);

