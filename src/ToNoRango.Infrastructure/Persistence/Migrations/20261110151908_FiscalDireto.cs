using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToNoRango.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FiscalDireto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_documents_OrderId",
                schema: "fiscal",
                table: "documents");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CertificateExpiresAt",
                schema: "fiscal",
                table: "profiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CertificateSubject",
                schema: "fiscal",
                table: "profiles",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CscId",
                schema: "fiscal",
                table: "profiles",
                type: "character varying(6)",
                maxLength: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EncryptedCertificate",
                schema: "fiscal",
                table: "profiles",
                type: "text",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EncryptedCertificatePassword",
                schema: "fiscal",
                table: "profiles",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EncryptedCscToken",
                schema: "fiscal",
                table: "profiles",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Environment",
                schema: "fiscal",
                table: "profiles",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<int>(
                name: "NfeNextNumber",
                schema: "fiscal",
                table: "profiles",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "NfeSeries",
                schema: "fiscal",
                table: "profiles",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "CancelProtocol",
                schema: "fiscal",
                table: "documents",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                schema: "fiscal",
                table: "documents",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAt",
                schema: "fiscal",
                table: "documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ContingencyAt",
                schema: "fiscal",
                table: "documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecipientDocument",
                schema: "fiscal",
                table: "documents",
                type: "character varying(14)",
                maxLength: 14,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecipientName",
                schema: "fiscal",
                table: "documents",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "number_gaps",
                schema: "fiscal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Model = table.Column<int>(type: "integer", nullable: false),
                    Series = table.Column<int>(type: "integer", nullable: false),
                    FromNumber = table.Column<int>(type: "integer", nullable: false),
                    ToNumber = table.Column<int>(type: "integer", nullable: false),
                    Environment = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Protocol = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Xml = table.Column<string>(type: "text", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_number_gaps", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_documents_OrderId_Model",
                schema: "fiscal",
                table: "documents",
                columns: new[] { "OrderId", "Model" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_number_gaps_EstablishmentId_Model_Series",
                schema: "fiscal",
                table: "number_gaps",
                columns: new[] { "EstablishmentId", "Model", "Series" });

            migrationBuilder.CreateIndex(
                name: "IX_number_gaps_TenantId",
                schema: "fiscal",
                table: "number_gaps",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "number_gaps",
                schema: "fiscal");

            migrationBuilder.DropIndex(
                name: "IX_documents_OrderId_Model",
                schema: "fiscal",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "CertificateExpiresAt",
                schema: "fiscal",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "CertificateSubject",
                schema: "fiscal",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "CscId",
                schema: "fiscal",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "EncryptedCertificate",
                schema: "fiscal",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "EncryptedCertificatePassword",
                schema: "fiscal",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "EncryptedCscToken",
                schema: "fiscal",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "Environment",
                schema: "fiscal",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "NfeNextNumber",
                schema: "fiscal",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "NfeSeries",
                schema: "fiscal",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "CancelProtocol",
                schema: "fiscal",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "CancelReason",
                schema: "fiscal",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                schema: "fiscal",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "ContingencyAt",
                schema: "fiscal",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "RecipientDocument",
                schema: "fiscal",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "RecipientName",
                schema: "fiscal",
                table: "documents");

            migrationBuilder.CreateIndex(
                name: "IX_documents_OrderId",
                schema: "fiscal",
                table: "documents",
                column: "OrderId",
                unique: true);
        }
    }
}
