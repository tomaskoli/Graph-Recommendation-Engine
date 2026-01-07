using FluentResults;
using MediatR;
using Neo4j.Driver;
using Recommendation.Api.Common.Contracts;
using Recommendation.Api.Features.Products.Contracts;
using Recommendation.Api.Infrastructure.Neo4j;

namespace Recommendation.Api.Features.Categories.GetProductsByCategory;

public class GetProductsByCategoryHandler(INeo4jConnectionFactory connectionFactory)
    : IRequestHandler<GetProductsByCategoryQuery, Result<PaginatedResult<ProductDto>>>
{
    public async Task<Result<PaginatedResult<ProductDto>>> Handle(GetProductsByCategoryQuery request, CancellationToken cancellationToken)
    {
        await using var session = connectionFactory.CreateSession();

        var skip = (request.Page - 1) * request.PageSize;

        // Product listing for category 
        const string countQuery = """
            MATCH (p:Product)-[:BELONGS_TO]->(c:Category {categoryId: $categoryId})
            RETURN count(p) AS total
            """;

        const string query = """
            MATCH (p:Product)-[:BELONGS_TO]->(c:Category {categoryId: $categoryId})
            OPTIONAL MATCH (p)-[:MADE_BY]->(b:Brand)
            RETURN p.productId AS productId, p.productName AS productName, 
                   p.productDescription AS productDescription, b.brandId AS brandId, b.name AS brandName,
                   p.categoryId AS categoryId
            ORDER BY p.productName
            SKIP $skip LIMIT $take
            """;

        var (products, totalCount) = await session.ExecuteReadAsync(async tx =>
        {
            var countCursor = await tx.RunAsync(countQuery, new { categoryId = request.CategoryId });
            var countRecord = await countCursor.SingleAsync(cancellationToken);
            var total = countRecord["total"].As<int>();

            var cursor = await tx.RunAsync(query, new { categoryId = request.CategoryId, skip, take = request.PageSize });
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
