using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToNoRango.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ModulesOperacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_contracts_TenantId",
                schema: "contracts",
                table: "contracts");

            migrationBuilder.EnsureSchema(
                name: "messaging");

            migrationBuilder.EnsureSchema(
                name: "parties");

            migrationBuilder.EnsureSchema(
                name: "delivery");

            migrationBuilder.EnsureSchema(
                name: "finance");

            migrationBuilder.EnsureSchema(
                name: "zest");

            migrationBuilder.EnsureSchema(
                name: "payments");

            migrationBuilder.EnsureSchema(
                name: "inventory");

            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.AddColumn<string>(
                name: "CustomerPhoneKey",
                schema: "orders",
                table: "orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PaidAt",
                schema: "orders",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StockDeductedAt",
                schema: "orders",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "orders",
                table: "orders",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<int>(
                name: "MaxOrdersPerMonth",
                schema: "contracts",
                table: "contracts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxUsers",
                schema: "contracts",
                table: "contracts",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE orders.orders AS target
                SET "CustomerPhoneKey" = CASE WHEN length(source.digits) < 10 THEN source.digits ELSE left(source.digits, 2) || right(source.digits, 8) END
                FROM (
                    SELECT "Id", CASE WHEN length(raw) IN (12, 13) AND left(raw, 2) = '55' THEN substr(raw, 3) ELSE raw END AS digits
                    FROM (SELECT "Id", regexp_replace("CustomerPhone", '\D', '', 'g') AS raw FROM orders.orders) AS cleaned
                ) AS source
                WHERE source."Id" = target."Id";
                """);

            migrationBuilder.CreateTable(
                name: "conversation_messages",
                schema: "messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    Author = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversation_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "conversations",
                schema: "messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PhoneKey = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CustomerName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    LastMessage = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LastMessageAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    BotPausedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "customers",
                schema: "parties",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PhoneKey = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Document = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OrdersCount = table.Column<int>(type: "integer", nullable: false),
                    TotalOrderedInCents = table.Column<long>(type: "bigint", nullable: false),
                    LastOrderAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "delivery_zones",
                schema: "delivery",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    District = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    DistrictKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    FeeInCents = table.Column<long>(type: "bigint", nullable: false),
                    EstimatedMinutes = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_zones", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "financial_entries",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Category = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AmountInCents = table.Column<long>(type: "bigint", nullable: false),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    SettledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_entries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_entries",
                schema: "zest",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Topic = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Answer = table.Column<string>(type: "character varying(1500)", maxLength: 1500, nullable: false),
                    Keywords = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_entries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "outbound_messages",
                schema: "messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbound_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "payment_settings",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    EncryptedAccessToken = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    EncryptedWebhookSecret = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PublicKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsPixEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsCardEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ProviderPaymentId = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StatusDetail = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    AmountInCents = table.Column<long>(type: "bigint", nullable: false),
                    PixQrCode = table.Column<string>(type: "text", maxLength: 512, nullable: true),
                    PixQrCodeBase64 = table.Column<string>(type: "text", maxLength: 512, nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastCheckedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "purchase_entries",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    InvoiceNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    AccessKey = table.Column<string>(type: "character varying(44)", maxLength: 44, nullable: true),
                    IssuedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    TotalInCents = table.Column<long>(type: "bigint", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_entries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "recipe_lines",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    MenuItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    StockItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recipe_lines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "stock_items",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Unit = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    QuantityOnHand = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    MinimumQuantity = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    AverageCostInCents = table.Column<long>(type: "bigint", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_items", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "stock_movements",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    StockItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    UnitCostInCents = table.Column<long>(type: "bigint", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    PurchaseEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_movements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "suppliers",
                schema: "parties",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    TradeName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    Document = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    Phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_suppliers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    FailedLoginAttempts = table.Column<int>(type: "integer", nullable: false),
                    LockedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "whatsapp_settings",
                schema: "messaging",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstanceName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    EncryptedApiKey = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    EncryptedWebhookToken = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    NotifyOrderStatus = table.Column<bool>(type: "boolean", nullable: false),
                    AutoReply = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_whatsapp_settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "purchase_entry_lines",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    StockItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    UnitCostInCents = table.Column<long>(type: "bigint", nullable: false),
                    TotalInCents = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_entry_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_purchase_entry_lines_purchase_entries_PurchaseEntryId",
                        column: x => x.PurchaseEntryId,
                        principalSchema: "inventory",
                        principalTable: "purchase_entries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_orders_EstablishmentId_CustomerPhoneKey",
                schema: "orders",
                table: "orders",
                columns: new[] { "EstablishmentId", "CustomerPhoneKey" });

            migrationBuilder.CreateIndex(
                name: "IX_contracts_TenantId",
                schema: "contracts",
                table: "contracts",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_conversation_messages_ConversationId_CreatedAt",
                schema: "messaging",
                table: "conversation_messages",
                columns: new[] { "ConversationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_conversation_messages_EstablishmentId_ExternalId",
                schema: "messaging",
                table: "conversation_messages",
                columns: new[] { "EstablishmentId", "ExternalId" },
                unique: true,
                filter: "\"ExternalId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_conversation_messages_TenantId",
                schema: "messaging",
                table: "conversation_messages",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_conversations_EstablishmentId_PhoneKey",
                schema: "messaging",
                table: "conversations",
                columns: new[] { "EstablishmentId", "PhoneKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_conversations_TenantId",
                schema: "messaging",
                table: "conversations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_customers_EstablishmentId_PhoneKey",
                schema: "parties",
                table: "customers",
                columns: new[] { "EstablishmentId", "PhoneKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customers_TenantId",
                schema: "parties",
                table: "customers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_delivery_zones_EstablishmentId_DistrictKey",
                schema: "delivery",
                table: "delivery_zones",
                columns: new[] { "EstablishmentId", "DistrictKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_delivery_zones_TenantId",
                schema: "delivery",
                table: "delivery_zones",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_financial_entries_EstablishmentId_DueOn",
                schema: "finance",
                table: "financial_entries",
                columns: new[] { "EstablishmentId", "DueOn" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_entries_Source_SourceId",
                schema: "finance",
                table: "financial_entries",
                columns: new[] { "Source", "SourceId" },
                unique: true,
                filter: "\"SourceId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_financial_entries_TenantId",
                schema: "finance",
                table: "financial_entries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_entries_EstablishmentId",
                schema: "zest",
                table: "knowledge_entries",
                column: "EstablishmentId");

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_entries_TenantId",
                schema: "zest",
                table: "knowledge_entries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_outbound_messages_Status_NextAttemptAt",
                schema: "messaging",
                table: "outbound_messages",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_outbound_messages_TenantId",
                schema: "messaging",
                table: "outbound_messages",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_payment_settings_EstablishmentId",
                schema: "payments",
                table: "payment_settings",
                column: "EstablishmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_settings_TenantId",
                schema: "payments",
                table: "payment_settings",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_payments_EstablishmentId_ProviderPaymentId",
                schema: "payments",
                table: "payments",
                columns: new[] { "EstablishmentId", "ProviderPaymentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payments_OrderId",
                schema: "payments",
                table: "payments",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_payments_TenantId",
                schema: "payments",
                table: "payments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_entries_EstablishmentId_AccessKey",
                schema: "inventory",
                table: "purchase_entries",
                columns: new[] { "EstablishmentId", "AccessKey" },
                unique: true,
                filter: "\"AccessKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_entries_TenantId",
                schema: "inventory",
                table: "purchase_entries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_entry_lines_PurchaseEntryId",
                schema: "inventory",
                table: "purchase_entry_lines",
                column: "PurchaseEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_entry_lines_TenantId",
                schema: "inventory",
                table: "purchase_entry_lines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_recipe_lines_MenuItemId_StockItemId",
                schema: "inventory",
                table: "recipe_lines",
                columns: new[] { "MenuItemId", "StockItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_recipe_lines_TenantId",
                schema: "inventory",
                table: "recipe_lines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_items_EstablishmentId",
                schema: "inventory",
                table: "stock_items",
                column: "EstablishmentId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_items_TenantId",
                schema: "inventory",
                table: "stock_items",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_EstablishmentId_CreatedAt",
                schema: "inventory",
                table: "stock_movements",
                columns: new[] { "EstablishmentId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_OrderId",
                schema: "inventory",
                table: "stock_movements",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_TenantId",
                schema: "inventory",
                table: "stock_movements",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_suppliers_EstablishmentId_Document",
                schema: "parties",
                table: "suppliers",
                columns: new[] { "EstablishmentId", "Document" });

            migrationBuilder.CreateIndex(
                name: "IX_suppliers_TenantId",
                schema: "parties",
                table: "suppliers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_users_Email",
                schema: "identity",
                table: "users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_TenantId",
                schema: "identity",
                table: "users",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_whatsapp_settings_EstablishmentId",
                schema: "messaging",
                table: "whatsapp_settings",
                column: "EstablishmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_whatsapp_settings_TenantId",
                schema: "messaging",
                table: "whatsapp_settings",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conversation_messages",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "conversations",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "customers",
                schema: "parties");

            migrationBuilder.DropTable(
                name: "delivery_zones",
                schema: "delivery");

            migrationBuilder.DropTable(
                name: "financial_entries",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "knowledge_entries",
                schema: "zest");

            migrationBuilder.DropTable(
                name: "outbound_messages",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "payment_settings",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "payments",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "purchase_entry_lines",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "recipe_lines",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_items",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_movements",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "suppliers",
                schema: "parties");

            migrationBuilder.DropTable(
                name: "users",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "whatsapp_settings",
                schema: "messaging");

            migrationBuilder.DropTable(
                name: "purchase_entries",
                schema: "inventory");

            migrationBuilder.DropIndex(
                name: "IX_orders_EstablishmentId_CustomerPhoneKey",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "IX_contracts_TenantId",
                schema: "contracts",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "CustomerPhoneKey",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "PaidAt",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "StockDeductedAt",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "MaxOrdersPerMonth",
                schema: "contracts",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "MaxUsers",
                schema: "contracts",
                table: "contracts");

            migrationBuilder.CreateIndex(
                name: "IX_contracts_TenantId",
                schema: "contracts",
                table: "contracts",
                column: "TenantId");
        }
    }
}
