-- Operational SQL database schema created on deploy.
CREATE TABLE dbo.app_settings (
    setting_key   NVARCHAR(100) NOT NULL PRIMARY KEY,
    setting_value NVARCHAR(400) NULL
);
