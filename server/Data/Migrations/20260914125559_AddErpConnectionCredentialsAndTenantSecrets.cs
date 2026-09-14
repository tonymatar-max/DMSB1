using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusDocs.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddErpConnectionCredentialsAndTenantSecrets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "ErpConnections",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "UserName",
                table: "ErpConnections",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "TenantSecrets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    EncryptedValue = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSecrets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenantSecrets_TenantId_Key",
                table: "TenantSecrets",
                columns: new[] { "TenantId", "Key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantSecrets");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "ErpConnections");

            migrationBuilder.DropColumn(
                name: "UserName",
                table: "ErpConnections");
        }
    }
}
