using System.ComponentModel.DataAnnotations;

namespace MSDentalSys.Models
{
    public class AtencionOdontologica
    {
        public int AtencionOdontologicaId { get; set; }

        public int PacienteId { get; set; }

        public int? CitaId { get; set; }

        public string OdontologoId { get; set; } = string.Empty;

        public DateTime FechaAtencion { get; set; } = DateTime.Now;

        [StringLength(250)]
        public string? MotivoConsulta { get; set; }

        [StringLength(500)]
        public string? Observaciones { get; set; }

        public Paciente Paciente { get; set; } = null!;

        public Cita? Cita { get; set; }

        public ApplicationUser Odontologo { get; set; } = null!;

        public ICollection<Diagnostico> Diagnosticos { get; set; }
            = new List<Diagnostico>();

        public ICollection<EvolucionClinica> EvolucionesClinicas { get; set; }
            = new List<EvolucionClinica>();

        public ICollection<Tratamiento> Tratamientos { get; set; }
            = new List<Tratamiento>();
    }
}