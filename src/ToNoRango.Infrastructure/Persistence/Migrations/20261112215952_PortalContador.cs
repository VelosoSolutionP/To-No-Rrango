using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ToNoRango.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PortalContador : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accountant_firms",
                schema: "fiscal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Document = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    Crc = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    IsPartner = table.Column<bool>(type: "boolean", nullable: false),
                    PartnerSince = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PartnerRequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accountant_firms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "accountant_links",
                schema: "fiscal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    InvitedEmail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CodeExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FirmId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    InvitedBy = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accountant_links", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "accountant_messages",
                schema: "fiscal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EstablishmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromAccountant = table.Column<bool>(type: "boolean", nullable: false),
                    Author = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    IsRequest = table.Column<bool>(type: "boolean", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResolvedBy = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    ReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accountant_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "accountant_users",
                schema: "fiscal",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    FailedLoginAttempts = table.Column<int>(type: "integer", nullable: false),
                    LockedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accountant_users", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_accountant_firms_Document",
                schema: "fiscal",
                table: "accountant_firms",
                column: "Document");

            migrationBuilder.CreateIndex(
                name: "IX_accountant_links_CodeHash",
                schema: "fiscal",
                table: "accountant_links",
                column: "CodeHash",
                unique: true,
                filter: "\"CodeHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_accountant_links_EstablishmentId_Status",
                schema: "fiscal",
                table: "accountant_links",
                columns: new[] { "EstablishmentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_accountant_links_FirmId_Status",
                schema: "fiscal",
                table: "accountant_links",
                columns: new[] { "FirmId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_accountant_links_TenantId",
                schema: "fiscal",
                table: "accountant_links",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_accountant_messages_EstablishmentId_CreatedAt",
                schema: "fiscal",
                table: "accountant_messages",
                columns: new[] { "EstablishmentId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_accountant_messages_LinkId",
                schema: "fiscal",
                table: "accountant_messages",
                column: "LinkId");

            migrationBuilder.CreateIndex(
                name: "IX_accountant_messages_TenantId",
                schema: "fiscal",
                table: "accountant_messages",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_accountant_users_Email",
                schema: "fiscal",
                table: "accountant_users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accountant_users_FirmId",
                schema: "fiscal",
                table: "accountant_users",
                column: "FirmId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accountant_firms",
                schema: "fiscal");

            migrationBuilder.DropTable(
                name: "accountant_links",
                schema: "fiscal");

            migrationBuilder.DropTable(
                name: "accountant_messages",
                schema: "fiscal");

            migrationBuilder.DropTable(
                name: "accountant_users",
                schema: "fiscal");
        }
    }
}
