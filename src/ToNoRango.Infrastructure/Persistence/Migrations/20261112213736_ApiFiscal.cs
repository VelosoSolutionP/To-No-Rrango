using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToNoRango.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ApiFiscal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSimulation",
                schema: "fiscal",
                table: "documents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "api_keys",
                schema: "fiscal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Prefix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsTest = table.Column<bool>(type: "boolean", nullable: false),
                    CanIssue = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_api_keys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "webhook_deliveries",
                schema: "fiscal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    WebhookId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Event = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Payload = table.Column<string>(type: "text", maxLength: 512, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastStatusCode = table.Column<int>(type: "integer", nullable: true),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_deliveries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "webhooks",
                schema: "fiscal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    EncryptedSecret = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Subscribed = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ScannedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhooks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_api_keys_EstablishmentId",
                schema: "fiscal",
                table: "api_keys",
                column: "EstablishmentId");

            migrationBuilder.CreateIndex(
                name: "IX_api_keys_Hash",
                schema: "fiscal",
                table: "api_keys",
                column: "Hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_api_keys_TenantId",
                schema: "fiscal",
                table: "api_keys",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_webhook_deliveries_EstablishmentId_CreatedAt",
                schema: "fiscal",
                table: "webhook_deliveries",
                columns: new[] { "EstablishmentId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_webhook_deliveries_Status_NextAttemptAt",
                schema: "fiscal",
                table: "webhook_deliveries",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_webhook_deliveries_TenantId",
                schema: "fiscal",
                table: "webhook_deliveries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_webhook_deliveries_WebhookId_EventKey",
                schema: "fiscal",
                table: "webhook_deliveries",
                columns: new[] { "WebhookId", "EventKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_webhooks_EstablishmentId_IsActive",
                schema: "fiscal",
                table: "webhooks",
                columns: new[] { "EstablishmentId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_webhooks_TenantId",
                schema: "fiscal",
                table: "webhooks",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_keys",
                schema: "fiscal");

            migrationBuilder.DropTable(
                name: "webhook_deliveries",
                schema: "fiscal");

            migrationBuilder.DropTable(
                name: "webhooks",
                schema: "fiscal");

            migrationBuilder.DropColumn(
                name: "IsSimulation",
                schema: "fiscal",
                table: "documents");
        }
    }
}
