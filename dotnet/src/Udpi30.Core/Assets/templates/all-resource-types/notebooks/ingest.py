# Ingest notebook — referenced by resources.notebooks.ingest_notebook
# Deployed to your Fabric workspace by udpi30. Reads from a source and lands
# raw data into the bound default lakehouse (raw_lakehouse).

from pyspark.sql import SparkSession

spark = SparkSession.builder.getOrCreate()

# Example: land a source table into the raw lakehouse.
# df = spark.read.format("csv").option("header", True).load("Files/incoming/sales.csv")
# df.write.format("delta").mode("append").saveAsTable("raw_lakehouse.sales")

print("ingest complete")
