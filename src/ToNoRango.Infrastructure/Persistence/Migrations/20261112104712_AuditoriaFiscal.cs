using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToNoRango.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditoriaFiscal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_entries",
                schema: "fiscal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Actor = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Action = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Target = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Period = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    Details = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    StatusCode = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_entries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_entries_EstablishmentId_At",
                schema: "fiscal",
                table: "audit_entries",
                columns: new[] { "EstablishmentId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_entries_EstablishmentId_Category_Period",
                schema: "fiscal",
                table: "audit_entries",
                columns: new[] { "EstablishmentId", "Category", "Period" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_entries_TenantId",
                schema: "fiscal",
                table: "audit_entries",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_entries",
                schema: "fiscal");
        }
    }
}
