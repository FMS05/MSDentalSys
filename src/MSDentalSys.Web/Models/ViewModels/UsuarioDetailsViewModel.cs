namespace MSDentalSys.Web.Models.ViewModels
{
    public class UsuarioDetailsViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string NombreCompleto { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Cedula { get; set; }
        public string? FotoPerfil { get; set; }
        public string? Telefono { get; set; }
        public string Rol { get; set; } = string.Empty;
        public bool Estado { get; set; }
        public DateTime FechaCreacion { get; set; }
        public bool EsAdministradorInicial { get; set; }
        public bool PuedeRestablecerPassword { get; set; }
        public bool PuedeEditar { get; set; }
        public bool EsPerfilPropio { get; set; }
        public bool PuedeCambiarEstado { get; set; }
    }
}
