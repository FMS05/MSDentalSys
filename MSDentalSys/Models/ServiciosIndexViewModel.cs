namespace MSDentalSys.Models
{
    public class ServiciosIndexViewModel
    {
        public string? SearchTerm { get; set; }
        public IReadOnlyList<ServicioOdontologico> Servicios { get; set; } = [];
    }
}
