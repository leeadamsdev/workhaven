using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workhaven.Api.Features.Identity.Migrations;

/// <inheritdoc />
public partial class QueueConfirmationEmails : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PendingConfirmationEmails",
            schema: "identity",
            columns: table => new
            {
                UserId = table.Column<string>(type: "text", nullable: false),
                NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Attempts = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PendingConfirmationEmails", x => x.UserId);
                table.ForeignKey(
                    name: "FK_PendingConfirmationEmails_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalSchema: "identity",
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PendingConfirmationEmails_NextAttemptAt",
            schema: "identity",
            table: "PendingConfirmationEmails",
            column: "NextAttemptAt");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "PendingConfirmationEmails",
            schema: "identity");
    }
}
