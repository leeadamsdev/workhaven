using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workhaven.Api.Features.Identity.Migrations;

/// <inheritdoc />
public partial class RequireUniqueEmail : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "EmailIndex",
            schema: "identity",
            table: "AspNetUsers");

        migrationBuilder.CreateIndex(
            name: "EmailIndex",
            schema: "identity",
            table: "AspNetUsers",
            column: "NormalizedEmail",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "EmailIndex",
            schema: "identity",
            table: "AspNetUsers");

        migrationBuilder.CreateIndex(
            name: "EmailIndex",
            schema: "identity",
            table: "AspNetUsers",
            column: "NormalizedEmail");
    }
}
