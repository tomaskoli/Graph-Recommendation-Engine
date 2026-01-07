using MediatR;
using Microsoft.AspNetCore.Mvc;
using Recommendation.Api.Common;
using Recommendation.Api.Common.Extensions;
using Recommendation.Api.Features.Recommendations.Contracts;

namespace Recommendation.Api.Features.Recommendations.GetRecommendations;

public static class GetRecommendationsEndpoint
{
    public static void MapGetRecommendationsEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/recommendations", async (
            [FromQuery] int productId,
            [FromQuery] int page,
            [FromQuery] int pageSize,
            [FromServices] IMediator mediator) =>
        {
            var (effectivePage, effectivePageSize) = PaginationDefaults.Normalize(page, pageSize);

            var result = await mediator.Send(new GetRecommendationsQuery(productId, effectivePage, effectivePageSize));

            return result.ToHttpResult();
        })
        .WithName("GetRecommendations")
        .WithTags("Recommendations")
        .Produces<RecommendationDto>();
    }
}
