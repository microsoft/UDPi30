# Delta maintenance for the curated tables.
for table in ("sales", "customers"):
    spark.sql(f"OPTIMIZE {table}")
    spark.sql(f"VACUUM {table} RETAIN 168 HOURS")
print("Maintenance complete")
