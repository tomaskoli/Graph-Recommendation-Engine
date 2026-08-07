# Spark Co-Purchase Pipeline

Computes `ALSO_BOUGHT` edges (co-purchase lift) from transaction data and writes them
to Neo4j, complementing the GDS `SIMILAR_TO` edges.

Design rationale, the lift math, and every non-obvious gotcha:
**[doc/SPARK-PIPELINE.md](../doc/SPARK-PIPELINE.md)**.

## Setup

```bash
pip install -r spark/requirements.txt   # Stage 0/1 only; Stage 2/3 run in Docker
cp spark/.env.example spark/.env
```

`.env` holds `NEO4J_URI` / `NEO4J_USER` / `NEO4J_PASSWORD` / `NEO4J_DATABASE`.
Defaults match `docker-compose.neo4j.yml` and the API's `Neo4jOptions`.

Neo4j must be running, with constraints applied:

```bash
docker-compose -f deploy/Docker/docker-compose.neo4j.yml up -d
# then run deploy/scripts/seed-neo4j.cypher in Neo4j Browser
```

## Run order

Each stage depends on the previous one. GDS runs last (Stage 1 falls back to
category siblings when `SIMILAR_TO` doesn't exist yet).

```bash
python spark/generate_catalog.py            # 0: catalog → Neo4j
python spark/generate_transactions.py       # 1: baskets → Parquet
docker compose -f deploy/Docker/docker-compose.spark.yml run --rm --service-ports spark
# ^ 2+3: lift → Parquet + Neo4j.  --service-ports publishes the Spark UI (see below)
# then run deploy/scripts/compute-similarity-embeddings.cypher in Neo4j Browser
```

Everything is seeded and idempotent — safe to re-run any stage.

## Stages and options

| Stage | Script | What it does |
|-------|--------|--------------|
| 0 | `generate_catalog.py` | Synthetic catalog → Neo4j. The repo's seed script creates only constraints, no nodes. |
| 1 | `generate_transactions.py` | Graph-correlated baskets → `data/transactions/*.parquet` |
| 2 | `copurchase_lift.py` | Spark job: baskets → `count`/`lift`/`confidence` per pair → `data/edges/` |
| 3 | `copurchase_lift.py --write-neo4j` | Same run: writes `ALSO_BOUGHT` via the Neo4j Spark Connector |

**Stage 0** — `--products-per-leaf` (12), `--seed` (42), `--seed-data`
([catalog_seed.yaml](catalog_seed.yaml) — edit to reshape the catalog, no code
changes), `--dry-run` (generate + validate in memory, no Neo4j).

**Stage 1** — `--customers` (5000), `--orders` (50000), `--seed` (7), `--output`,
`--dry-run` (offline catalog, no Neo4j or disk).

**Stage 2/3** — `--input`, `--output`, `--min-support` (5, minimum co-purchase count
before a pair is scored), `--write-neo4j`.

## Docker options

The compose `command` defaults to the full Stage 2+3 pipeline. To run Stage 2 only
(Parquet, no Neo4j write), override it:

```bash
docker compose -f deploy/Docker/docker-compose.spark.yml run --rm --service-ports spark /opt/spark/bin/spark-submit spark/copurchase_lift.py
```

**Spark UI:** <http://localhost:4040>
The UI is served by the driver, so it's live only while the job runs (~1–2 min) and
dies with it — no history server is set up. Open it as soon as the job starts.

**Watch progress:** `docker logs -f $(docker ps -q --filter name=devnet-spark)`

[spark/Dockerfile](Dockerfile) is the `apache/spark` image plus the connector JAR baked
into `/opt/spark/jars`. That's why the command needs no `--packages` or env flags —
see doc/SPARK-PIPELINE.md Stage 3 for what that avoids. Rebuild after changing it:

```bash
docker compose -f deploy/Docker/docker-compose.spark.yml build
```

**Run Stage 2/3 outside Docker** (needs a JDK 11/17/21; `pyspark` deliberately isn't
in `requirements.txt`):

```bash
pip install pyspark==4.2.0
export PYSPARK_SUBMIT_ARGS="--packages org.neo4j.connectors:spark:6.0.0-s_2.13 pyspark-shell"
python spark/copurchase_lift.py --write-neo4j
```

Connector, Scala, and Spark versions must move together: connector 6.x is Scala
2.13-only, built against Spark 4.1.x.

## Expected output

Stage 2 prints the edge count, top-10 pairs by lift, and the physical plan
(`explain()`). With defaults: 50k orders → 137,970 order-lines → **3,342 directed
edges** at `min_support=5`. `count` and `lift` are symmetric per pair, `confidence` is
directional.
