-- Materialized lake view: daily per-device telemetry rollup.
-- Refreshed on the schedule declared in udp.yml (refresh_cron).
CREATE MATERIALIZED LAKE VIEW IF NOT EXISTS device_daily_summary AS
SELECT
    device_id,
    CAST(event_time AS DATE)      AS event_date,
    COUNT(*)                      AS reading_count,
    AVG(temperature)              AS avg_temperature,
    MAX(temperature)              AS max_temperature,
    MIN(temperature)              AS min_temperature,
    AVG(humidity)                 AS avg_humidity
FROM telemetry_readings
GROUP BY device_id, CAST(event_time AS DATE);
