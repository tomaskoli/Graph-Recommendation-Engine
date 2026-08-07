"""Stages 2-3 of the Spark co-purchase pipeline (see doc/SPARK-PIPELINE.md).

Reads data/transactions (written by generate_transactions.py), computes co-purchase
lift and confidence per product pair, and writes data/edges as Parquet. This is the
pipeline's actual Spark job — Stage 0 and Stage 1 only manufacture its input.

With --write-neo4j, also writes the result to Neo4j as ALSO_BOUGHT relationships via
the Neo4j Spark Connector (org.neo4j.connectors:spark, the current package — not the
older org.neo4j:neo4j-connector-apache-spark, which has no Spark 4.x build). The
connector JAR must be on the classpath: spark/Dockerfile bakes it into the image, so
running via docker-compose.spark.yml needs no extra flags. Running the script outside
that image means supplying it yourself — see spark/README.md.

Usage:
    docker compose -f deploy/Docker/docker-compose.spark.yml run --rm spark
    python spark/copurchase_lift.py               # Stage 2 only, no connector needed
"""

import argparse
import os
import sys
from pathlib import Path

from pyspark.sql import SparkSession, functions as F

REPO_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_INPUT_DIR = REPO_ROOT / "data" / "transactions"
DEFAULT_OUTPUT_DIR = REPO_ROOT / "data" / "edges"

MIN_BASKET_SIZE = 2
MAX_BASKET_SIZE = 20
MIN_SUPPORT = 5

# Connector coordinates, kept here for reference only — spark/Dockerfile is what
# actually puts the JAR on the classpath (pinned to the same version there).
# Deliberately NOT wired into SparkSession.config("spark.jars.packages") below: Ivy
# resolution happens in the spark-submit launcher before the driver JVM starts, so
# setting it from Python is silently ignored and shows up later as a confusing
# DATA_SOURCE_NOT_FOUND. It only works as a `--packages` CLI arg (or a baked-in JAR).
NEO4J_CONNECTOR_PACKAGE = "org.neo4j.connectors:spark:6.0.0-s_2.13"


def build_spark(need_neo4j_connector: bool = False):
    builder = SparkSession.builder.appName("copurchase-lift").master("local[*]")
    if need_neo4j_connector:
        # Only the connector's own runtime config — read at write() time, not at
        # classpath-resolution time, so setting these here is fine.
        builder = (
            builder
            .config("neo4j.url", os.environ.get("NEO4J_URI", "neo4j://localhost:7687"))
            .config("neo4j.authentication.type", "basic")
            .config("neo4j.authentication.basic.username", os.environ.get("NEO4J_USER", "neo4j"))
            .config("neo4j.authentication.basic.password", os.environ.get("NEO4J_PASSWORD", "12345678"))
            .config("neo4j.database", os.environ.get("NEO4J_DATABASE", "recommendation"))
        )
    return builder.getOrCreate()


def compute_edges(spark, input_dir: Path, min_support: int):
    tx = spark.read.parquet(str(input_dir))

    # Dedup (order_id, product_id) — quantity > 1 isn't a co-purchase signal.
    dedup = tx.dropDuplicates(["order_id", "product_id"])

    # Basket-size cap: single-item orders carry no pair signal, and an uncapped
    # basket is a hot key in the self-join below (order_id is the join key) —
    # this is skew mitigation, not just denoising.
    sizes = dedup.groupBy("order_id").agg(F.count("*").alias("size"))
    valid_orders = sizes.where(
        (F.col("size") >= MIN_BASKET_SIZE) & (F.col("size") <= MAX_BASKET_SIZE)
    ).select("order_id")

    # clean is read three times below (n, marginals, self-join) — cache so the
    # read + filter lineage doesn't recompute for each.
    clean = dedup.join(valid_orders, "order_id").cache()
    n = clean.select("order_id").distinct().count()

    # Marginals, written as literal SQL against a temp view — same Catalyst plan
    # as the DataFrame API, just the other front-end.
    clean.createOrReplaceTempView("baskets")
    marg = spark.sql("""
        SELECT product_id, COUNT(DISTINCT order_id) AS cnt
        FROM baskets GROUP BY product_id
    """)

    # Pair generation: self-join on order_id, a.product_id < b.product_id keeps
    # each unordered pair once. Prune by min_support BEFORE joining marginals —
    # pruning after would shuffle the long tail for nothing.
    a, b = clean.alias("a"), clean.alias("b")
    pairs = (
        a.join(b, (F.col("a.order_id") == F.col("b.order_id")) & (F.col("a.product_id") < F.col("b.product_id")))
         .groupBy(F.col("a.product_id").alias("src"), F.col("b.product_id").alias("dst"))
         .count()
         .where(F.col("count") >= min_support)
    )

    # marg is one row per product — tiny. Broadcasting turns these into
    # BroadcastHashJoins instead of shuffling pairs against marg twice.
    marg_b = F.broadcast(marg)
    scored = (
        pairs
        .join(marg_b.select(F.col("product_id").alias("src"), F.col("cnt").alias("cs")), "src")
        .join(marg_b.select(F.col("product_id").alias("dst"), F.col("cnt").alias("cd")), "dst")
        # lift = P(a,b) / (P(a)*P(b)) — raw counts would just rank bestsellers,
        # since everything co-occurs with the top seller.
        .withColumn("lift", (F.col("count") * F.lit(n)) / (F.col("cs") * F.col("cd")))
        .withColumn("confidence_ab", F.col("count") / F.col("cs"))
        .withColumn("confidence_ba", F.col("count") / F.col("cd"))
    )

    # Confidence is asymmetric (case -> phone != phone -> case); emit both
    # directions. lift and count are symmetric and carried through unchanged.
    forward = scored.select("src", "dst", "count", "lift", F.col("confidence_ab").alias("confidence"))
    backward = scored.select(
        F.col("dst").alias("src"), F.col("src").alias("dst"), "count", "lift",
        F.col("confidence_ba").alias("confidence"),
    )
    return forward.unionByName(backward)


