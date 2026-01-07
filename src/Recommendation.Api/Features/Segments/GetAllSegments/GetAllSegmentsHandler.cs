using FluentResults;
using MediatR;
using Neo4j.Driver;
using Recommendation.Api.Common.Contracts;
using Recommendation.Api.Features.Segments.Contracts;
using Recommendation.Api.Infrastructure.Neo4j;

namespace Recommendation.Api.Features.Segments.GetAllSegments;

public class GetAllSegmentsHandler(INeo4jConnectionFactory connectionFactory)
    : IRequestHandler<GetAllSegmentsQuery, Result<PaginatedResult<CatalogSegmentDto>>>
{
    public async Task<Result<PaginatedResult<CatalogSegmentDto>>> Handle(GetAllSegmentsQuery request, CancellationToken cancellationToken)
    {
        await using var session = connectionFactory.CreateSession();

        var skip = (request.Page - 1) * request.PageSize;

        // List all catalog segments 
        const string countQuery = "MATCH (s:CatalogSegment) RETURN count(s) AS total";

        const string query = """
            MATCH (s:CatalogSegment)
            RETURN s.segmentId AS segmentId, s.segmentName AS segmentName
            ORDER BY s.segmentName
            SKIP $skip LIMIT $take
            """;

        var (segments, totalCount) = await session.ExecuteReadAsync(async tx =>
        {
            var countCursor = await tx.RunAsync(countQuery);
            var countRecord = await countCursor.SingleAsync(cancellationToken);
            var total = countRecord["total"].As<int>();

            var cursor = await tx.RunAsync(query, new { skip, take = request.PageSize });
            var records = await cursor.ToListAsync(cancellationToken);

            var items = records
                .Select(record => new CatalogSegmentDto(
                    record["segmentId"].As<int>(),
                    record["segmentName"].As<string>()))
                .ToList();

            return (items, total);
        });

        return Result.Ok(PaginatedResult.Create(segments, totalCount, request.Page, request.PageSize));
    }
}
