using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LogMount.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceFeederWithErrorNameInRetryImprove : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ErrorName",
                table: "RetryImprove",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.Sql("UPDATE RetryImprove SET ErrorName = Feeder WHERE ErrorName IS NULL AND Feeder IS NOT NULL");

            migrationBuilder.DropColumn(
                name: "Feeder",
                table: "RetryImprove");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Feeder",
                table: "RetryImprove",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.Sql("UPDATE RetryImprove SET Feeder = ErrorName WHERE Feeder IS NULL AND ErrorName IS NOT NULL");

            migrationBuilder.DropColumn(
                name: "ErrorName",
                table: "RetryImprove");
        }
    }
}
