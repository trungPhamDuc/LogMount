using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LogMount.Migrations
{
    public partial class AddErrorLogEntries : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ErrorLogEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Date = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    EventDate = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Line = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Lane = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Table = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Error = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    EventNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ProgramName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Details = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SourceFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    UploadBatchId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ErrorLogEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ErrorLogEntries_Date",
                table: "ErrorLogEntries",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_ErrorLogEntries_Date_UploadedAt_Id",
                table: "ErrorLogEntries",
                columns: new[] { "Date", "UploadedAt", "Id" },
                descending: new[] { false, true, false });

            migrationBuilder.CreateIndex(
                name: "IX_ErrorLogEntries_Error",
                table: "ErrorLogEntries",
                column: "Error");

            migrationBuilder.CreateIndex(
                name: "IX_ErrorLogEntries_Line",
                table: "ErrorLogEntries",
                column: "Line");

            migrationBuilder.CreateIndex(
                name: "IX_ErrorLogEntries_UploadedAt",
                table: "ErrorLogEntries",
                column: "UploadedAt");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ErrorLogEntries");
        }
    }
}
