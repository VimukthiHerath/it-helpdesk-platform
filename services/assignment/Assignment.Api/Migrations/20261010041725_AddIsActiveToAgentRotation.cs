using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Assignment.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddIsActiveToAgentRotation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "Agents",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_active",
                table: "Agents");
        }
    }
}
