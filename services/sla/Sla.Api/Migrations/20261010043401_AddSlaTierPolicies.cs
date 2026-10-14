using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sla.Api.Data;

namespace Sla.Api.Migrations;

[DbContext(typeof(SlaDbContext))]
[Migration("20261010043401_AddSlaTierPolicies")]
public partial class AddSlaTierPolicies : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SlaTierPolicies",
            columns: table => new
            {
                TierName = table.Column<string>(type: "varchar(255)", nullable: false),
                DurationMinutes = table.Column<int>(type: "int", nullable: false),
                LastUpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                UpdatedByAdminId = table.Column<string>(type: "longtext", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_SlaTierPolicies", x => x.TierName));

        // Column types are given explicitly: this migration has no Designer file, so EF has no
        // target model to look the types up in and InsertData would throw without them.
        migrationBuilder.InsertData("SlaTierPolicies",
            new[] { "TierName", "DurationMinutes", "LastUpdatedAtUtc" },
            new[] { "varchar(255)", "int", "datetime(6)" },
            new object[,]
            {
                { "LOW", 2880, new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc) },
                { "MEDIUM", 1440, new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc) },
                { "HIGH", 240, new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc) },
                { "CRITICAL", 60, new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc) }
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable("SlaTierPolicies");
}
