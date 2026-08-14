using System.ComponentModel.DataAnnotations;

namespace MSDentalSys.Web.Models.ViewModels;

public class EvolucionClinicaCreateViewModel
{
    public int AtencionOdontologicaId { get; set; }

    [Required(ErrorMessage = "La fecha de evolución es obligatoria.")]
    [Display(Name = "Fecha de evolución")]
    public DateTime FechaEvolucion { get; set; } = DateTime.Now;

    [Required(ErrorMessage = "La descripción es obligatoria.")]
    [StringLength(500)]
    [Display(Name = "Descripción")]
    public string Descripcion { get; set; } = string.Empty;
}
