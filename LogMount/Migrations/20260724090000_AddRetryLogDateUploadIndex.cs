using LogMount.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LogMount.Migrations
{
    [DbContext(typeof(LogMountDbContext))]
    [Migration("20260724090000_AddRetryLogDateUploadIndex")]
    public partial class AddRetryLogDateUploadIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_RetryLogEntries_Date_UploadedAt_Id",
                table: "RetryLogEntries",
                columns: new[] { "Date", "UploadedAt", "Id" },
                descending: new[] { false, true, false });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RetryLogEntries_Date_UploadedAt_Id",
                table: "RetryLogEntries");
        }
    }
}
