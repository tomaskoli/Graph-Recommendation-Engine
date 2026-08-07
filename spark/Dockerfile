# Spark image with the Neo4j Spark Connector baked in.
#
# Pre-baking the JAR replaces `spark-submit --packages`, which removes the runtime
# Ivy resolution step — and with it the need to override the JVM's user.home (the
# base image's user has /nonexistent as its home, and Ivy writes its cache under
# Java's user.home, not $HOME). See doc/SPARK-PIPELINE.md Stage 3.
FROM apache/spark:4.1.3-scala2.13-java17-python3-r-ubuntu

# 6.0.0+ is Scala 2.13 only and is built against Spark 4.1.x — hence the base tag.
# This is a fat JAR (bundles the Neo4j Java driver, Netty, Kotlin), so there are no
# transitive dependencies to fetch. /opt/spark/jars is on the default classpath and
# is already owned by the image's `spark` user, so no USER switching is needed.
ARG NEO4J_CONNECTOR_VERSION=6.0.0-s_2.13
ARG NEO4J_CONNECTOR_SHA512=581709f819047af277a539d4717c9a3170f80673557493d181a43559210302eae24600b91ed58a8754593011c939caf077e3cccf2170c54766427f2edfdffc5e

RUN set -eu; \
    jar="/opt/spark/jars/neo4j-connector-spark-${NEO4J_CONNECTOR_VERSION}.jar"; \
    curl -fsSL -o "$jar" \
      "https://repo1.maven.org/maven2/org/neo4j/connectors/spark/${NEO4J_CONNECTOR_VERSION}/spark-${NEO4J_CONNECTOR_VERSION}.jar"; \
    echo "${NEO4J_CONNECTOR_SHA512}  $jar" | sha512sum -c -
