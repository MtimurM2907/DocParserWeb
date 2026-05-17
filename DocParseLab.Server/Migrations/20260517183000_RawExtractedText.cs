using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocParseLab.Server.Migrations;

/// <inheritdoc />
public partial class RawExtractedText : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "RawExtractedText",
            table: "ParsedDocuments",
            type: "text",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "RawExtractedText",
            table: "ParsedDocuments");
    }
}
