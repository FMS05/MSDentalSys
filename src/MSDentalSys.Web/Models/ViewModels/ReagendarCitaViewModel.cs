using System.ComponentModel.DataAnnotations;

namespace MSDentalSys.Web.Models.ViewModels
{
    public class ReagendarCitaViewModel
    {
        public int CitaId { get; set; }

        [Required(ErrorMessage = "Indica la nueva fecha y hora.")]
        [Display(Name = "Nueva fecha y hora")]
        public DateTime FechaHoraInicio { get; set; }
    }
}
