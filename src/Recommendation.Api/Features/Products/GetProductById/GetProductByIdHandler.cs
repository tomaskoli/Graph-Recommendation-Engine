using FluentResults;
using MediatR;
using Neo4j.Driver;
using Recommendation.Api.Common.Errors;
using Recommendation.Api.Features.Products.Contracts;
using Recommendation.Api.Infrastructure.Neo4j;

namespace Recommendation.Api.Features.Products.GetProductById;

public class GetProductByIdHandler(INeo4jConnectionFactory connectionFactory) : IRequestHandler<GetProductByIdQuery, Result<ProductDetailDto>>
{
    public async Task<Result<ProductDetailDto>> Handle(GetProductByIdQuery request, CancellationToken cancellationToken)
    {
        await using var session = connectionFactory.CreateSession();

        const string query = """
            MATCH (p:Product {productId: $productId})
            OPTIONAL MATCH (p)-[:MADE_BY]->(b:Brand)
            OPTIONAL MATCH (p)-[:HAS_PARAMETER]->(param:Parameter)
            WITH p, b, collect({
                parameterId: param.parameterId,
                parameterName: param.parameterName,
                value: param.value
            }) AS parameters
            RETURN p.productId AS productId, p.productName AS productName, 
                   p.productDescription AS productDescription, b.brandId AS brandId, b.name AS brandName,
                   p.categoryId AS categoryId, parameters
            """;

        var result = await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync(query, new { productId = request.ProductId });
            var records = await cursor.ToListAsync(cancellationToken);
            return records.FirstOrDefault();
        });

        if (result is null)
        {
            return Result.Fail(new NotFoundError("Product", request.ProductId));
        }

        var parametersData = result["parameters"].As<List<IDictionary<string, object>>>();
        var parameters = parametersData
            .Where(p => p["parameterId"] != null)
            .Select(p => new ParameterDto(
                p["parameterId"].As<int>(),
                p["parameterName"].As<string>(),
                p["value"].As<string>()))
            .OrderBy(p => p.ParameterName)
            .ToList();

        var product = new ProductDetailDto(
            result["productId"].As<int>(),
            result["productName"].As<string>(),
            result["productDescription"].As<string?>(),
            result["brandId"].As<int?>(),
            result["brandName"].As<string?>(),
            result["categoryId"].As<int?>(),
            parameters);

        return Result.Ok(product);
    }
}