def write_to_neo4j(edges_df):
    """Stage 3: write scored edges to Neo4j as ALSO_BOUGHT via the Neo4j Spark
    Connector. script.1/script.2 run once before the write (docs confirm this is
    per write-operation, not per-partition, so coalesce(1) below isn't load-bearing
    for that — it's still required to avoid relationship-write lock deadlocks)."""
    (
        edges_df.coalesce(1)  # parallel relationship writes deadlock on node locks
        .write.format("org.neo4j.spark.DataSource")
        .mode("Append")
        # Append/CREATE never deletes pairs that dropped below min_support since
        # the last run — clear stale edges first, matching the same step in
        # compute-similarity-embeddings.cypher for SIMILAR_TO. Index creation is
        # idempotent (IF NOT EXISTS), so running it every write is harmless.
        .option("script.1", "MATCH ()-[r:ALSO_BOUGHT]->() DELETE r")
        .option("script.2", "CREATE INDEX also_bought_lift_idx IF NOT EXISTS FOR ()-[r:ALSO_BOUGHT]-() ON (r.lift)")
        .option("relationship", "ALSO_BOUGHT")
        .option("relationship.save.strategy", "keys")
        .option("relationship.source.labels", ":Product")
        .option("relationship.source.save.mode", "Match")  # never create products from tx data
        .option("relationship.source.node.keys", "src:productId")
        .option("relationship.target.labels", ":Product")
        .option("relationship.target.save.mode", "Match")
        .option("relationship.target.node.keys", "dst:productId")
        .option("relationship.properties", "count,lift,confidence")
        .save()
    )

    count = (
        edges_df.sparkSession.read.format("org.neo4j.spark.DataSource")
        .option("query", "MATCH ()-[r:ALSO_BOUGHT]->() RETURN count(r) AS c")
        .load()
        .collect()[0]["c"]
    )
    print(f"Wrote {count} ALSO_BOUGHT relationships to Neo4j")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, default=DEFAULT_INPUT_DIR)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT_DIR)
    parser.add_argument("--min-support", type=int, default=MIN_SUPPORT)
    parser.add_argument("--write-neo4j", action="store_true",
                         help="also write the result to Neo4j as ALSO_BOUGHT edges")
    args = parser.parse_args()

    if args.write_neo4j:
        # Must happen before build_spark(): the connector config below is
        # session-level (spark.jars.packages, neo4j.url, ...), set at creation
        # and immutable afterward. Best-effort — fine locally without
        # python-dotenv; Docker gets these vars via docker-compose's env_file.
        try:
            from dotenv import load_dotenv
            load_dotenv(Path(__file__).parent / ".env")
        except ImportError:
            pass

    spark = build_spark(need_neo4j_connector=args.write_neo4j)
    spark.sparkContext.setLogLevel("WARN")
    try:
        edges = compute_edges(spark, args.input, args.min_support).cache()

        print(f"{edges.count()} directed edges (min_support={args.min_support})")
        edges.orderBy(F.col("lift").desc()).show(10, truncate=False)

        print("--- physical plan (see spark/README.md for how to read this) ---")
        edges.explain()

        edges.write.mode("overwrite").parquet(str(args.output))
        print(f"Wrote edges to {args.output}")

        if args.write_neo4j:
            write_to_neo4j(edges)
            # ponytail: the connector's Neo4j driver leaves non-daemon JVM threads, so
            # spark.stop() and the JVM's own shutdown both block forever — spark-submit
            # hangs on a finished job and the container never exits. Killing just the
            # Python process isn't enough: the JVM is spark-submit's (and the
            # container's) main process, so it has to go first. The write is already
            # committed server-side. Failure paths still fall through to the graceful
            # spark.stop() below, preserving tracebacks and exit codes.
            sys.stdout.flush()
            sys.stderr.flush()
            try:
                spark._jvm.System.exit(0)
            except Exception:
                pass  # expected — the py4j gateway dies with the JVM
            os._exit(0)
    finally:
        spark.stop()


if __name__ == "__main__":
    main()
