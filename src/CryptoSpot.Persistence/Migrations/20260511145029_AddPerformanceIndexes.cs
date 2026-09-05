using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CryptoSpot.Persistence.Migrations
{
    public partial class AddPerformanceIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Orders table
            CreateIndexIfMissing(
                migrationBuilder,
                "IX_Orders_TradingPairId_Status_Side_Price",
                "Orders",
                "`TradingPairId`, `Status`, `Side`, `Price`");

            CreateIndexIfMissing(
                migrationBuilder,
                "IX_Orders_UserId_CreatedAt",
                "Orders",
                "`UserId`, `CreatedAt`");

            CreateIndexIfMissing(
                migrationBuilder,
                "IX_Orders_Status_CreatedAt",
                "Orders",
                "`Status`, `CreatedAt`");

            // Trades table
            CreateIndexIfMissing(
                migrationBuilder,
                "IX_Trades_TradingPairId_ExecutedAt",
                "Trades",
                "`TradingPairId`, `ExecutedAt`");

            CreateIndexIfMissing(
                migrationBuilder,
                "IX_Trades_BuyerId_ExecutedAt",
                "Trades",
                "`BuyerId`, `ExecutedAt`");

            CreateIndexIfMissing(
                migrationBuilder,
                "IX_Trades_SellerId_ExecutedAt",
                "Trades",
                "`SellerId`, `ExecutedAt`");

            CreateIndexIfMissing(
                migrationBuilder,
                "IX_KLineData_TradingPairId_TimeFrame_OpenTime",
                "KLineData",
                "`TradingPairId`, `TimeFrame`, `OpenTime`");
        }

        private static void CreateIndexIfMissing(
            MigrationBuilder migrationBuilder,
            string indexName,
            string tableName,
            string columns,
            bool unique = false)
        {
            var uniqueKeyword = unique ? "UNIQUE " : string.Empty;
            migrationBuilder.Sql($"""
                SET @indexExists := (
                    SELECT COUNT(1)
                    FROM INFORMATION_SCHEMA.STATISTICS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = '{tableName}'
                      AND INDEX_NAME = '{indexName}'
                );
                SET @sql := IF(
                    @indexExists = 0,
                    'CREATE {uniqueKeyword}INDEX `{indexName}` ON `{tableName}` ({columns})',
                    'SELECT 1'
                );
                PREPARE stmt FROM @sql;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // KLineData: revert unique index
            migrationBuilder.DropIndex(
                name: "IX_KLineData_TradingPairId_TimeFrame_OpenTime",
                table: "KLineData");

            migrationBuilder.CreateIndex(
                name: "IX_KLineData_TradingPairId_TimeFrame_OpenTime",
                table: "KLineData",
                columns: new[] { "TradingPairId", "TimeFrame", "OpenTime" });

            // Trades
            migrationBuilder.DropIndex(name: "IX_Trades_SellerId_ExecutedAt", table: "Trades");
            migrationBuilder.DropIndex(name: "IX_Trades_BuyerId_ExecutedAt", table: "Trades");
            migrationBuilder.DropIndex(name: "IX_Trades_TradingPairId_ExecutedAt", table: "Trades");

            // Orders
            migrationBuilder.DropIndex(name: "IX_Orders_Status_CreatedAt", table: "Orders");
            migrationBuilder.DropIndex(name: "IX_Orders_UserId_CreatedAt", table: "Orders");
            migrationBuilder.DropIndex(name: "IX_Orders_TradingPairId_Status_Side_Price", table: "Orders");
        }
    }
}
