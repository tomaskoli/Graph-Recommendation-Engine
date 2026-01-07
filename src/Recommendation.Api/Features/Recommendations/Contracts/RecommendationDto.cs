using Recommendation.Api.Common.Contracts;
using Recommendation.Api.Features.Products.Contracts;

namespace Recommendation.Api.Features.Recommendations.Contracts;

public record RecommendationDto(PaginatedResult<ScoredProductDto> SimilarProducts);
