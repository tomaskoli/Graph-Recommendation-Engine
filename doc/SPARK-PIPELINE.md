# Spark Co-Purchase Pipeline Specification

## Purpose

Extend the recommendation engine with a **behavioral signal** computed by Apache Spark.
The existing GDS pipeline (FastRP + kNN) produces `SIMILAR_TO` edges from catalog
*structure* (categories, parameters). This pipeline produces `ALSO_BOUGHT` edges from
transaction *behavior* (co-purchase lift). The two signals are complementary and have
opposite failure modes:

| Signal | Edge | Strength | Fails on |
|--------|------|----------|----------|
| Content (GDS) | `SIMILAR_TO` | Cold-start products, substitutes | Cross-category intent (phone → case) |
| Behavioral (Spark) | `ALSO_BOUGHT` | Complements, real purchase intent | New products with no sales |

Neither pipeline overwrites the other's edges. The API blends both at query time.

## Architecture

```
generate_catalog.py ──▶ Neo4j (Product, Category, Brand, Parameter, CatalogSegment)
   (plain Python)              │
                                ▼
generate_transactions.py ──▶ Parquet ──▶ copurchase_lift.py ──▶ Parquet ──▶ Neo4j
     (plain Python)        (baskets)      (PySpark local[*])    (edges)   (ALSO_BOUGHT)
                                                                              │
GDS FastRP + kNN ─────────────────────────▶ SIMILAR_TO ──────────────────────┤
                                                                              ▼
                                                              API blends at query time
```

**No seed data exists in the repo today.** `deploy/scripts/seed-neo4j.cypher` only
declares constraints and indexes — it creates zero nodes. `generate_catalog.py` fills
that gap and is a prerequisite for everything below: `generate_transactions.py` reads
the catalog from Neo4j to correlate baskets, so an empty graph produces no usable
baskets. Run order: catalog → transactions → Spark job → GDS embeddings.

- Spark runs as PySpark on `local[*]` — no cluster. Recommended execution is via
  `deploy/Docker/docker-compose.spark.yml` (the `apache/spark` image already
  bundles PySpark, so `copurchase_lift.py` needs no custom build); running the
  script directly with a local `pip install pyspark` also works given a working
  JDK. The Neo4j Spark connector JAR (Stage 3) is pulled via `spark.jars.packages`
  (`org.neo4j:neo4j-connector-apache-spark_2.12` matching the PySpark version).
- Parquet is the intermediate at both hops so the Neo4j load can be re-run without
  re-running the aggregation (and vice versa).
- Aspire does not orchestrate Spark; the pipeline is a batch job run manually or by CI.

## Repository layout

```
spark/
├── catalog_seed.yaml           # catalog data (segments/categories/brands/parameters)
├── generate_catalog.py         # synthetic catalog generator (plain Python + neo4j driver)
├── generate_transactions.py    # synthetic basket generator (plain Python + neo4j driver)
├── copurchase_lift.py          # the Spark job: baskets → scored edges → Neo4j
├── requirements.txt            # neo4j, python-dotenv, pyyaml, pyarrow (no pyspark — see below)
├── .env.example                # NEO4J_URI / NEO4J_USER / NEO4J_PASSWORD / NEO4J_DATABASE template
└── README.md                   # how to run, expected output, plan-reading notes
deploy/Docker/docker-compose.spark.yml  # runs copurchase_lift.py in the apache/spark image
data/                           # gitignored Parquet output (transactions/, edges/)
```

## Stage 0 — Synthetic catalog generator

`generate_catalog.py`. Plain Python + the `neo4j` driver, **not** a Spark job — same
reasoning as the transaction generator: it manufactures input, it isn't the showcase.
Writes directly via batched `UNWIND ... MERGE` (no CSV/`LOAD CSV` plumbing needed for
demo scale).

Fills every node/relationship type the schema already defines but the repo never
seeds, matching `doc/GRAPH-SCHEMA.md` and what the GDS script and `/api/segments`
endpoint already assume exists:

1. **CatalogSegment** (~4–6: Electronics, Home, Fashion, Sports, ...).
2. **Category**, two levels deep, `CHILD_OF` parent, `IN_SEGMENT` a segment
   (~30–40 leaf categories across the segments).
3. **Brand** (~25–30).
4. **Product** (~400–600), `BELONGS_TO` a leaf category, `MADE_BY` a brand.
   Assign popularity from a Zipf distribution here — the transaction generator
   (Stage 1) reuses these weights so head/tail skew is consistent across both
   generators instead of resampled independently.
5. **Parameter** (2–5 per product: e.g. Color, Size), `HAS_PARAMETER`.

Note: `doc/GRAPH-SCHEMA.md` currently flags `CatalogSegment` and `IN_SEGMENT` as
"not yet ingested" — this generator resolves that gap. Update the doc once it lands.

Idempotent: run guarded by the existing `MATCH (n) DETACH DELETE n;` at the top of
`seed-neo4j.cypher`, or the script clears its own labels before writing. Seeded RNG
for reproducibility.

