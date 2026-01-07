using FluentResults;
using MediatR;
using Neo4j.Driver;
using Recommendation.Api.Common.Constants;
using Recommendation.Api.Common.Contracts;
using Recommendation.Api.Features.Products.Contracts;
using Recommendation.Api.Features.Recommendations.Contracts;
using Recommendation.Api.Infrastructure.Neo4j;

namespace Recommendation.Api.Features.Recommendations.GetRecommendations;

public class GetRecommendationsHandler(INeo4jConnectionFactory connectionFactory)
    : IRequestHandler<GetRecommendationsQuery, Result<RecommendationDto>>
{
    public async Task<Result<RecommendationDto>> Handle(GetRecommendationsQuery request, CancellationToken cancellationToken)
    {
        await using var session = connectionFactory.CreateSession();

        var similarProducts = await GetSimilarProductsAsync(session, request.ProductId, request.Page, request.PageSize, cancellationToken);

        return Result.Ok(new RecommendationDto(similarProducts));
    }

    private static async Task<PaginatedResult<ScoredProductDto>> GetSimilarProductsAsync(
        IAsyncSession session, int productId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var skip = (page - 1) * pageSize;

        // Recommendations based on precomputed similarity scores from GDS (FastRP + kNN)
        const string countQuery = """
            MATCH (p:Product {productId: $productId})-[r:SIMILAR_TO]->(similar:Product)
            WHERE r.score >= $minScore
            RETURN count(similar) AS total
            """;

        const string query = """
            MATCH (p:Product {productId: $productId})-[r:SIMILAR_TO]->(similar:Product)
            WHERE r.score >= $minScore
            OPTIONAL MATCH (similar)-[:MADE_BY]->(b:Brand)
            RETURN similar.productId AS productId, 
                   similar.productName AS productName,
                   similar.productDescription AS productDescription, 
                   b.brandId AS brandId, 
                   b.name AS brandName,
                   r.score AS score,
                   coalesce(r.sameBrand, false) AS sameBrand
            ORDER BY r.score DESC
            SKIP $skip LIMIT $take
            """;

        return await session.ExecuteReadAsync(async tx =>
        {
            var queryParams = new { productId, skip, take = pageSize, minScore = RecommendationConstants.MinSimilarityScore };
            
            var countCursor = await tx.RunAsync(countQuery, queryParams);
            var countRecord = await countCursor.SingleAsync(cancellationToken);
            var total = countRecord["total"].As<int>();

            var cursor = await tx.RunAsync(query, queryParams);
            var records = await cursor.ToListAsync(cancellationToken);

            var items = records.Select(record => new ScoredProductDto(
                record["productId"].As<int>(),
                record["productName"].As<string>(),
                record["productDescription"].As<string?>(),
                record["brandId"].As<int?>(),
                record["brandName"].As<string?>(),
                record["score"].As<double>(),
                record["sameBrand"].As<bool>())).ToList();

            return PaginatedResult.Create(items, total, page, pageSize);
        });
    }
}
