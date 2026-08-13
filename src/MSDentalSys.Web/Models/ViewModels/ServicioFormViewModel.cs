using System.ComponentModel.DataAnnotations;

namespace MSDentalSys.Web.Models.ViewModels
{
    public class ServicioFormViewModel
    {
        public int ServicioOdontologicoId { get; set; }

        [Required(ErrorMessage = "El nombre del servicio es obligatorio.")]
        [StringLength(100, ErrorMessage = "El nombre no puede superar los 100 caracteres.")]
        [Display(Name = "Nombre")]
        public string Nombre { get; set; } = string.Empty;

        [StringLength(300, ErrorMessage = "La descripción no puede superar los 300 caracteres.")]
        [Display(Name = "Descripción")]
        public string? Descripcion { get; set; }

        [Range(1, 1440, ErrorMessage = "La duración debe estar entre 1 y 1440 minutos.")]
        [Display(Name = "Duración estimada (minutos)")]
        public int? DuracionEstimadaMinutos { get; set; }
    }
}
