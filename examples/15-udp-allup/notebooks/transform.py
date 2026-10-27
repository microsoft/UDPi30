# Transform raw data into the curated sales table.
raw = spark.read.table("raw_sales")
curated = raw.dropDuplicates(["order_id"])
curated.write.mode("overwrite").partitionBy("order_date").saveAsTable("sales")
print("Transform complete")
