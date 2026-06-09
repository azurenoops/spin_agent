using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ato.Copilot.Core.Migrations
{
    /// <inheritdoc />
    public partial class Feature200_NotificationDeliveryChannels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // UF-016 / #200 — Add delivery-channel columns to NotificationPreferences
            migrationBuilder.AddColumn<bool>(
                name: "EmailEnabled",
                table: "NotificationPreferences",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "EmailAddress",
                table: "NotificationPreferences",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TeamsEnabled",
                table: "NotificationPreferences",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SlackEnabled",
                table: "NotificationPreferences",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "EmailEnabled",   table: "NotificationPreferences");
            migrationBuilder.DropColumn(name: "EmailAddress",   table: "NotificationPreferences");
            migrationBuilder.DropColumn(name: "TeamsEnabled",   table: "NotificationPreferences");
            migrationBuilder.DropColumn(name: "SlackEnabled",   table: "NotificationPreferences");
        }
    }
}
