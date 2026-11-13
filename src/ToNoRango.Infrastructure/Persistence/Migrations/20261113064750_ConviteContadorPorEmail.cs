using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToNoRango.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConviteContadorPorEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InviteEmailError",
                schema: "fiscal",
                table: "accountant_links",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "InviteEmailedAt",
                schema: "fiscal",
                table: "accountant_links",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InviteEmailError",
                schema: "fiscal",
                table: "accountant_links");

            migrationBuilder.DropColumn(
                name: "InviteEmailedAt",
                schema: "fiscal",
                table: "accountant_links");
        }
    }
}
