using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocParseLab.Server.Migrations
{
    /// <inheritdoc />
    public partial class StabilityInvariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DocumentShares_DocumentId",
                table: "DocumentShares");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "ParsedDocuments",
                type: "bytea",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentShares_DocumentId_ToUserId",
                table: "DocumentShares",
                columns: new[] { "DocumentId", "ToUserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DocumentShares_DocumentId_ToUserId",
                table: "DocumentShares");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "ParsedDocuments");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentShares_DocumentId",
                table: "DocumentShares",
                column: "DocumentId");
        }
    }
}
