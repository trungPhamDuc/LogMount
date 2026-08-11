using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LogMount.Migrations
{
    public partial class AddAutoImportStates : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AutoImportStates",
                columns: table => new
                {
                    JobName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastRunDate = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    LastStartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastCompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutoImportStates", x => x.JobName);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutoImportStates");
        }
    }
}
