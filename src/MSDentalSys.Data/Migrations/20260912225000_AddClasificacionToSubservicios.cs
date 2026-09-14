using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSDentalSys.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClasificacionToSubservicios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Clasificacion",
                table: "SubserviciosOdontologicos",
                type: "int",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Subservicios_Clasificacion",
                table: "SubserviciosOdontologicos",
                sql: "[Clasificacion] IS NULL OR [Clasificacion] IN (1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Subservicios_Clasificacion",
                table: "SubserviciosOdontologicos");

            migrationBuilder.DropColumn(
                name: "Clasificacion",
                table: "SubserviciosOdontologicos");
        }
    }
}
