using MediatR;
using Microsoft.AspNetCore.Mvc;
using Recommendation.Api.Common;
using Recommendation.Api.Common.Contracts;
using Recommendation.Api.Common.Extensions;
using Recommendation.Api.Features.Categories.Contracts;

namespace Recommendation.Api.Features.Categories.GetCategoryHierarchy;

public static class GetCategoryHierarchyEndpoint
{
    public static void MapGetCategoryHierarchyEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/categories", async (
            [FromQuery] int page,
            [FromQuery] int pageSize,
            [FromServices] IMediator mediator) =>
        {
            var (effectivePage, effectivePageSize) = PaginationDefaults.Normalize(page, pageSize);

            var result = await mediator.Send(new GetCategoryHierarchyQuery(null, effectivePage, effectivePageSize));

            return result.ToHttpResult();
        })
        .WithName("GetCategoryHierarchy")
        .WithTags("Categories")
        .Produces<PaginatedResult<CategoryDto>>();

        app.MapGet("/api/categories/{id:int}", async (
            int id,
            [FromServices] IMediator mediator) =>
        {
            var result = await mediator.Send(new GetCategoryHierarchyQuery(id, 1, PaginationDefaults.MaxPageSize));

            return result.ToHttpResult();
        })
        .WithName("GetCategoryById")
        .WithTags("Categories")
        .Produces<PaginatedResult<CategoryDto>>();
    }
}
