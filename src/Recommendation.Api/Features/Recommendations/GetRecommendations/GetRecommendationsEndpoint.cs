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
            [FromQuery] string? strategy,
            [FromServices] IMediator mediator) =>
        {
            var effectiveStrategy = RecommendationStrategy.Hybrid;
            if (!string.IsNullOrEmpty(strategy) && !Enum.TryParse(strategy, ignoreCase: true, out effectiveStrategy))
            {
                return Results.BadRequest($"Invalid strategy '{strategy}'. Must be one of: content, behavioral, hybrid.");
            }

            var (effectivePage, effectivePageSize) = PaginationDefaults.Normalize(page, pageSize);

            var result = await mediator.Send(
                new GetRecommendationsQuery(productId, effectivePage, effectivePageSize, effectiveStrategy));

            return result.ToHttpResult();
        })
        .WithName("GetRecommendations")
        .WithTags("Recommendations")
        .Produces<RecommendationDto>()
        .Produces(StatusCodes.Status400BadRequest);
    }
}
