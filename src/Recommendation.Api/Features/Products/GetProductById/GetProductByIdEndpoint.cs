using MediatR;
using Microsoft.AspNetCore.Mvc;
using Recommendation.Api.Common.Extensions;

namespace Recommendation.Api.Features.Products.GetProductById;

public static class GetProductByIdEndpoint
{
    public static void MapGetProductByIdEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/products/{id:int}", async (int id, [FromServices] IMediator mediator) =>
        {
            var result = await mediator.Send(new GetProductByIdQuery(id));
            return result.ToHttpResult();
        })
        .WithName("GetProductById")
        .WithTags("Products")
        .Produces<Contracts.ProductDetailDto>()
        .Produces(StatusCodes.Status404NotFound);
    }
}

