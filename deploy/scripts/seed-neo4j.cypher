// Recommendation Neo4j Seed Data
// Manual setup in Neo4j Browser:
//   1. Connect to 'system' database and run: CREATE DATABASE recommendation IF NOT EXISTS;
//   2. Switch to 'recommendation' database: :use recommendation
//   3. Run the rest of this script

// Clear existing data (use with caution in production)
MATCH (n) DETACH DELETE n;

// Unique constraints (also create implicit indexes)
CREATE CONSTRAINT product_id_unique IF NOT EXISTS
FOR (p:Product) REQUIRE p.productId IS UNIQUE;

CREATE CONSTRAINT category_id_unique IF NOT EXISTS
FOR (c:Category) REQUIRE c.categoryId IS UNIQUE;

CREATE CONSTRAINT brand_id_unique IF NOT EXISTS
FOR (b:Brand) REQUIRE b.brandId IS UNIQUE;

CREATE CONSTRAINT segment_id_unique IF NOT EXISTS
FOR (s:CatalogSegment) REQUIRE s.segmentId IS UNIQUE;

CREATE CONSTRAINT parameter_unique IF NOT EXISTS
FOR (p:Parameter) REQUIRE (p.parameterId, p.value) IS UNIQUE;

// Additional indexes for query performance
CREATE INDEX product_name_idx IF NOT EXISTS
FOR (p:Product) ON (p.productName);

CREATE INDEX category_name_idx IF NOT EXISTS
FOR (c:Category) ON (c.categoryName);

CREATE INDEX brand_name_idx IF NOT EXISTS
FOR (b:Brand) ON (b.name);

CREATE INDEX parameter_id_idx IF NOT EXISTS
FOR (p:Parameter) ON (p.parameterId);

CREATE INDEX parameter_name_idx IF NOT EXISTS
FOR (p:Parameter) ON (p.parameterName);