## Stage 1 — Synthetic transaction generator

`generate_transactions.py`. Plain Python, **not** a Spark job — it manufactures input,
it is not part of the showcase.

Uniformly random baskets produce lift ≈ 1.0 everywhere and the recommendations look
broken. The generator must correlate baskets with the existing graph:

1. Read product IDs, category membership, and popularity weights from Neo4j (written
   by Stage 0). `SIMILAR_TO` neighborhoods won't exist yet on a fresh graph — the GDS
   embeddings script runs *after* this pipeline, so basket walking must fall back to
   same-category siblings when `SIMILAR_TO` is empty (always true on first run).
2. Reuse the **Zipf popularity** weights Stage 0 assigned (real catalogs are
   head-heavy; this also creates the bestseller bias that lift must then correct).
3. Sample baskets: pick a seed product by popularity, then fill the basket by walking
   `SIMILAR_TO` edges and same-category siblings with decaying probability. Basket size
   drawn from a distribution centered on 2–5 items, with a small tail of large baskets
   (so the size cap in Stage 2 has something to cut).
4. Write one Parquet file: `order_id, customer_id, product_id, ts` (timestamps spread
   over ~180 days — required later for the `BOUGHT_NEXT` stretch goal).

Defaults: ~5 000 customers, ~50 000 orders. Seeded RNG for reproducibility.

## Stage 2 — Spark job: co-purchase lift

`copurchase_lift.py`. This is the showcase. Steps, in order:

### 2.1 Read + clean

```python
tx = spark.read.parquet("data/transactions")
```

- Deduplicate `(order_id, product_id)` (quantity > 1 is not a co-purchase signal).
- **Basket size cap: keep orders with 2–20 items.** Single-item orders carry no pair
  signal; a 200-item basket emits ~20k pairs of noise *and* is a hot key in the
  self-join below — the cap is the skew mitigation, not just denoising.
- **`clean.cache()`** — `clean` is consumed three times (total-order count, marginals,
  self-join). Without caching, the full read + filter lineage recomputes per action.

### 2.2 Marginals — written in SQL

One step is deliberately written as literal Spark SQL against a temp view, to show both
front-ends of the same Catalyst engine:

```python
clean.createOrReplaceTempView("baskets")
marg = spark.sql("""
    SELECT product_id, COUNT(DISTINCT order_id) AS cnt
    FROM baskets GROUP BY product_id
""")
n = clean.select("order_id").distinct().count()
```

### 2.3 Pair generation (self-join)

Self-join on `order_id` with `a.product_id < b.product_id` (each unordered pair once),
then count pairs and **prune `count >= minSupport` (default 5) before joining
marginals** — pruning after would shuffle the long tail for nothing.

### 2.4 Lift, confidence, and symmetrization

Raw co-purchase counts rank bestsellers: everything co-occurs with the top seller.
Score with **lift** instead:

```
lift(a,b) = P(a,b) / (P(a) · P(b)) = (pairCount · totalOrders) / (cntA · cntB)
```

- Join marginals with **`F.broadcast(marg)`** — marg is one row per product, tiny;
  broadcasting eliminates two shuffle stages (`BroadcastHashJoin` in the plan).
- Also compute **confidence** `P(b|a) = pairCount / cntA`, which is asymmetric
  (case → phone ≠ phone → case).
- Emit both directions: union the frame with src/dst swapped, confidence recomputed
  per direction. Lift and count are symmetric and carried through.

### 2.5 Plan inspection

The job calls `edges.explain()` once; `spark/README.md` documents how to read the
output. Confirmed against a real run: the marginal joins show as `BroadcastHashJoin`
as expected, and at demo data scale **AQE also converts the self-join into a
`BroadcastHashJoin`** once it sees how small `clean` is post-filter — a genuine
runtime re-optimization, not a static plan choice. At a scale where `clean` exceeds
`spark.sql.autoBroadcastJoinThreshold`, expect a shuffled `SortMergeJoin` there instead.

### 2.6 Output

Write the edge frame to `data/edges` (Parquet): `src, dst, count, lift, confidence`.

## Stage 3 — Neo4j write

Same script, final stage (skippable via flag so the aggregation can run standalone).

1. **Delete stale edges first** — the connector's `Overwrite` mode MERGEs matching
   pairs but never deletes pairs that dropped below support since the last run. Mirror
   `compute-similarity-embeddings.cypher` step 1: run
   `MATCH ()-[r:ALSO_BOUGHT]->() DELETE r` via the connector's `script` option, then
   write with `Append`.
2. Relationship write:

