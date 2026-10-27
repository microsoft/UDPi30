# Ingest raw data into the raw lakehouse.
# Parameters (source_path, batch_size, full_refresh) are declared in udp.yml.
source_path = "Files/landing"
batch_size = 1000
full_refresh = False

df = spark.read.format("csv").option("header", True).load(source_path)
df.limit(batch_size).write.mode("overwrite" if full_refresh else "append").saveAsTable("raw_sales")
print("Ingest complete")
