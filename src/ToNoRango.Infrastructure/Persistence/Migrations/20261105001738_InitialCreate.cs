using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToNoRango.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "contracts");

            migrationBuilder.EnsureSchema(
                name: "establishments");

            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.EnsureSchema(
                name: "orders");

            migrationBuilder.CreateTable(
                name: "contracts",
                schema: "contracts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpiresOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contracts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "establishments",
                schema: "establishments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LogoUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CoverUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    WhatsApp = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    AddressLine = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    City = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    State = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    PostalCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    TimeZone = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsPickupEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsDeliveryEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsDineInEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    DeliveryFeeInCents = table.Column<long>(type: "bigint", nullable: false),
                    MinimumOrderInCents = table.Column<long>(type: "bigint", nullable: false),
                    PrimaryColor = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    SecondaryColor = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_establishments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "menu_categories",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_menu_categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "menu_items",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ImageUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PriceInCents = table.Column<long>(type: "bigint", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsAvailable = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_menu_items", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "orders",
                schema: "orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CustomerName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CustomerPhone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    FulfillmentType = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PaymentMethod = table.Column<int>(type: "integer", nullable: false),
                    PaymentStatus = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DeliveryStreet = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DeliveryNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    DeliveryComplement = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DeliveryDistrict = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    DeliveryCity = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    DeliveryState = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    DeliveryPostalCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    SubtotalInCents = table.Column<long>(type: "bigint", nullable: false),
                    DeliveryFeeInCents = table.Column<long>(type: "bigint", nullable: false),
                    TotalInCents = table.Column<long>(type: "bigint", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReadyAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelReason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CancelledBy = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_orders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "contract_modules",
                schema: "contracts",
                columns: table => new
                {
                    code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ContractId = table.Column<Guid>(type: "uuid", nullable: false),
                    is_available = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contract_modules", x => new { x.ContractId, x.code });
                    table.ForeignKey(
                        name: "FK_contract_modules_contracts_ContractId",
                        column: x => x.ContractId,
                        principalSchema: "contracts",
                        principalTable: "contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "menu_option_groups",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    MenuItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    MinSelections = table.Column<int>(type: "integer", nullable: false),
                    MaxSelections = table.Column<int>(type: "integer", nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_menu_option_groups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_menu_option_groups_menu_items_MenuItemId",
                        column: x => x.MenuItemId,
                        principalSchema: "catalog",
                        principalTable: "menu_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_items",
                schema: "orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    MenuItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    UnitPriceInCents = table.Column<long>(type: "bigint", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TotalInCents = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_order_items_orders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "orders",
                        principalTable: "orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_status_history",
                schema: "orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<int>(type: "integer", nullable: true),
                    ToStatus = table.Column<int>(type: "integer", nullable: false),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ChangedBy = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_status_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_order_status_history_orders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "orders",
                        principalTable: "orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "menu_options",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    MenuOptionGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    PriceInCents = table.Column<long>(type: "bigint", nullable: false),
                    IsAvailable = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_menu_options", x => x.Id);
                    table.ForeignKey(
                        name: "FK_menu_options_menu_option_groups_MenuOptionGroupId",
                        column: x => x.MenuOptionGroupId,
                        principalSchema: "catalog",
                        principalTable: "menu_option_groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_item_options",
                schema: "orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    PriceInCents = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_item_options", x => x.Id);
                    table.ForeignKey(
                        name: "FK_order_item_options_order_items_OrderItemId",
                        column: x => x.OrderItemId,
                        principalSchema: "orders",
                        principalTable: "order_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_contracts_EstablishmentId",
                schema: "contracts",
                table: "contracts",
                column: "EstablishmentId");

            migrationBuilder.CreateIndex(
                name: "IX_contracts_TenantId",
                schema: "contracts",
                table: "contracts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_establishments_Slug",
                schema: "establishments",
                table: "establishments",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_establishments_TenantId",
                schema: "establishments",
                table: "establishments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_menu_categories_EstablishmentId_SortOrder",
                schema: "catalog",
                table: "menu_categories",
                columns: new[] { "EstablishmentId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_menu_categories_TenantId",
                schema: "catalog",
                table: "menu_categories",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_menu_items_EstablishmentId_CategoryId_SortOrder",
                schema: "catalog",
                table: "menu_items",
                columns: new[] { "EstablishmentId", "CategoryId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_menu_items_TenantId",
                schema: "catalog",
                table: "menu_items",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_menu_option_groups_MenuItemId_SortOrder",
                schema: "catalog",
                table: "menu_option_groups",
                columns: new[] { "MenuItemId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_menu_option_groups_TenantId",
                schema: "catalog",
                table: "menu_option_groups",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_menu_options_MenuOptionGroupId_SortOrder",
                schema: "catalog",
                table: "menu_options",
                columns: new[] { "MenuOptionGroupId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_menu_options_TenantId",
                schema: "catalog",
                table: "menu_options",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_order_item_options_OrderItemId",
                schema: "orders",
                table: "order_item_options",
                column: "OrderItemId");

            migrationBuilder.CreateIndex(
                name: "IX_order_item_options_TenantId",
                schema: "orders",
                table: "order_item_options",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_order_items_OrderId",
                schema: "orders",
                table: "order_items",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_order_items_TenantId",
                schema: "orders",
                table: "order_items",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_order_status_history_OrderId_ChangedAt",
                schema: "orders",
                table: "order_status_history",
                columns: new[] { "OrderId", "ChangedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_order_status_history_TenantId",
                schema: "orders",
                table: "order_status_history",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_orders_Code",
                schema: "orders",
                table: "orders",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_orders_EstablishmentId_CreatedAt",
                schema: "orders",
                table: "orders",
                columns: new[] { "EstablishmentId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_orders_EstablishmentId_IdempotencyKey",
                schema: "orders",
                table: "orders",
                columns: new[] { "EstablishmentId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_orders_TenantId",
                schema: "orders",
                table: "orders",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contract_modules",
                schema: "contracts");

            migrationBuilder.DropTable(
                name: "establishments",
                schema: "establishments");

            migrationBuilder.DropTable(
                name: "menu_categories",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "menu_options",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "order_item_options",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "order_status_history",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "contracts",
                schema: "contracts");

            migrationBuilder.DropTable(
                name: "menu_option_groups",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "order_items",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "menu_items",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "orders",
                schema: "orders");
        }
    }
}
