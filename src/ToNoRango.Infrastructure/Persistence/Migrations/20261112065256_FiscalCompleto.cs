using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToNoRango.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FiscalCompleto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "LastNsu",
                schema: "fiscal",
                table: "profiles",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextDistributionAt",
                schema: "fiscal",
                table: "profiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CorrectedAt",
                schema: "fiscal",
                table: "documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorrectionProtocol",
                schema: "fiscal",
                table: "documents",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CorrectionSequence",
                schema: "fiscal",
                table: "documents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CorrectionText",
                schema: "fiscal",
                table: "documents",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Purpose",
                schema: "fiscal",
                table: "documents",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "ReferencedKey",
                schema: "fiscal",
                table: "documents",
                type: "character varying(44)",
                maxLength: 44,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "received_notes",
                schema: "fiscal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nsu = table.Column<long>(type: "bigint", nullable: false),
                    AccessKey = table.Column<string>(type: "character varying(44)", maxLength: 44, nullable: false),
                    IssuerDocument = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    IssuerName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TotalInCents = table.Column<long>(type: "bigint", nullable: false),
                    Situation = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Xml = table.Column<string>(type: "text", maxLength: 512, nullable: true),
                    AcknowledgedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastEvent = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_received_notes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_received_notes_EstablishmentId_AccessKey",
                schema: "fiscal",
                table: "received_notes",
                columns: new[] { "EstablishmentId", "AccessKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_received_notes_TenantId",
                schema: "fiscal",
                table: "received_notes",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "received_notes",
                schema: "fiscal");

            migrationBuilder.DropColumn(
                name: "LastNsu",
                schema: "fiscal",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "NextDistributionAt",
                schema: "fiscal",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "CorrectedAt",
                schema: "fiscal",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "CorrectionProtocol",
                schema: "fiscal",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "CorrectionSequence",
                schema: "fiscal",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "CorrectionText",
                schema: "fiscal",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "Purpose",
                schema: "fiscal",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "ReferencedKey",
                schema: "fiscal",
                table: "documents");
        }
    }
}
