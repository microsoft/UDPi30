-- Process layer: stored procedure that refreshes the gold order summary.
CREATE OR ALTER PROCEDURE dbo.usp_refresh_order_summary
AS
BEGIN
    TRUNCATE TABLE dbo.order_summary;

    INSERT INTO dbo.order_summary (order_date, total_orders, revenue)
    SELECT order_date, COUNT(*), SUM(total_amount)
    FROM dbo.sales
    GROUP BY order_date;
END;
