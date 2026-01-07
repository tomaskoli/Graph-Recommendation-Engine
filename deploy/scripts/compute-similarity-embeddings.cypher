// ============================================================================
// Neo4j GDS - Embeddings + kNN Product Similarity
// ============================================================================
// Uses FastRP to generate embeddings from graph structure, then kNN to find nearest neighbors.
//
// Usage in Neo4j Browser:
//   1. Neo4j GDS plugin installed
//   1. Connect to 'recommendation' database
//   2. Run each section in order
//
// ============================================================================

// ----------------------------------------------------------------------------
// Step 1: Clean up existing similarity edges
// ----------------------------------------------------------------------------
MATCH ()-[r:SIMILAR_TO]->()
DELETE r;

// ----------------------------------------------------------------------------
// Step 2: Create in-memory graph projection
// ----------------------------------------------------------------------------
// Include all node types and relationships that define product similarity
// UNDIRECTED orientation allows random walks in both directions
//
// Exclude Brand from projection - brand acts as a "mega-hub" that makes
// all products of the same brand appear similar regardless of category.
// Brand similarity is handled separately in Step 6 (score boost).
//
CALL gds.graph.project(
    'product-embedding-graph',
    ['Product', 'Category', 'CatalogSegment', 'Parameter'],
    {
        BELONGS_TO: { orientation: 'UNDIRECTED' },
        CHILD_OF: { orientation: 'UNDIRECTED' },
        IN_SEGMENT: { orientation: 'UNDIRECTED' },
        HAS_PARAMETER: { orientation: 'UNDIRECTED' }
    }
)
YIELD graphName, nodeCount, relationshipCount;

// ----------------------------------------------------------------------------
// Step 3: Generate embeddings using FastRP (Fast Random Projection)
// ----------------------------------------------------------------------------
// FastRP creates dense vector representations of nodes based on their
// neighborhood structure. No LLM or training required.
//
// Parameters:
//   embeddingDimension: size of output vector (higher = more expressive)
//   iterationWeights: influence of 1-hop, 2-hop, 3-hop, 4-hop neighbors
//     [0.0, 1.0, 1.0, 1.0, 1.0] = skip self, equal weight on 1/2/3/4 hop neighbors
//
// Result: each node gets an 'embedding' property in the projected graph

CALL gds.fastRP.mutate(
    'product-embedding-graph',
    {
        embeddingDimension: 256,
        iterationWeights: [0.0, 1.0, 1.0, 1.0, 1.0],
        concurrency: 1,
        randomSeed: 42,
        mutateProperty: 'embedding'
    }
)
YIELD nodePropertiesWritten;

// ----------------------------------------------------------------------------
// Step 4: Run kNN on embeddings to find similar products
// ----------------------------------------------------------------------------
// kNN computes cosine similarity between embedding vectors and writes
// SIMILAR_TO relationships for the top-K most similar product pairs.
//
// Parameters: 
//   sourceNodeFilter/targetNodeFilter: only compare Products (not Categories)
//   nodeProperties: use the 'embedding' vectors we just created
//   topK: max similar products per product
//   similarityCutoff: minimum similarity to write an edge (0-1 scale)

CALL gds.knn.filtered.write(
    'product-embedding-graph',
    {
        sourceNodeFilter: 'Product',
        targetNodeFilter: 'Product',
        nodeProperties: ['embedding'],
        topK: 20,
        similarityCutoff: 0.5,
        concurrency: 1,
        randomSeed: 42,
        writeRelationshipType: 'SIMILAR_TO',
        writeProperty: 'score'
    }
)
YIELD nodesCompared, relationshipsWritten, similarityDistribution;

// ----------------------------------------------------------------------------
// Step 5: Drop the in-memory graph projection (cleanup)
// ----------------------------------------------------------------------------
CALL gds.graph.drop('product-embedding-graph') YIELD graphName;

// ----------------------------------------------------------------------------
// Step 6: Add brand similarity boost
// ----------------------------------------------------------------------------
// Products from the same brand get a score bonus

MATCH (p1:Product)-[sim:SIMILAR_TO]->(p2:Product)
WHERE EXISTS { (p1)-[:MADE_BY]->(b:Brand)<-[:MADE_BY]-(p2) }
SET sim.score = sim.score + 0.01,
    sim.sameBrand = true;

// ----------------------------------------------------------------------------
// Step 7: Create index for fast similarity lookups
// ----------------------------------------------------------------------------
CREATE INDEX similar_to_score_idx IF NOT EXISTS
FOR ()-[r:SIMILAR_TO]-() ON (r.score);

// ----------------------------------------------------------------------------
// Verification queries
// ----------------------------------------------------------------------------

// Check similarity count
// MATCH ()-[r:SIMILAR_TO]->() RETURN count(r) AS totalSimilarities;

// Check sample similarities for a product
// MATCH (p:Product {productId: 1})-[r:SIMILAR_TO]->(similar:Product)
// RETURN similar.productId, similar.productName, r.score, r.sameBrand
// ORDER BY r.score DESC LIMIT 10;

