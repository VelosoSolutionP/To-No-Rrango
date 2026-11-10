using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToNoRango.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MesaGarcom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WaiterName",
                schema: "orders",
                table: "orders",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_orders_EstablishmentId_TableLabel",
                schema: "orders",
                table: "orders",
                columns: new[] { "EstablishmentId", "TableLabel" },
                filter: "\"TableLabel\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_orders_EstablishmentId_TableLabel",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "WaiterName",
                schema: "orders",
                table: "orders");
        }
    }
}
