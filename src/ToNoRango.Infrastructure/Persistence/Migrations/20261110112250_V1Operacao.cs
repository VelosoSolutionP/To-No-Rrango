using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToNoRango.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class V1Operacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "pdv");

            migrationBuilder.EnsureSchema(
                name: "feedback");

            migrationBuilder.AlterColumn<string>(
                name: "Text",
                schema: "messaging",
                table: "outbound_messages",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000);

            migrationBuilder.AddColumn<string>(
                name: "AudioBase64",
                schema: "messaging",
                table: "outbound_messages",
                type: "text",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AudioMimeType",
                schema: "messaging",
                table: "outbound_messages",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CourierAssignedAt",
                schema: "orders",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CourierId",
                schema: "orders",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CourierName",
                schema: "orders",
                table: "orders",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryReference",
                schema: "orders",
                table: "orders",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DiscountInCents",
                schema: "orders",
                table: "orders",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "DiscountReason",
                schema: "orders",
                table: "orders",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ScheduledFor",
                schema: "orders",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TableLabel",
                schema: "orders",
                table: "orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PrepStatus",
                schema: "orders",
                table: "order_items",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PrepUpdatedAt",
                schema: "orders",
                table: "order_items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Station",
                schema: "orders",
                table: "order_items",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<long>(
                name: "CompareAtPriceInCents",
                schema: "catalog",
                table: "menu_items",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Station",
                schema: "catalog",
                table: "menu_categories",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "AcceptsOrdersWhenClosed",
                schema: "establishments",
                table: "establishments",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "OpeningHours",
                schema: "establishments",
                table: "establishments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MediaFileId",
                schema: "messaging",
                table: "conversation_messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cash_sessions",
                schema: "pdv",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    OpenedBy = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    OpenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OpeningAmountInCents = table.Column<long>(type: "bigint", nullable: false),
                    ClosedBy = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CountedCashInCents = table.Column<long>(type: "bigint", nullable: true),
                    ExpectedCashInCents = table.Column<long>(type: "bigint", nullable: true),
                    ClosingNotes = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_sessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_gaps",
                schema: "zest",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Question = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    QuestionKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Times = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    FirstAskedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastAskedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_gaps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "menu_combos",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    MenuItemId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_menu_combos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "order_ratings",
                schema: "feedback",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ServiceScore = table.Column<int>(type: "integer", nullable: false),
                    ProductScore = table.Column<int>(type: "integer", nullable: false),
                    OverallScore = table.Column<int>(type: "integer", nullable: false),
                    StoreComment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CourierId = table.Column<Guid>(type: "uuid", nullable: true),
                    CourierName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CourierServiceScore = table.Column<int>(type: "integer", nullable: true),
                    CourierPunctualityScore = table.Column<int>(type: "integer", nullable: true),
                    CourierCareScore = table.Column<int>(type: "integer", nullable: true),
                    CourierComment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_ratings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "cash_movements",
                schema: "pdv",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CashSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    AmountInCents = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    OperatorName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_movements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cash_movements_cash_sessions_CashSessionId",
                        column: x => x.CashSessionId,
                        principalSchema: "pdv",
                        principalTable: "cash_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "menu_combo_lines",
                schema: "catalog",
                columns: table => new
                {
                    MenuComboId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConstituentMenuItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_menu_combo_lines", x => new { x.MenuComboId, x.ConstituentMenuItemId });
                    table.ForeignKey(
                        name: "FK_menu_combo_lines_menu_combos_MenuComboId",
                        column: x => x.MenuComboId,
                        principalSchema: "catalog",
                        principalTable: "menu_combos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cash_movements_CashSessionId",
                schema: "pdv",
                table: "cash_movements",
                column: "CashSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_cash_movements_OrderId",
                schema: "pdv",
                table: "cash_movements",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_cash_movements_TenantId",
                schema: "pdv",
                table: "cash_movements",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_cash_sessions_EstablishmentId",
                schema: "pdv",
                table: "cash_sessions",
                column: "EstablishmentId",
                unique: true,
                filter: "\"Status\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_cash_sessions_EstablishmentId_OpenedAt",
                schema: "pdv",
                table: "cash_sessions",
                columns: new[] { "EstablishmentId", "OpenedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_cash_sessions_TenantId",
                schema: "pdv",
                table: "cash_sessions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_gaps_EstablishmentId_QuestionKey",
                schema: "zest",
                table: "knowledge_gaps",
                columns: new[] { "EstablishmentId", "QuestionKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_gaps_TenantId",
                schema: "zest",
                table: "knowledge_gaps",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_menu_combo_lines_TenantId",
                schema: "catalog",
                table: "menu_combo_lines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_menu_combos_MenuItemId",
                schema: "catalog",
                table: "menu_combos",
                column: "MenuItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_menu_combos_TenantId",
                schema: "catalog",
                table: "menu_combos",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_order_ratings_CourierId",
                schema: "feedback",
                table: "order_ratings",
                column: "CourierId");

            migrationBuilder.CreateIndex(
                name: "IX_order_ratings_EstablishmentId_CreatedAt",
                schema: "feedback",
                table: "order_ratings",
                columns: new[] { "EstablishmentId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_order_ratings_OrderId",
                schema: "feedback",
                table: "order_ratings",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_ratings_TenantId",
                schema: "feedback",
                table: "order_ratings",
                column: "TenantId");

            // Orders that already moved keep their items in step, so old orders do not show items "received".
            migrationBuilder.Sql("""
                UPDATE orders.order_items AS item
                SET "PrepStatus" = CASE o."Status" WHEN 6 THEN 6 WHEN 5 THEN 5 WHEN 4 THEN 5 WHEN 3 THEN 3 WHEN 2 THEN 2 ELSE 1 END
                FROM orders.orders AS o
                WHERE o."Id" = item."OrderId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cash_movements",
                schema: "pdv");

            migrationBuilder.DropTable(
                name: "knowledge_gaps",
                schema: "zest");

            migrationBuilder.DropTable(
                name: "menu_combo_lines",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "order_ratings",
                schema: "feedback");

            migrationBuilder.DropTable(
                name: "cash_sessions",
                schema: "pdv");

            migrationBuilder.DropTable(
                name: "menu_combos",
                schema: "catalog");

            migrationBuilder.DropColumn(
                name: "AudioBase64",
                schema: "messaging",
                table: "outbound_messages");

            migrationBuilder.DropColumn(
                name: "AudioMimeType",
                schema: "messaging",
                table: "outbound_messages");

            migrationBuilder.DropColumn(
                name: "CourierAssignedAt",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "CourierId",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "CourierName",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "DeliveryReference",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "DiscountInCents",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "DiscountReason",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "ScheduledFor",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "TableLabel",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "PrepStatus",
                schema: "orders",
                table: "order_items");

            migrationBuilder.DropColumn(
                name: "PrepUpdatedAt",
                schema: "orders",
                table: "order_items");

            migrationBuilder.DropColumn(
                name: "Station",
                schema: "orders",
                table: "order_items");

            migrationBuilder.DropColumn(
                name: "CompareAtPriceInCents",
                schema: "catalog",
                table: "menu_items");

            migrationBuilder.DropColumn(
                name: "Station",
                schema: "catalog",
                table: "menu_categories");

            migrationBuilder.DropColumn(
                name: "AcceptsOrdersWhenClosed",
                schema: "establishments",
                table: "establishments");

            migrationBuilder.DropColumn(
                name: "OpeningHours",
                schema: "establishments",
                table: "establishments");

            migrationBuilder.DropColumn(
                name: "MediaFileId",
                schema: "messaging",
                table: "conversation_messages");

            migrationBuilder.AlterColumn<string>(
                name: "Text",
                schema: "messaging",
                table: "outbound_messages",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);
        }
    }
}
