using MediatR;
using Microsoft.AspNetCore.Mvc;
using Recommendation.Api.Common;
using Recommendation.Api.Common.Contracts;
using Recommendation.Api.Common.Extensions;
using Recommendation.Api.Features.Products.Contracts;

namespace Recommendation.Api.Features.Products.GetRelatedProducts;

public static class GetRelatedProductsEndpoint
{
    public static void MapGetRelatedProductsEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/products/{id:int}/related", async (
            int id,
            [FromQuery] int page,
            [FromQuery] int pageSize,
            [FromServices] IMediator mediator) =>
        {
            var (effectivePage, effectivePageSize) = PaginationDefaults.Normalize(page, pageSize);

            var result = await mediator.Send(new GetRelatedProductsQuery(id, effectivePage, effectivePageSize));

            return result.ToHttpResult();
        })
        .WithName("GetRelatedProducts")
        .WithTags("Products")
        .Produces<PaginatedResult<ProductDto>>();
    }
}
