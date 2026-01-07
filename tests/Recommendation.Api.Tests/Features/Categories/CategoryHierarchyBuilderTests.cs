using Recommendation.Api.Features.Categories.Services;

namespace Recommendation.Api.Tests.Features.Categories;

public class CategoryHierarchyBuilderTests
{
    [Fact]
    public void Build_WithFlatList_BuildsHierarchy()
    {
        var flatCategories = new List<FlatCategory>
        {
            new(1, "Electronics", null, 1, "Tech"),
            new(2, "Phones", 1, 1, "Tech"),
            new(3, "Laptops", 1, 1, "Tech"),
            new(4, "Smartphones", 2, 1, "Tech")
        };
        var rootIds = new HashSet<int> { 1 };

        var result = CategoryHierarchyBuilder.Build(flatCategories, rootIds);

        Assert.Single(result);
        var root = result[0];
        Assert.Equal("Electronics", root.CategoryName);
        Assert.NotNull(root.SubCategories);
        Assert.Equal(2, root.SubCategories!.Count);
    }

    [Fact]
    public void Build_SortsChildrenByName()
    {
        var flatCategories = new List<FlatCategory>
        {
            new(1, "Root", null, null, null),
            new(2, "Zebra", 1, null, null),
            new(3, "Apple", 1, null, null),
            new(4, "Mango", 1, null, null)
        };
        var rootIds = new HashSet<int> { 1 };

        var result = CategoryHierarchyBuilder.Build(flatCategories, rootIds);

        var children = result[0].SubCategories!;
        Assert.Equal("Apple", children[0].CategoryName);
        Assert.Equal("Mango", children[1].CategoryName);
        Assert.Equal("Zebra", children[2].CategoryName);
    }

    [Fact]
    public void Build_WithMultipleRoots_ReturnsAllRoots()
    {
        var flatCategories = new List<FlatCategory>
        {
            new(1, "Electronics", null, 1, "Tech"),
            new(2, "Clothing", null, 2, "Fashion"),
            new(3, "Phones", 1, 1, "Tech")
        };
        var rootIds = new HashSet<int> { 1, 2 };

        var result = CategoryHierarchyBuilder.Build(flatCategories, rootIds);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, c => c.CategoryName == "Electronics");
        Assert.Contains(result, c => c.CategoryName == "Clothing");
    }

    [Fact]
    public void Build_LeafCategory_HasNullSubCategories()
    {
        var flatCategories = new List<FlatCategory>
        {
            new(1, "Root", null, null, null),
            new(2, "Leaf", 1, null, null)
        };
        var rootIds = new HashSet<int> { 1 };

        var result = CategoryHierarchyBuilder.Build(flatCategories, rootIds);

        var leaf = result[0].SubCategories![0];
        Assert.Null(leaf.SubCategories);
    }

    [Fact]
    public void Build_EmptyList_ReturnsEmptyResult()
    {
        var flatCategories = new List<FlatCategory>();
        var rootIds = new HashSet<int> { 1 };

        var result = CategoryHierarchyBuilder.Build(flatCategories, rootIds);

        Assert.Empty(result);
    }

    [Fact]
    public void Build_DeepHierarchy_BuildsCorrectly()
    {
        var flatCategories = new List<FlatCategory>
        {
            new(1, "Level1", null, null, null),
            new(2, "Level2", 1, null, null),
            new(3, "Level3", 2, null, null),
            new(4, "Level4", 3, null, null)
        };
        var rootIds = new HashSet<int> { 1 };

        var result = CategoryHierarchyBuilder.Build(flatCategories, rootIds);

        var level1 = result[0];
        var level2 = level1.SubCategories![0];
        var level3 = level2.SubCategories![0];
        var level4 = level3.SubCategories![0];

        Assert.Equal("Level1", level1.CategoryName);
        Assert.Equal("Level2", level2.CategoryName);
        Assert.Equal("Level3", level3.CategoryName);
        Assert.Equal("Level4", level4.CategoryName);
        Assert.Null(level4.SubCategories);
    }
}

