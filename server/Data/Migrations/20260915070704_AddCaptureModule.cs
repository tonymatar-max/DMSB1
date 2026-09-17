using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusDocs.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCaptureModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IngestBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IngestSourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ItemCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngestBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IngestSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    HotFolderPath = table.Column<string>(type: "TEXT", nullable: true),
                    ImapHost = table.Column<string>(type: "TEXT", nullable: true),
                    ImapPort = table.Column<int>(type: "INTEGER", nullable: true),
                    ImapUseSsl = table.Column<bool>(type: "INTEGER", nullable: false),
                    ImapUsername = table.Column<string>(type: "TEXT", nullable: true),
                    ImapCredentialsRef = table.Column<string>(type: "TEXT", nullable: true),
                    ImapFolderName = table.Column<string>(type: "TEXT", nullable: true),
                    LastPolledAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngestSources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IngestItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IngestBatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OriginalFileName = table.Column<string>(type: "TEXT", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ArchivedDocumentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    WorkflowInstanceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngestItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IngestItems_IngestBatches_IngestBatchId",
                        column: x => x.IngestBatchId,
                        principalTable: "IngestBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExtractionResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IngestItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OcrProviderUsed = table.Column<string>(type: "TEXT", nullable: false),
                    RawText = table.Column<string>(type: "TEXT", nullable: false),
                    DocumentTypeGuess = table.Column<string>(type: "TEXT", nullable: true),
                    SupplierTaxId = table.Column<string>(type: "TEXT", nullable: true),
                    SupplierName = table.Column<string>(type: "TEXT", nullable: true),
                    InvoiceNumber = table.Column<string>(type: "TEXT", nullable: true),
                    InvoiceDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Currency = table.Column<string>(type: "TEXT", nullable: true),
                    NetAmount = table.Column<decimal>(type: "TEXT", nullable: true),
                    TaxAmount = table.Column<decimal>(type: "TEXT", nullable: true),
                    TotalAmount = table.Column<decimal>(type: "TEXT", nullable: true),
                    PoReference = table.Column<string>(type: "TEXT", nullable: true),
                    ExtractedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExtractionResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExtractionResults_IngestItems_IngestItemId",
                        column: x => x.IngestItemId,
                        principalTable: "IngestItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IngestItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Outcome = table.Column<int>(type: "INTEGER", nullable: false),
                    MatchedCardCode = table.Column<string>(type: "TEXT", nullable: true),
                    MatchedPoDocEntry = table.Column<int>(type: "INTEGER", nullable: true),
                    MatchedPoDocNum = table.Column<string>(type: "TEXT", nullable: true),
                    MatchedGrpoDocEntry = table.Column<int>(type: "INTEGER", nullable: true),
                    MatchedGrpoDocNum = table.Column<string>(type: "TEXT", nullable: true),
                    VarianceReasons = table.Column<string>(type: "TEXT", nullable: true),
                    MatchedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchResults_IngestItems_IngestItemId",
                        column: x => x.IngestItemId,
                        principalTable: "IngestItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExtractionResults_IngestItemId",
                table: "ExtractionResults",
                column: "IngestItemId");

            migrationBuilder.CreateIndex(
                name: "IX_IngestItems_IngestBatchId",
                table: "IngestItems",
                column: "IngestBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_IngestItems_TenantId_IngestBatchId",
                table: "IngestItems",
                columns: new[] { "TenantId", "IngestBatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_IngestItems_TenantId_IngestBatchId_ContentHash",
                table: "IngestItems",
                columns: new[] { "TenantId", "IngestBatchId", "ContentHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IngestItems_TenantId_Status",
                table: "IngestItems",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchResults_IngestItemId",
                table: "MatchResults",
                column: "IngestItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExtractionResults");

            migrationBuilder.DropTable(
                name: "IngestSources");

            migrationBuilder.DropTable(
                name: "MatchResults");

            migrationBuilder.DropTable(
                name: "IngestItems");

            migrationBuilder.DropTable(
                name: "IngestBatches");
        }
    }
}
