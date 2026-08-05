"""Stage 2 of the Spark co-purchase pipeline (see doc/SPARK-PIPELINE.md).

Reads data/transactions (written by generate_transactions.py), computes co-purchase
lift and confidence per product pair, and writes data/edges as Parquet. This is the
pipeline's actual Spark job — Stage 0 and Stage 1 only manufacture its input.

Usage:
    python spark/copurchase_lift.py
"""

import argparse
from pathlib import Path

from pyspark.sql import SparkSession, functions as F

REPO_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_INPUT_DIR = REPO_ROOT / "data" / "transactions"
DEFAULT_OUTPUT_DIR = REPO_ROOT / "data" / "edges"

MIN_BASKET_SIZE = 2
MAX_BASKET_SIZE = 20
MIN_SUPPORT = 5


def build_spark():
    return SparkSession.builder.appName("copurchase-lift").master("local[*]").getOrCreate()


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


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, default=DEFAULT_INPUT_DIR)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT_DIR)
    parser.add_argument("--min-support", type=int, default=MIN_SUPPORT)
    args = parser.parse_args()

    spark = build_spark()
    spark.sparkContext.setLogLevel("WARN")
    try:
        edges = compute_edges(spark, args.input, args.min_support).cache()

        print(f"{edges.count()} directed edges (min_support={args.min_support})")
        edges.orderBy(F.col("lift").desc()).show(10, truncate=False)

        print("--- physical plan (see spark/README.md for how to read this) ---")
        edges.explain()

        edges.write.mode("overwrite").parquet(str(args.output))
        print(f"Wrote edges to {args.output}")
    finally:
        spark.stop()


if __name__ == "__main__":
    main()
