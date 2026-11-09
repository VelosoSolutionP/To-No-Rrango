using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToNoRango.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FiscalNfce : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "fiscal");

            migrationBuilder.AddColumn<string>(
                name: "NfeXml",
                schema: "inventory",
                table: "purchase_entries",
                type: "text",
                maxLength: 512,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "documents",
                schema: "fiscal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Model = table.Column<int>(type: "integer", nullable: false),
                    Series = table.Column<int>(type: "integer", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Environment = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AccessKey = table.Column<string>(type: "character varying(44)", maxLength: 44, nullable: true),
                    Protocol = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Xml = table.Column<string>(type: "text", maxLength: 512, nullable: true),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    TotalInCents = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "product_data",
                schema: "fiscal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    MenuItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ncm = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Cfop = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    Cest = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    IcmsCst = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    IcmsRate = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    PisCofinsCst = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    PisRate = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    CofinsRate = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product_data", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "profiles",
                schema: "fiscal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    LegalName = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    TradeName = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Cnpj = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    StateRegistration = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    Street = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    District = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    City = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    State = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    PostalCode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    CityIbgeCode = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    Series = table.Column<int>(type: "integer", nullable: false),
                    NextNumber = table.Column<int>(type: "integer", nullable: false),
                    TaxRegime = table.Column<int>(type: "integer", nullable: true),
                    AccountantName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AccountantCpf = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: true),
                    AccountantCrc = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    AccountantEmail = table.Column<string>(type: "character varying(115)", maxLength: 115, nullable: true),
                    SpedProfile = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_profiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_documents_EstablishmentId_Model_Series_Number_Environment",
                schema: "fiscal",
                table: "documents",
                columns: new[] { "EstablishmentId", "Model", "Series", "Number", "Environment" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_documents_OrderId",
                schema: "fiscal",
                table: "documents",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_documents_TenantId",
                schema: "fiscal",
                table: "documents",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_product_data_MenuItemId",
                schema: "fiscal",
                table: "product_data",
                column: "MenuItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_data_TenantId",
                schema: "fiscal",
                table: "product_data",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_profiles_EstablishmentId",
                schema: "fiscal",
                table: "profiles",
                column: "EstablishmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_profiles_TenantId",
                schema: "fiscal",
                table: "profiles",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "documents",
                schema: "fiscal");

            migrationBuilder.DropTable(
                name: "product_data",
                schema: "fiscal");

            migrationBuilder.DropTable(
                name: "profiles",
                schema: "fiscal");

            migrationBuilder.DropColumn(
                name: "NfeXml",
                schema: "inventory",
                table: "purchase_entries");
        }
    }
}
