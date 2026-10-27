-- Warehouse SQL — referenced by resources.warehouses.analytics_warehouse.
-- Runs against the analytics warehouse after it is created.

CREATE VIEW IF NOT EXISTS dbo.sales_summary AS
SELECT
    region,
    COUNT(*)      AS order_count,
    SUM(amount)   AS total_amount
FROM dbo.sales
GROUP BY region;
