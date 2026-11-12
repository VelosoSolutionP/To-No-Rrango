using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToNoRango.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NfseNacional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MunicipalRegistration",
                schema: "fiscal",
                table: "profiles",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "NextServiceNumber",
                schema: "fiscal",
                table: "profiles",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<int>(
                name: "ServiceSeries",
                schema: "fiscal",
                table: "profiles",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "service_invoices",
                schema: "fiscal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Series = table.Column<int>(type: "integer", nullable: false),
                    Number = table.Column<long>(type: "bigint", nullable: false),
                    Environment = table.Column<int>(type: "integer", nullable: false),
                    IsSimulation = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DpsId = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    AccessKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Xml = table.Column<string>(type: "text", maxLength: 512, nullable: true),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    TakerDocument = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    TakerName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    ServiceCode = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    AmountInCents = table.Column<long>(type: "bigint", nullable: false),
                    IssRate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    IssWithheld = table.Column<bool>(type: "boolean", nullable: false),
                    Competence = table.Column<DateOnly>(type: "date", nullable: false),
                    CancelReason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_invoices", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_service_invoices_EstablishmentId_CreatedAt",
                schema: "fiscal",
                table: "service_invoices",
                columns: new[] { "EstablishmentId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_service_invoices_EstablishmentId_IsSimulation_Environment_S~",
                schema: "fiscal",
                table: "service_invoices",
                columns: new[] { "EstablishmentId", "IsSimulation", "Environment", "Series", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_service_invoices_EstablishmentId_IsSimulation_ExternalId",
                schema: "fiscal",
                table: "service_invoices",
                columns: new[] { "EstablishmentId", "IsSimulation", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_service_invoices_TenantId",
                schema: "fiscal",
                table: "service_invoices",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "service_invoices",
                schema: "fiscal");

            migrationBuilder.DropColumn(
                name: "MunicipalRegistration",
                schema: "fiscal",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "NextServiceNumber",
                schema: "fiscal",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "ServiceSeries",
                schema: "fiscal",
                table: "profiles");
        }
    }
}
