namespace MSDentalSys.Models
{
    public class DashboardViewModel
    {
        public string NombreCompleto { get; set; } = string.Empty;
        public string Rol { get; set; } = string.Empty;
        public int PacientesRegistrados { get; set; }
        public int CitasDelDia { get; set; }
        public int CitasPendientes { get; set; }
        public int AtencionesRealizadas { get; set; }
    }
}
