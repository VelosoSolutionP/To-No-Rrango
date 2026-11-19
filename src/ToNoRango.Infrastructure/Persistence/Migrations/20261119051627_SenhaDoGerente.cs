using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToNoRango.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SenhaDoGerente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ManagerPinHash",
                schema: "establishments",
                table: "establishments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ManagerPinHash",
                schema: "establishments",
                table: "establishments");
        }
    }
}
