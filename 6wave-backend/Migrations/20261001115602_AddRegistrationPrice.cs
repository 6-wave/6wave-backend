using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SixWaveBackend.Migrations
{
    /// <inheritdoc />
    public partial class AddRegistrationPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PriceNaira",
                table: "Registrations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Registrations made before waves existed were all sold at these prices.
            migrationBuilder.Sql("""
                UPDATE "Registrations" SET "PriceNaira" = CASE "OptionId"
                    WHEN 'regular' THEN 7000
                    WHEN 'vip' THEN 10000
                    WHEN 'regular-group' THEN 35000
                    WHEN 'vip-group' THEN 50000
                    WHEN 'table-100k' THEN 100000
                    WHEN 'table-150k' THEN 150000
                    WHEN 'table-200k' THEN 200000
                    WHEN 'table-250k' THEN 250000
                    WHEN 'table-300k' THEN 300000
                    ELSE 0 END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PriceNaira",
                table: "Registrations");
        }
    }
}
