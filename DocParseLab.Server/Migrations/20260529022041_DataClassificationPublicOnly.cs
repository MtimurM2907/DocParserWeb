using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocParseLab.Server.Migrations
{
    /// <inheritdoc />
    public partial class DataClassificationPublicOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "ParsedDocuments"
                SET "DataClassification" = 'Public'
                WHERE "DataClassification" IS NULL
                   OR TRIM("DataClassification") = ''
                   OR "DataClassification" = 'Internal';
                """);

            migrationBuilder.AlterColumn<string>(
                name: "DataClassification",
                table: "ParsedDocuments",
                type: "text",
                nullable: false,
                defaultValue: "Public",
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValue: "Internal");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "DataClassification",
                table: "ParsedDocuments",
                type: "text",
                nullable: false,
                defaultValue: "Internal",
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValue: "Public");
        }
    }
}
