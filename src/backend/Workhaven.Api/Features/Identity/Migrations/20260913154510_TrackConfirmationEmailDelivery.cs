using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workhaven.Api.Features.Identity.Migrations;

/// <inheritdoc />
public partial class TrackConfirmationEmailDelivery : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_PendingConfirmationEmails_NextAttemptAt",
            schema: "identity",
            table: "PendingConfirmationEmails");

        migrationBuilder.AddColumn<Guid>(
            name: "DeliveryId",
            schema: "identity",
            table: "PendingConfirmationEmails",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "FailedAt",
            schema: "identity",
            table: "PendingConfirmationEmails",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "FailureCode",
            schema: "identity",
            table: "PendingConfirmationEmails",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "PreparedAt",
            schema: "identity",
            table: "PendingConfirmationEmails",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ProtectedMessage",
            schema: "identity",
            table: "PendingConfirmationEmails",
            type: "text",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_PendingConfirmationEmails_NextAttemptAt",
            schema: "identity",
            table: "PendingConfirmationEmails",
            column: "NextAttemptAt",
            filter: "\"FailedAt\" IS NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_PendingConfirmationEmails_NextAttemptAt",
            schema: "identity",
            table: "PendingConfirmationEmails");

        migrationBuilder.DropColumn(
            name: "DeliveryId",
            schema: "identity",
            table: "PendingConfirmationEmails");

        migrationBuilder.DropColumn(
            name: "FailedAt",
            schema: "identity",
            table: "PendingConfirmationEmails");

        migrationBuilder.DropColumn(
            name: "FailureCode",
            schema: "identity",
            table: "PendingConfirmationEmails");

        migrationBuilder.DropColumn(
            name: "PreparedAt",
            schema: "identity",
            table: "PendingConfirmationEmails");

        migrationBuilder.DropColumn(
            name: "ProtectedMessage",
            schema: "identity",
            table: "PendingConfirmationEmails");

        migrationBuilder.CreateIndex(
            name: "IX_PendingConfirmationEmails_NextAttemptAt",
            schema: "identity",
            table: "PendingConfirmationEmails",
            column: "NextAttemptAt");
    }
}
