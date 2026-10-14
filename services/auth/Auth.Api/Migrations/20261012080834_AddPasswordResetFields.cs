using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auth.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPasswordResetFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "last_password_reset_requested_at_utc",
                table: "Users",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "reset_request_count_in_window",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "reset_token_expires_at_utc",
                table: "Users",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reset_token_hash",
                table: "Users",
                type: "varchar(255)",
                maxLength: 255,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "reset_token_used",
                table: "Users",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "security_stamp",
                table: "Users",
                type: "varchar(255)",
                maxLength: 255,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_password_reset_requested_at_utc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "reset_request_count_in_window",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "reset_token_expires_at_utc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "reset_token_hash",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "reset_token_used",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "security_stamp",
                table: "Users");
        }
    }
}
