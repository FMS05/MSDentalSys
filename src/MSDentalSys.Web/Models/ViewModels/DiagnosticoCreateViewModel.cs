using System.ComponentModel.DataAnnotations;

namespace MSDentalSys.Web.Models.ViewModels;

public class DiagnosticoCreateViewModel
{
    public int AtencionOdontologicaId { get; set; }

    [Required(ErrorMessage = "La descripción del diagnóstico es obligatoria.")]
    [StringLength(300)]
    [Display(Name = "Descripción del diagnóstico")]
    public string Descripcion { get; set; } = string.Empty;

    [StringLength(300)]
    [Display(Name = "Observaciones")]
    public string? Observaciones { get; set; }
}
