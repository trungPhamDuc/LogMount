using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LogMount.Migrations
{
    /// <inheritdoc />
    public partial class AddImprovementTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ErrorImprove",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Error = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Line = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Lane = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Side = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Machine = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    EngineerName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ActionTaken = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ExecutionDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ErrorImprove", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RetryImprove",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PartsName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Line = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Lane = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Side = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Machine = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Feeder = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    EngineerName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ActionTaken = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ExecutionDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetryImprove", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ErrorImprove_Error",
                table: "ErrorImprove",
                column: "Error");

            migrationBuilder.CreateIndex(
                name: "IX_ErrorImprove_ExecutionDate",
                table: "ErrorImprove",
                column: "ExecutionDate");

            migrationBuilder.CreateIndex(
                name: "IX_RetryImprove_ExecutionDate",
                table: "RetryImprove",
                column: "ExecutionDate");

            migrationBuilder.CreateIndex(
                name: "IX_RetryImprove_PartsName",
                table: "RetryImprove",
                column: "PartsName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ErrorImprove");

            migrationBuilder.DropTable(
                name: "RetryImprove");
        }
    }
}
