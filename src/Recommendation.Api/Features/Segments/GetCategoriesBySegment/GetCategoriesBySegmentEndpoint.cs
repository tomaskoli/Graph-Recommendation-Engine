using MediatR;
using Microsoft.AspNetCore.Mvc;
using Recommendation.Api.Common;
using Recommendation.Api.Common.Contracts;
using Recommendation.Api.Common.Extensions;
using Recommendation.Api.Features.Categories.Contracts;

namespace Recommendation.Api.Features.Segments.GetCategoriesBySegment;

public static class GetCategoriesBySegmentEndpoint
{
    public static void MapGetCategoriesBySegmentEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/segments/{id:int}/categories", async (
            int id,
            [FromQuery] int page,
            [FromQuery] int pageSize,
            [FromServices] IMediator mediator) =>
        {
            var (effectivePage, effectivePageSize) = PaginationDefaults.Normalize(page, pageSize);

            var result = await mediator.Send(new GetCategoriesBySegmentQuery(id, effectivePage, effectivePageSize));

            return result.ToHttpResult();
        })
        .WithName("GetCategoriesBySegment")
        .WithTags("Segments")
        .Produces<PaginatedResult<CategoryDto>>();
    }
}
