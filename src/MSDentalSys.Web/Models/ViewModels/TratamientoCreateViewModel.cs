using System.ComponentModel.DataAnnotations;

namespace MSDentalSys.Web.Models.ViewModels;

public class TratamientoCreateViewModel
{
    public int AtencionOdontologicaId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Selecciona un servicio odontológico.")]
    [Display(Name = "Servicio odontológico")]
    public int ServicioOdontologicoId { get; set; }

    [Required(ErrorMessage = "La fecha de inicio es obligatoria.")]
    [Display(Name = "Fecha de inicio")]
    public DateTime FechaInicio { get; set; } = DateTime.Today;

    [Required(ErrorMessage = "El estado del tratamiento es obligatorio.")]
    [Display(Name = "Estado")]
    public string EstadoTratamiento { get; set; } = "Planificado";

    [StringLength(400)]
    [Display(Name = "Observaciones")]
    public string? Observaciones { get; set; }

    public IEnumerable<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> Servicios { get; set; } = [];
}
