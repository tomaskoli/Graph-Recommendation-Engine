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

        var similarProducts = await GetSimilarProductsAsync(
            session, request.ProductId, request.Strategy, request.Page, request.PageSize, cancellationToken);

        return Result.Ok(new RecommendationDto(similarProducts));
    }

    private static async Task<PaginatedResult<ScoredProductDto>> GetSimilarProductsAsync(
        IAsyncSession session, int productId, RecommendationStrategy strategy, int page, int pageSize, CancellationToken cancellationToken)
    {
        var skip = (page - 1) * pageSize;
        var (countQuery, query) = QueriesFor(strategy);

        return await session.ExecuteReadAsync(async tx =>
        {
            var queryParams = new
            {
                productId,
                skip,
                take = pageSize,
                minScore = RecommendationConstants.MinSimilarityScore,
                wContent = RecommendationConstants.ContentWeight,
                wBehavior = RecommendationConstants.BehavioralWeight,
            };

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

    private static (string CountQuery, string Query) QueriesFor(RecommendationStrategy strategy) => strategy switch
    {
        RecommendationStrategy.Content => (ContentCountQuery, ContentQuery),
        RecommendationStrategy.Behavioral => (BehavioralCountQuery, BehavioralQuery),
        RecommendationStrategy.Hybrid => (HybridCountQuery, HybridQuery),
        _ => throw new ArgumentOutOfRangeException(nameof(strategy), strategy, null),
    };

    // Precomputed similarity from GDS (FastRP + kNN) — sameBrand is already on the edge.
    private const string ContentCountQuery = """
        MATCH (p:Product {productId: $productId})-[r:SIMILAR_TO]->(similar:Product)
        WHERE r.score >= $minScore
        RETURN count(similar) AS total
        """;

    private const string ContentQuery = """
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

    // Co-purchase lift from the Spark pipeline (doc/SPARK-PIPELINE.md) — lift > 1.0 means the pair
    // co-occurs more than chance, so it's the floor for "behavioral" rather than a tunable constant.
    // ALSO_BOUGHT carries no sameBrand property, so it's derived here instead of read off the edge.
    private const string BehavioralCountQuery = """
        MATCH (p:Product {productId: $productId})-[r:ALSO_BOUGHT]->(similar:Product)
        WHERE r.lift > 1.0
        RETURN count(similar) AS total
        """;

    private const string BehavioralQuery = """
        MATCH (p:Product {productId: $productId})-[r:ALSO_BOUGHT]->(similar:Product)
        WHERE r.lift > 1.0
        OPTIONAL MATCH (p)-[:MADE_BY]->(pb:Brand)
        OPTIONAL MATCH (similar)-[:MADE_BY]->(b:Brand)
        RETURN similar.productId AS productId,
               similar.productName AS productName,
               similar.productDescription AS productDescription,
               b.brandId AS brandId,
               b.name AS brandName,
               r.lift AS score,
               (pb IS NOT NULL AND b IS NOT NULL AND pb.brandId = b.brandId) AS sameBrand
        ORDER BY r.lift DESC
        SKIP $skip LIMIT $take
        """;

    // Blends both signals; UNION (not ALL) here only to get a distinct-product total for pagination.
    private const string HybridCountQuery = """
        MATCH (p:Product {productId: $productId})
        CALL {
            WITH p MATCH (p)-[r:SIMILAR_TO]->(c:Product) WHERE r.score >= $minScore
            RETURN c
          UNION
            WITH p MATCH (p)-[r:ALSO_BOUGHT]->(c:Product) WHERE r.lift > 1.0
            RETURN c
        }
        RETURN count(DISTINCT c) AS total
        """;

    private const string HybridQuery = """
        MATCH (p:Product {productId: $productId})
        OPTIONAL MATCH (p)-[:MADE_BY]->(pb:Brand)
        CALL {
            WITH p MATCH (p)-[r:SIMILAR_TO]->(c:Product) WHERE r.score >= $minScore
            RETURN c, r.score * $wContent AS s
          UNION ALL
            WITH p MATCH (p)-[r:ALSO_BOUGHT]->(c:Product) WHERE r.lift > 1.0
            RETURN c, (1.0 - 1.0 / r.lift) * $wBehavior AS s
        }
        WITH c, pb, sum(s) AS score
        OPTIONAL MATCH (c)-[:MADE_BY]->(b:Brand)
        RETURN c.productId AS productId,
               c.productName AS productName,
               c.productDescription AS productDescription,
               b.brandId AS brandId,
               b.name AS brandName,
               score,
               (pb IS NOT NULL AND b IS NOT NULL AND pb.brandId = b.brandId) AS sameBrand
        ORDER BY score DESC
        SKIP $skip LIMIT $take
        """;
}
