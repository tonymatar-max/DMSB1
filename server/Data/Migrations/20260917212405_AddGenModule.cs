using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusDocs.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGenModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    TemplateHtml = table.Column<string>(type: "TEXT", nullable: false),
                    SourceObjectType = table.Column<int>(type: "INTEGER", nullable: true),
                    SampleDataJson = table.Column<string>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GeneratedDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentTemplateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GeneratedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InputDataJson = table.Column<string>(type: "TEXT", nullable: false),
                    RenderedBlobHash = table.Column<string>(type: "TEXT", nullable: false),
                    ArchivedDocumentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EnvelopeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeneratedDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GeneratedDocuments_DocumentTemplates_DocumentTemplateId",
                        column: x => x.DocumentTemplateId,
                        principalTable: "DocumentTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GeneratedDocuments_DocumentTemplateId",
                table: "GeneratedDocuments",
                column: "DocumentTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_GeneratedDocuments_TenantId_DocumentTemplateId",
                table: "GeneratedDocuments",
                columns: new[] { "TenantId", "DocumentTemplateId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GeneratedDocuments");

            migrationBuilder.DropTable(
                name: "DocumentTemplates");
        }
    }
}
