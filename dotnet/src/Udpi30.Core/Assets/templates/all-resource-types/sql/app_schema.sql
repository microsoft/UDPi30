-- SQL database schema — referenced by resources.sql_databases.app_db.
-- Runs against the operational SQL database after it is created.

CREATE TABLE IF NOT EXISTS dbo.app_events (
    id          BIGINT IDENTITY PRIMARY KEY,
    event_type  NVARCHAR(100) NOT NULL,
    payload     NVARCHAR(MAX),
    created_at  DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
