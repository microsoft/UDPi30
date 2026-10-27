# Read a lakehouse that lives in another workspace (external_lakehouse in udp.yml).
df = spark.read.table("shared_lakehouse.dim_customer")
print(df.count())
