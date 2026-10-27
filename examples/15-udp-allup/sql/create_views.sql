-- Warehouse views created on deploy.
CREATE OR ALTER VIEW dbo.gold_order_summary AS
SELECT
    order_date,
    COUNT(*)          AS total_orders,
    SUM(total_amount) AS revenue
FROM dbo.sales
GROUP BY order_date;
