using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToNoRango.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ZestHotelCerebro : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HotelBotId",
                schema: "messaging",
                table: "whatsapp_settings",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HotelBotName",
                schema: "messaging",
                table: "whatsapp_settings",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HotelBotPrompt",
                schema: "messaging",
                table: "whatsapp_settings",
                type: "text",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HotelBotId",
                schema: "messaging",
                table: "whatsapp_settings");

            migrationBuilder.DropColumn(
                name: "HotelBotName",
                schema: "messaging",
                table: "whatsapp_settings");

            migrationBuilder.DropColumn(
                name: "HotelBotPrompt",
                schema: "messaging",
                table: "whatsapp_settings");
        }
    }
}
