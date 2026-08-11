using System.ComponentModel.DataAnnotations;

namespace MSDentalSys.Models
{
    public class AntecedenteClinico
    {
        public int AntecedenteClinicoId { get; set; }

        public int PacienteId { get; set; }

        [StringLength(300)]
        public string? Alergias { get; set; }

        [StringLength(300)]
        public string? EnfermedadesSistemicas { get; set; }

        [StringLength(300)]
        public string? MedicamentosActuales { get; set; }

        [StringLength(300)]
        public string? CirugiasPrevias { get; set; }

        [StringLength(300)]
        public string? HabitosRelevantes { get; set; }

        public bool? Embarazo { get; set; }

        [StringLength(500)]
        public string? Observaciones { get; set; }

        public DateTime FechaActualizacion { get; set; } = DateTime.Now;

        public Paciente Paciente { get; set; } = null!;
    }
}