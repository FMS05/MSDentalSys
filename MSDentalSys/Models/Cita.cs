using System.ComponentModel.DataAnnotations;

namespace MSDentalSys.Models
{
    public class Cita
    {
        public int CitaId { get; set; }

        public int PacienteId { get; set; }

        public string OdontologoId { get; set; } = string.Empty;

        public int ServicioOdontologicoId { get; set; }

        public DateTime FechaHoraInicio { get; set; }

        [Required]
        [StringLength(20)]
        public string EstadoCita { get; set; } = "Pendiente";

        [StringLength(300)]
        public string? Observaciones { get; set; }

        public DateTime FechaCreacion { get; set; } = DateTime.Now;

        public Paciente Paciente { get; set; } = null!;

        public ApplicationUser Odontologo { get; set; } = null!;

        public ServicioOdontologico ServicioOdontologico { get; set; } = null!;
    }
}