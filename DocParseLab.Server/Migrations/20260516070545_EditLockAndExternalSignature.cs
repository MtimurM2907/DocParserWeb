using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocParseLab.Server.Migrations
{
    /// <inheritdoc />
    public partial class EditLockAndExternalSignature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EditLockExpiresAt",
                table: "ParsedDocuments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EditLockedByUserId",
                table: "ParsedDocuments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CertificateSubject",
                table: "DocumentSignatures",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CertificateThumbprint",
                table: "DocumentSignatures",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ExternalCryptoVerified",
                table: "DocumentSignatures",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalPayloadBase64",
                table: "DocumentSignatures",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ParsedDocuments_EditLockedByUserId",
                table: "ParsedDocuments",
                column: "EditLockedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ParsedDocuments_Users_EditLockedByUserId",
                table: "ParsedDocuments",
                column: "EditLockedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ParsedDocuments_Users_EditLockedByUserId",
                table: "ParsedDocuments");

            migrationBuilder.DropIndex(
                name: "IX_ParsedDocuments_EditLockedByUserId",
                table: "ParsedDocuments");

            migrationBuilder.DropColumn(
                name: "EditLockExpiresAt",
                table: "ParsedDocuments");

            migrationBuilder.DropColumn(
                name: "EditLockedByUserId",
                table: "ParsedDocuments");

            migrationBuilder.DropColumn(
                name: "CertificateSubject",
                table: "DocumentSignatures");

            migrationBuilder.DropColumn(
                name: "CertificateThumbprint",
                table: "DocumentSignatures");

            migrationBuilder.DropColumn(
                name: "ExternalCryptoVerified",
                table: "DocumentSignatures");

            migrationBuilder.DropColumn(
                name: "ExternalPayloadBase64",
                table: "DocumentSignatures");
        }
    }
}
