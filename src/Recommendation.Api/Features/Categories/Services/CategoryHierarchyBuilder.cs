using Recommendation.Api.Features.Categories.Contracts;

namespace Recommendation.Api.Features.Categories.Services;

public sealed record FlatCategory(
    int CategoryId, 
    string CategoryName, 
    int? ParentCategoryId,
    int? SegmentId,
    string? SegmentName);

public static class CategoryHierarchyBuilder
{
    public static List<CategoryDto> Build(IEnumerable<FlatCategory> flatList, HashSet<int> rootIds)
    {
        var flatArray = flatList.ToList();
        var lookup = flatArray.ToLookup(x => x.ParentCategoryId);

        CategoryDto BuildCategory(FlatCategory category)
        {
            var children = lookup[category.CategoryId]
                .Select(BuildCategory)
                .OrderBy(c => c.CategoryName)
                .ToList();

            return new CategoryDto(
                category.CategoryId,
                category.CategoryName,
                category.ParentCategoryId,
                category.SegmentId,
                category.SegmentName,
                children.Count > 0 ? children : null);
        }

        return flatArray
            .Where(c => rootIds.Contains(c.CategoryId))
            .Select(BuildCategory)
            .OrderBy(c => c.CategoryName)
            .ToList();
    }
}

