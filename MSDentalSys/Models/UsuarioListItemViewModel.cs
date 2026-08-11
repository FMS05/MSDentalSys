namespace MSDentalSys.Models
{
    public class UsuarioListItemViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string NombreCompleto { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Telefono { get; set; }
        public string Rol { get; set; } = string.Empty;
        public bool Estado { get; set; }
        public bool EsAdministradorInicial { get; set; }
    }
}
