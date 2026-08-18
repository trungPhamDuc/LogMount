using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LogMount.Migrations
{
    /// <inheritdoc />
    public partial class AddRequestLogEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RequestLogEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Date = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Shift = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Line = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Process = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ModelSuffix = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Chassis = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Board = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PartAssy = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    WorkOrder = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    PartNo = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    PidOrLot = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PQty = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    RQty = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    AmtOnRequest = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Remarks = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Department = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    StatusRemarks = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SourceFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    UploadBatchId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestLogEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RequestLogEntries_Date",
                table: "RequestLogEntries",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_RequestLogEntries_Line",
                table: "RequestLogEntries",
                column: "Line");

            migrationBuilder.CreateIndex(
                name: "IX_RequestLogEntries_PartNo",
                table: "RequestLogEntries",
                column: "PartNo");

            migrationBuilder.CreateIndex(
                name: "IX_RequestLogEntries_RQty",
                table: "RequestLogEntries",
                column: "RQty");

            migrationBuilder.CreateIndex(
                name: "IX_RequestLogEntries_UploadedAt",
                table: "RequestLogEntries",
                column: "UploadedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RequestLogEntries");
        }
    }
}
