using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LogMount.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExpensiveParts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PartsName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Cost = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpensiveParts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RetryLogEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Date = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Line = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Language = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    OccurrenceTime = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    LotName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ErrorNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ErrorName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Lane = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Table = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PartsNo = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    PartsName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    HeadNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    NozzleType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FeederNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FeederId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CartId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    VisErrorNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ErrorVacuum = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetryLogEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExpensiveParts_PartsName",
                table: "ExpensiveParts",
                column: "PartsName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RetryLogEntries_ErrorNo",
                table: "RetryLogEntries",
                column: "ErrorNo");

            migrationBuilder.CreateIndex(
                name: "IX_RetryLogEntries_Line",
                table: "RetryLogEntries",
                column: "Line");

            migrationBuilder.CreateIndex(
                name: "IX_RetryLogEntries_PartsName",
                table: "RetryLogEntries",
                column: "PartsName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExpensiveParts");

            migrationBuilder.DropTable(
                name: "RetryLogEntries");
        }
    }
}
