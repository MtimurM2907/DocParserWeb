using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocParseLab.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddEditedText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EditedAt",
                table: "ParsedDocuments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EditedText",
                table: "ParsedDocuments",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EditedAt",
                table: "ParsedDocuments");

            migrationBuilder.DropColumn(
                name: "EditedText",
                table: "ParsedDocuments");
        }
    }
}
