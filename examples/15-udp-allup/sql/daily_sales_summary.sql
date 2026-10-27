-- Materialized lake view: daily sales rollup, refreshed per refresh_cron in udp.yml.
CREATE MATERIALIZED LAKE VIEW IF NOT EXISTS daily_sales_summary AS
SELECT
    order_date,
    COUNT(*)          AS total_orders,
    SUM(total_amount) AS revenue
FROM sales
GROUP BY order_date;
