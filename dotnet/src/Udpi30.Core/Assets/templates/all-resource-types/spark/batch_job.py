# Spark job definition entry point — referenced by
# resources.spark_job_definitions.batch_job. Invoked with: --mode full

import sys

from pyspark.sql import SparkSession

spark = SparkSession.builder.getOrCreate()
mode = sys.argv[sys.argv.index("--mode") + 1] if "--mode" in sys.argv else "incremental"

print(f"running batch job in {mode} mode")

# Example transformation over the curated lakehouse:
# df = spark.read.format("delta").table("curated_lakehouse.sales")
# df.groupBy("region").sum("amount").write.format("delta").mode("overwrite") \
#   .saveAsTable("curated_lakehouse.sales_by_region")
