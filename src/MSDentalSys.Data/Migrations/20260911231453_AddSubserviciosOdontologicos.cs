using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSDentalSys.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSubserviciosOdontologicos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Citas_ServicioOdontologicoId",
                table: "Citas");

            migrationBuilder.AddColumn<int>(
                name: "DuracionProgramadaMinutos",
                table: "Citas",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SubservicioOdontologicoId",
                table: "Citas",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SubserviciosOdontologicos",
                columns: table => new
                {
                    SubservicioOdontologicoId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ServicioOdontologicoId = table.Column<int>(type: "int", nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    DuracionEstimadaMinutos = table.Column<int>(type: "int", nullable: false),
                    Estado = table.Column<bool>(type: "bit", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CodigoCatalogo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubserviciosOdontologicos", x => x.SubservicioOdontologicoId);
                    table.UniqueConstraint("AK_SubserviciosOdontologicos_ServicioOdontologicoId_SubservicioOdontologicoId", x => new { x.ServicioOdontologicoId, x.SubservicioOdontologicoId });
                    table.CheckConstraint("CK_Subservicios_Duracion", "[DuracionEstimadaMinutos] >= 1 AND [DuracionEstimadaMinutos] <= 1440");
                    table.ForeignKey(
                        name: "FK_SubserviciosOdontologicos_ServiciosOdontologicos_ServicioOdontologicoId",
                        column: x => x.ServicioOdontologicoId,
                        principalTable: "ServiciosOdontologicos",
                        principalColumn: "ServicioOdontologicoId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Citas_ServicioOdontologicoId_SubservicioOdontologicoId",
                table: "Citas",
                columns: new[] { "ServicioOdontologicoId", "SubservicioOdontologicoId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Citas_DuracionProgramada",
                table: "Citas",
                sql: "[DuracionProgramadaMinutos] IS NULL OR ([DuracionProgramadaMinutos] >= 1 AND [DuracionProgramadaMinutos] <= 1440)");

            migrationBuilder.CreateIndex(
                name: "IX_SubserviciosOdontologicos_CodigoCatalogo",
                table: "SubserviciosOdontologicos",
                column: "CodigoCatalogo",
                unique: true,
                filter: "[CodigoCatalogo] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Subservicios_Servicio_Nombre",
                table: "SubserviciosOdontologicos",
                columns: new[] { "ServicioOdontologicoId", "Nombre" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Citas_SubserviciosOdontologicos_ServicioOdontologicoId_SubservicioOdontologicoId",
                table: "Citas",
                columns: new[] { "ServicioOdontologicoId", "SubservicioOdontologicoId" },
                principalTable: "SubserviciosOdontologicos",
                principalColumns: new[] { "ServicioOdontologicoId", "SubservicioOdontologicoId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Citas_SubserviciosOdontologicos_ServicioOdontologicoId_SubservicioOdontologicoId",
                table: "Citas");

            migrationBuilder.DropTable(
                name: "SubserviciosOdontologicos");

            migrationBuilder.DropIndex(
                name: "IX_Citas_ServicioOdontologicoId_SubservicioOdontologicoId",
                table: "Citas");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Citas_DuracionProgramada",
                table: "Citas");

            migrationBuilder.DropColumn(
                name: "DuracionProgramadaMinutos",
                table: "Citas");

            migrationBuilder.DropColumn(
                name: "SubservicioOdontologicoId",
                table: "Citas");

            migrationBuilder.CreateIndex(
                name: "IX_Citas_ServicioOdontologicoId",
                table: "Citas",
                column: "ServicioOdontologicoId");
        }
    }
}
