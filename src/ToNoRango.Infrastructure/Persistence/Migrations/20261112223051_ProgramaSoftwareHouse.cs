using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToNoRango.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProgramaSoftwareHouse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PartnerFirmId",
                schema: "fiscal",
                table: "webhooks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PartnerFirmId",
                schema: "fiscal",
                table: "api_keys",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                schema: "fiscal",
                table: "accountant_links",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                schema: "fiscal",
                table: "accountant_firms",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PartnerFirmId",
                schema: "fiscal",
                table: "webhooks");

            migrationBuilder.DropColumn(
                name: "PartnerFirmId",
                schema: "fiscal",
                table: "api_keys");

            migrationBuilder.DropColumn(
                name: "Kind",
                schema: "fiscal",
                table: "accountant_links");

            migrationBuilder.DropColumn(
                name: "Kind",
                schema: "fiscal",
                table: "accountant_firms");
        }
    }
}
