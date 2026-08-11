using System.ComponentModel.DataAnnotations;

namespace MSDentalSys.Models
{
    public class ActualizarEstadoCitaViewModel
    {
        public int CitaId { get; set; }

        [Required(ErrorMessage = "Selecciona un estado válido.")]
        public string EstadoCita { get; set; } = string.Empty;
    }
}
