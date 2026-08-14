using System.ComponentModel.DataAnnotations;

namespace MSDentalSys.Web.Models.ViewModels;

public class AtencionOdontologicaCreateViewModel
{
    public int CitaId { get; set; }

    [Required(ErrorMessage = "El motivo de consulta es obligatorio.")]
    [StringLength(250)]
    [Display(Name = "Motivo de consulta")]
    public string MotivoConsulta { get; set; } = string.Empty;

    [StringLength(500)]
    [Display(Name = "Observaciones")]
    public string? Observaciones { get; set; }
}
