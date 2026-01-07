using FluentResults;
using MediatR;
using Neo4j.Driver;
using Recommendation.Api.Common.Contracts;
using Recommendation.Api.Features.Products.Contracts;
using Recommendation.Api.Infrastructure.Neo4j;

namespace Recommendation.Api.Features.Products.GetRelatedProducts;

public class GetRelatedProductsHandler(INeo4jConnectionFactory connectionFactory) 
    : IRequestHandler<GetRelatedProductsQuery, Result<PaginatedResult<ProductDto>>>
{
    public async Task<Result<PaginatedResult<ProductDto>>> Handle(GetRelatedProductsQuery request, CancellationToken cancellationToken)
    {
        await using var session = connectionFactory.CreateSession();

        var skip = (request.Page - 1) * request.PageSize;

        // Category-based related products 
        const string countQuery = """
            MATCH (p:Product {productId: $productId})-[:BELONGS_TO]->(c:Category)<-[:BELONGS_TO]-(related:Product)
            WHERE related.productId <> $productId
            RETURN count(DISTINCT related) AS total
            """;

        const string query = """
            MATCH (p:Product {productId: $productId})-[:BELONGS_TO]->(c:Category)<-[:BELONGS_TO]-(related:Product)
            WHERE related.productId <> $productId
            OPTIONAL MATCH (related)-[:MADE_BY]->(b:Brand)
            RETURN DISTINCT related.productId AS productId, related.productName AS productName, 
                   related.productDescription AS productDescription, b.brandId AS brandId, b.name AS brandName,
                   related.categoryId AS categoryId
            ORDER BY related.productName
            SKIP $skip LIMIT $take
            """;

        var (products, totalCount) = await session.ExecuteReadAsync(async tx =>
        {
            var countCursor = await tx.RunAsync(countQuery, new { productId = request.ProductId });
            var countRecord = await countCursor.SingleAsync(cancellationToken);
            var total = countRecord["total"].As<int>();

            var cursor = await tx.RunAsync(query, new { productId = request.ProductId, skip, take = request.PageSize });
            var records = await cursor.ToListAsync(cancellationToken);

            var items = records.Select(record => new ProductDto(
                record["productId"].As<int>(),
                record["productName"].As<string>(),
                record["productDescription"].As<string?>(),
                record["brandId"].As<int?>(),
                record["brandName"].As<string?>(),
                record["categoryId"].As<int?>())).ToList();

            return (items, total);
        });

        return Result.Ok(PaginatedResult.Create(products, totalCount, request.Page, request.PageSize));
    }
}
