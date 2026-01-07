using MediatR;
using Microsoft.AspNetCore.Mvc;
using Recommendation.Api.Common.Extensions;
using Recommendation.Api.Features.Search.Contracts;

namespace Recommendation.Api.Features.Search.GlobalSearch;

public static class GlobalSearchEndpoint
{
    public static void MapGlobalSearchEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/search", async (
            [FromQuery] string q,
            [FromQuery] int limit,
            [FromServices] IMediator mediator) =>
        {
            var effectiveLimit = limit > 0 ? limit : 5;
            var result = await mediator.Send(new GlobalSearchQuery(q, effectiveLimit));
            return result.ToHttpResult();
        })
        .WithName("GlobalSearch")
        .WithTags("Search")
        .Produces<SearchResultDto>()
        .Produces(StatusCodes.Status400BadRequest);
    }
}

