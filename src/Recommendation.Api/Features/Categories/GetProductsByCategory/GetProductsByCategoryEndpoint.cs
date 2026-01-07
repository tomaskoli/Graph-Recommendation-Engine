using MediatR;
using Microsoft.AspNetCore.Mvc;
using Recommendation.Api.Common;
using Recommendation.Api.Common.Contracts;
using Recommendation.Api.Common.Extensions;
using Recommendation.Api.Features.Products.Contracts;

namespace Recommendation.Api.Features.Categories.GetProductsByCategory;

public static class GetProductsByCategoryEndpoint
{
    public static void MapGetProductsByCategoryEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/categories/{id:int}/products", async (
            int id,
            [FromQuery] int page,
            [FromQuery] int pageSize,
            [FromServices] IMediator mediator) =>
        {
            var (effectivePage, effectivePageSize) = PaginationDefaults.Normalize(page, pageSize);
            
            var result = await mediator.Send(new GetProductsByCategoryQuery(id, effectivePage, effectivePageSize));

            return result.ToHttpResult();
        })
        .WithName("GetProductsByCategory")
        .WithTags("Categories")
        .Produces<PaginatedResult<ProductDto>>();
    }
}
