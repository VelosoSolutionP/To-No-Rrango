using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToNoRango.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CadastrosFinanceiro : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Barcode",
                schema: "catalog",
                table: "menu_items",
                type: "character varying(14)",
                maxLength: 14,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sku",
                schema: "catalog",
                table: "menu_items",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                schema: "catalog",
                table: "menu_items",
                type: "character varying(6)",
                maxLength: 6,
                nullable: false,
                defaultValue: "UN");

            migrationBuilder.AddColumn<string>(
                name: "Counterparty",
                schema: "finance",
                table: "financial_entries",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentNumber",
                schema: "finance",
                table: "financial_entries",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "SettledOn",
                schema: "finance",
                table: "financial_entries",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SettlementMethod",
                schema: "finance",
                table: "financial_entries",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                schema: "parties",
                table: "customers",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Barcode",
                schema: "catalog",
                table: "menu_items");

            migrationBuilder.DropColumn(
                name: "Sku",
                schema: "catalog",
                table: "menu_items");

            migrationBuilder.DropColumn(
                name: "Unit",
                schema: "catalog",
                table: "menu_items");

            migrationBuilder.DropColumn(
                name: "Counterparty",
                schema: "finance",
                table: "financial_entries");

            migrationBuilder.DropColumn(
                name: "DocumentNumber",
                schema: "finance",
                table: "financial_entries");

            migrationBuilder.DropColumn(
                name: "SettledOn",
                schema: "finance",
                table: "financial_entries");

            migrationBuilder.DropColumn(
                name: "SettlementMethod",
                schema: "finance",
                table: "financial_entries");

            migrationBuilder.DropColumn(
                name: "IsActive",
                schema: "parties",
                table: "customers");
        }
    }
}
