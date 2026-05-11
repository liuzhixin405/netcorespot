using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CryptoSpot.Persistence.Migrations
{
    public partial class AddPerformanceIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Orders table
            migrationBuilder.CreateIndex(
                name: "IX_Orders_TradingPairId_Status_Side_Price",
                table: "Orders",
                columns: new[] { "TradingPairId", "Status", "Side", "Price" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_UserId_CreatedAt",
                table: "Orders",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Status_CreatedAt",
                table: "Orders",
                columns: new[] { "Status", "CreatedAt" });

            // Trades table
            migrationBuilder.CreateIndex(
                name: "IX_Trades_TradingPairId_ExecutedAt",
                table: "Trades",
                columns: new[] { "TradingPairId", "ExecutedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Trades_BuyerId_ExecutedAt",
                table: "Trades",
                columns: new[] { "BuyerId", "ExecutedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Trades_SellerId_ExecutedAt",
                table: "Trades",
                columns: new[] { "SellerId", "ExecutedAt" });

            // KLineData: change existing index to unique
            migrationBuilder.DropIndex(
                name: "IX_KLineData_TradingPairId_TimeFrame_OpenTime",
                table: "KLineData");

            migrationBuilder.CreateIndex(
                name: "IX_KLineData_TradingPairId_TimeFrame_OpenTime",
                table: "KLineData",
                columns: new[] { "TradingPairId", "TimeFrame", "OpenTime" },
                unique: true);
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
