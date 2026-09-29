using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SixWaveBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Tickets_BackupCode",
                table: "Tickets",
                column: "BackupCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Registrations_Reference",
                table: "Registrations",
                column: "Reference",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tickets_BackupCode",
                table: "Tickets");

            migrationBuilder.DropIndex(
                name: "IX_Registrations_Reference",
                table: "Registrations");
        }
    }
}
