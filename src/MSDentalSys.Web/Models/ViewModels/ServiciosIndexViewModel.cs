using MSDentalSys.Data.Models;

namespace MSDentalSys.Web.Models.ViewModels
{
    public class ServiciosIndexViewModel
    {
        public string? SearchTerm { get; set; }
        public IReadOnlyList<ServicioOdontologico> Servicios { get; set; } = [];
    }
}