```python
(edges_df.coalesce(1)                       # parallel rel writes deadlock on node locks
  .write.format("org.neo4j.spark.DataSource")
  .mode("Append")
  .option("relationship", "ALSO_BOUGHT")
  .option("relationship.save.strategy", "keys")
  .option("relationship.source.labels", ":Product")
  .option("relationship.source.save.mode", "Match")   # never create products from tx data
  .option("relationship.source.node.keys", "src:productId")
  .option("relationship.target.labels", ":Product")
  .option("relationship.target.save.mode", "Match")
  .option("relationship.target.node.keys", "dst:productId")
  .option("relationship.properties", "count,lift,confidence")
  .save())
```

3. Index, added to the seed script:

```cypher
CREATE INDEX also_bought_lift_idx IF NOT EXISTS
FOR ()-[r:ALSO_BOUGHT]-() ON (r.lift);
```

`Product.productId` uniqueness constraint already exists — required by `node.keys`.

## Stage 4 — API blend

`GetRecommendationsHandler` gains a query parameter `strategy=content|behavioral|hybrid`
(default `hybrid`). Hybrid query:

```cypher
MATCH (p:Product {productId: $productId})
CALL {
    WITH p MATCH (p)-[r:SIMILAR_TO]->(c:Product) WHERE r.score >= $minScore
    RETURN c, r.score * $wContent AS s
  UNION ALL
    WITH p MATCH (p)-[r:ALSO_BOUGHT]->(c:Product) WHERE r.lift > 1.0
    RETURN c, (1.0 - 1.0/r.lift) * $wBehavior AS s
}
WITH c, sum(s) AS score
OPTIONAL MATCH (c)-[:MADE_BY]->(b:Brand)
RETURN c.productId AS productId, c.productName AS productName,
       c.productDescription AS productDescription,
       b.brandId AS brandId, b.name AS brandName, score
ORDER BY score DESC SKIP $skip LIMIT $take
```

- `UNION ALL`, not `UNION` — the branches never produce identical rows; `UNION`'s
  dedup is wasted work.
- `1 - 1/lift` squashes unbounded lift into 0..1 so the two signals are commensurable.
- `wContent` / `wBehavior` are hardcoded in `RecommendationConstants` (start 0.5/0.5)
  next to `MinSimilarityScore` — no config knobs for weights nobody tunes.
- Cache key gains the strategy segment: `recs:{productId}:{strategy}:{page}:{pageSize}`.
- A product appearing on both edges scores from both branches — intentional: agreement
  between independent signals is the strongest recommendation.

## Spark concepts demonstrated

| Concept | Where |
|---------|-------|
| DataFrame API (Spark SQL / Catalyst) | entire job |
| Literal SQL front-end | marginals via temp view (2.2) |
| Transformations vs actions, lineage | `cache()` on `clean` (2.1) |
| Shuffle vs broadcast joins | `F.broadcast(marg)` (2.4) |
| Adaptive Query Execution | self-join runtime-converted to `BroadcastHashJoin` at demo scale (2.5) |
| Skew mitigation | basket-size cap on the self-join key (2.1) |
| Early pruning before wide joins | `minSupport` filter placement (2.3) |
| Plan reading | `explain()` + README notes (2.5) |
| Window functions | `BOUGHT_NEXT` stretch goal |

Deliberately absent: raw RDD API (obsolete in application code) and streaming (out of
scope for a batch demo).

## Stretch goals (not in v1)

1. **`BOUGHT_NEXT`** — sequential purchases ("A, then B within 30 days") via
   `Window.partitionBy("customer_id").orderBy("ts")`. Better signal than co-purchase
   for accessories/consumables; the generator's timestamps already support it.
2. **Spark ↔ Neo4j read-back** — read `SIMILAR_TO` scores back into Spark via the
   connector's `query` read mode and join with behavioral scores offline.
3. **`Customer` nodes + `BOUGHT` edges** — only alongside a consuming feature
   (customer-level collaborative filtering endpoint, or GDS Louvain segmentation on
   the customer↔product bipartite graph). If added, aggregate to one
   `BOUGHT {purchaseCount, lastAt}` edge per customer–product pair; the graph does
   not need individual transactions.

## Decisions log

| Decision | Choice | Why |
|----------|--------|-----|
| Edge type | New `ALSO_BOUGHT`, never touch `SIMILAR_TO` | Opposite failure modes; blending needs both |
| Scoring | Lift (+ confidence), not raw counts | Raw counts rank bestsellers everywhere |
| Blend point | API query time | Keeps pipelines independent and re-runnable |
| Spark runtime | Bare PySpark `local[*]` | Demo scale; no infra to maintain |
| Catalog seed | New `generate_catalog.py`, plain Python | Repo has zero node-creation Cypher today — only constraints/indexes exist |
| Generator | Plain Python, graph-correlated, Zipf popularity | Uniform random data makes lift ≈ 1.0 everywhere |
| Stale edges | Delete-then-append | Connector `Overwrite` never deletes dropped pairs |
| Rel write parallelism | `coalesce(1)` | Concurrent partitions deadlock on node locks |
| Docker image (if ever) | `apache/spark` | `bitnami/spark` moved to `bitnamilegacy`, unmaintained |
