using FluentResults;
using MediatR;
using Neo4j.Driver;
using Recommendation.Api.Common.Contracts;
using Recommendation.Api.Features.Categories.Contracts;
using Recommendation.Api.Features.Categories.Services;
using Recommendation.Api.Infrastructure.Neo4j;

namespace Recommendation.Api.Features.Segments.GetCategoriesBySegment;

public class GetCategoriesBySegmentHandler(INeo4jConnectionFactory connectionFactory)
    : IRequestHandler<GetCategoriesBySegmentQuery, Result<PaginatedResult<CategoryDto>>>
{
    public async Task<Result<PaginatedResult<CategoryDto>>> Handle(GetCategoriesBySegmentQuery request, CancellationToken cancellationToken)
    {
        await using var session = connectionFactory.CreateSession();

        var skip = (request.Page - 1) * request.PageSize;

        // Segment-filtered 
        const string countQuery = """
            MATCH (c:Category)-[:IN_SEGMENT]->(s:CatalogSegment {segmentId: $segmentId})
            WHERE NOT (c)-[:CHILD_OF]->(:Category)
            RETURN count(c) AS total
            """;

        const string rootIdsQuery = """
            MATCH (c:Category)-[:IN_SEGMENT]->(s:CatalogSegment {segmentId: $segmentId})
            WHERE NOT (c)-[:CHILD_OF]->(:Category)
            RETURN c.categoryId AS categoryId
            ORDER BY c.categoryName
            SKIP $skip LIMIT $take
            """;

        // Full subtree for segment
        const string hierarchyQuery = """
            MATCH (root:Category)
            WHERE root.categoryId IN $rootIds
            OPTIONAL MATCH (root)<-[:CHILD_OF*0..]-(descendant:Category)
            OPTIONAL MATCH (descendant)-[:CHILD_OF]->(parent:Category)
            OPTIONAL MATCH (descendant)-[:IN_SEGMENT]->(s:CatalogSegment)
            RETURN descendant.categoryId AS categoryId, 
                   descendant.categoryName AS categoryName,
                   parent.categoryId AS parentCategoryId,
                   s.segmentId AS segmentId,
                   s.segmentName AS segmentName
            ORDER BY descendant.categoryName
            """;

        var (flatCategories, rootIds, totalCount) = await session.ExecuteReadAsync(async tx =>
        {
            var countCursor = await tx.RunAsync(countQuery, new { segmentId = request.SegmentId });
            var countRecord = await countCursor.SingleAsync(cancellationToken);
            var total = countRecord["total"].As<int>();

            var rootIdsCursor = await tx.RunAsync(rootIdsQuery, new { segmentId = request.SegmentId, skip, take = request.PageSize });
            var rootIdsRecords = await rootIdsCursor.ToListAsync(cancellationToken);
            var rootIdsList = rootIdsRecords.Select(r => r["categoryId"].As<int>()).ToList();

            if (rootIdsList.Count == 0)
            {
                return (new List<FlatCategory>(), rootIdsList, total);
            }

            var cursor = await tx.RunAsync(hierarchyQuery, new { rootIds = rootIdsList });
            var records = await cursor.ToListAsync(cancellationToken);

            var items = records
                .Select(record => new FlatCategory(
                    record["categoryId"].As<int>(),
                    record["categoryName"].As<string>(),
                    record["parentCategoryId"].As<int?>(),
                    record["segmentId"].As<int?>(),
                    record["segmentName"].As<string?>()))
                .DistinctBy(x => x.CategoryId)
                .ToList();

            return (items, rootIdsList, total);
        });

        var categories = CategoryHierarchyBuilder.Build(flatCategories, rootIds.ToHashSet());

        return Result.Ok(PaginatedResult.Create(categories, totalCount, request.Page, request.PageSize));
    }
}
