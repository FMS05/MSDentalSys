using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSDentalSys.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCedulaToApplicationUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Cedula",
                table: "AspNetUsers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_Cedula",
                table: "AspNetUsers",
                column: "Cedula",
                unique: true,
                filter: "[Cedula] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_Cedula",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Cedula",
                table: "AspNetUsers");
        }
    }
}
