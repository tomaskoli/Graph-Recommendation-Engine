using MediatR;
using Microsoft.AspNetCore.Mvc;
using Recommendation.Api.Common;
using Recommendation.Api.Common.Contracts;
using Recommendation.Api.Common.Extensions;
using Recommendation.Api.Features.Segments.Contracts;

namespace Recommendation.Api.Features.Segments.GetAllSegments;

public static class GetAllSegmentsEndpoint
{
    public static void MapGetAllSegmentsEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/segments", async (
            [FromQuery] int page,
            [FromQuery] int pageSize,
            [FromServices] IMediator mediator) =>
        {
            var (effectivePage, effectivePageSize) = PaginationDefaults.Normalize(page, pageSize);

            var result = await mediator.Send(new GetAllSegmentsQuery(effectivePage, effectivePageSize));

            return result.ToHttpResult();
        })
        .WithName("GetAllSegments")
        .WithTags("Segments")
        .Produces<PaginatedResult<CatalogSegmentDto>>();
    }
}
