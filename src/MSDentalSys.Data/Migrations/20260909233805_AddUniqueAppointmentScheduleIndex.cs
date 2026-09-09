using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSDentalSys.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueAppointmentScheduleIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Citas_OdontologoId",
                table: "Citas");

            migrationBuilder.CreateIndex(
                name: "UX_Citas_Odontologo_FechaHoraInicio_NoCancelada",
                table: "Citas",
                columns: new[] { "OdontologoId", "FechaHoraInicio" },
                unique: true,
                filter: "[EstadoCita] <> 'Cancelada'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Citas_Odontologo_FechaHoraInicio_NoCancelada",
                table: "Citas");

            migrationBuilder.CreateIndex(
                name: "IX_Citas_OdontologoId",
                table: "Citas",
                column: "OdontologoId");
        }
    }
}
