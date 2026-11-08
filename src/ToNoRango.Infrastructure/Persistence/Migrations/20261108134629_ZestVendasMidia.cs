using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToNoRango.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ZestVendasMidia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "media");

            migrationBuilder.AddColumn<string>(
                name: "ImageBase64",
                schema: "messaging",
                table: "outbound_messages",
                type: "text",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Channel",
                schema: "orders",
                table: "orders",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "SalesState",
                schema: "messaging",
                table: "conversations",
                type: "text",
                maxLength: 512,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "media_files",
                schema: "media",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Data = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_files", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_media_files_TenantId",
                schema: "media",
                table: "media_files",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "media_files",
                schema: "media");

            migrationBuilder.DropColumn(
                name: "ImageBase64",
                schema: "messaging",
                table: "outbound_messages");

            migrationBuilder.DropColumn(
                name: "Channel",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "SalesState",
                schema: "messaging",
                table: "conversations");
        }
    }
}
