-- Materialized lake view over the curated lakehouse.
CREATE MATERIALIZED LAKE VIEW IF NOT EXISTS curated_daily_summary AS
SELECT
    CAST(event_time AS DATE) AS event_date,
    COUNT(*)                 AS record_count
FROM curated_events
GROUP BY CAST(event_time AS DATE);
