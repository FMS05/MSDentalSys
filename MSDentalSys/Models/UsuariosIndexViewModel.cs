namespace MSDentalSys.Models
{
    public class UsuariosIndexViewModel
    {
        public string? SearchTerm { get; set; }
        public IReadOnlyList<UsuarioListItemViewModel> Usuarios { get; set; } = [];
    }
}
