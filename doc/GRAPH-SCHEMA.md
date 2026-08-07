# Neo4j Graph Schema Documentation

## Overview

The graph models a product catalog with relationships between products, brands, categories, and parameters. Recommendations are powered by two independent relationships: `SIMILAR_TO` (content signal, from GDS) and `ALSO_BOUGHT` (behavioral signal, from the Spark co-purchase pipeline — see [SPARK-PIPELINE.md](SPARK-PIPELINE.md)).

## Graph Visualization

```
                    ┌──────────────────────────────────────┐
                    │            SIMILAR_TO                │
                    │     (score, sameBrand properties)    │
                    └──────────────────────────────────────┘
                                      │
                                      ▼
┌────────────────┐ IN_SEGMENT  ┌──────────┐ CHILD_OF  ┌──────────┐
│ CatalogSegment │◄────────────│ Category │◄──────────│ Category │
└────────────────┘             └────▲─────┘           └──────────┘
                                    │
                                    │ BELONGS_TO
                                    │
                              ┌─────┴───┐  MADE_BY  ┌───────┐
                              │ Product │──────────►│ Brand │
                              └────┬────┘           └───────┘
                                   │
                                   │ HAS_PARAMETER
                                   ▼
                             ┌───────────┐
                             │ Parameter │
                             └───────────┘
```

`ALSO_BOUGHT` is a `Product`-to-`Product` self-relationship like `SIMILAR_TO`, omitted above for space —
see the [Relationships](#relationships) section below.

## Node Types

### Product

The central entity representing items in the catalog.

| Property | Type | Indexed | Description |
|----------|------|---------|-------------|
| `productId` | INTEGER | ✅ | Unique identifier |
| `productName` | STRING | ✅ | Display name |
| `productDescription` | STRING | ❌ | Detailed description |
| `brandId` | INTEGER | ❌ | Reference to brand |
| `categoryId` | INTEGER | ❌ | Reference to category |
| `created_at` | DATE_TIME | ❌ | Creation timestamp |

### Brand

Represents product manufacturers or brands.

| Property | Type | Indexed | Description |
|----------|------|---------|-------------|
| `brandId` | INTEGER | ✅ | Unique identifier |
| `name` | STRING | ✅ | Brand name |
| `created_at` | DATE_TIME | ❌ | Creation timestamp |

### Category

Hierarchical product categorization supporting parent-child relationships.

| Property | Type | Indexed | Description |
|----------|------|---------|-------------|
| `categoryId` | INTEGER | ✅ | Unique identifier |
| `categoryName` | STRING | ✅ | Category display name |
| `parentCategoryId` | INTEGER | ❌ | Reference to parent category |
| `created_at` | DATE_TIME | ❌ | Creation timestamp |

### Parameter

Product attributes and specifications.

| Property | Type | Indexed | Description |
|----------|------|---------|-------------|
| `parameterId` | INTEGER | ✅ | Unique identifier |
| `parameterName` | STRING | ✅ | Parameter name (e.g., "Color", "Size") |
| `value` | STRING | ✅ | Parameter value |
| `created_at` | DATE_TIME | ❌ | Creation timestamp |
| `updated_at` | DATE_TIME | ❌ | Last update timestamp |

### CatalogSegment

Groups categories into logical catalog segments.

| Property | Type | Indexed | Description |
|----------|------|---------|-------------|
| `segmentId` | INTEGER | ✅ | Unique identifier |
| `segmentName` | STRING | ❌ | Segment display name |

## Relationships

### SIMILAR_TO

Connects products that are similar to each other. This is the content signal for the recommendation engine.

- **Direction**: `(Product)-[:SIMILAR_TO]->(Product)`
- **Properties**:
  - `score` (FLOAT, indexed) - Similarity score between 0 and 1
  - `sameBrand` (BOOLEAN) - Indicates if both products share the same brand

### ALSO_BOUGHT

Connects products that are frequently co-purchased. This is the behavioral signal for the
recommendation engine, computed offline by the Spark pipeline (see
[SPARK-PIPELINE.md](SPARK-PIPELINE.md)) — never written by the API.

- **Direction**: `(Product)-[:ALSO_BOUGHT]->(Product)`
- **Properties**:
  - `count` (INTEGER) - Number of orders containing both products
  - `lift` (FLOAT, indexed) - `(count × totalOrders) / (cntA × cntB)`; > 1.0 means the pair
    co-occurs more than chance. Symmetric per pair.
  - `confidence` (FLOAT) - `count / cntA`; directional (A→B ≠ B→A)

### MADE_BY

Links a product to its manufacturer/brand.

- **Direction**: `(Product)-[:MADE_BY]->(Brand)`
- **Properties**: None

### BELONGS_TO

Assigns a product to a category.

- **Direction**: `(Product)-[:BELONGS_TO]->(Category)`
- **Properties**: None

### HAS_PARAMETER

Associates products with their attributes/specifications.

- **Direction**: `(Product)-[:HAS_PARAMETER]->(Parameter)`
- **Properties**: None

### CHILD_OF

Creates the category hierarchy by linking child categories to their parents.

- **Direction**: `(Category)-[:CHILD_OF]->(Category)`
- **Properties**: None

### IN_SEGMENT

Assigns a category to a catalog segment.

- **Direction**: `(Category)-[:IN_SEGMENT]->(CatalogSegment)`
- **Properties**: None
