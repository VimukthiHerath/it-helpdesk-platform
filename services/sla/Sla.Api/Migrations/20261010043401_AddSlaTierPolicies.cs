using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Sla.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSlaTierPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SlaTierPolicies",
                columns: table => new
                {
                    TierName = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    LastUpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedByAdminId = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlaTierPolicies", x => x.TierName);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TicketSlas",
                columns: table => new
                {
                    TicketId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TargetResolutionTimeUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketSlas", x => x.TicketId);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "SlaTierPolicies",
                columns: new[] { "TierName", "DurationMinutes", "LastUpdatedAtUtc", "UpdatedByAdminId" },
                values: new object[,]
                {
                    { "CRITICAL", 60, new DateTime(2026, 10, 10, 4, 34, 1, 321, DateTimeKind.Utc).AddTicks(3950), null },
                    { "HIGH", 240, new DateTime(2026, 10, 10, 4, 34, 1, 321, DateTimeKind.Utc).AddTicks(3948), null },
                    { "LOW", 2880, new DateTime(2026, 10, 10, 4, 34, 1, 321, DateTimeKind.Utc).AddTicks(3945), null },
                    { "MEDIUM", 1440, new DateTime(2026, 10, 10, 4, 34, 1, 321, DateTimeKind.Utc).AddTicks(3947), null }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SlaTierPolicies");

            migrationBuilder.DropTable(
                name: "TicketSlas");
        }
    }
}
