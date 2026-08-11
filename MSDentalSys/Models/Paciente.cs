using System.ComponentModel.DataAnnotations;

namespace MSDentalSys.Models
{
    public class Paciente
    {
        public int PacienteId { get; set; }

        [Required]
        [StringLength(60)]
        public string Nombre { get; set; } = string.Empty;

        [Required]
        [StringLength(60)]
        public string Apellido { get; set; } = string.Empty;

        [StringLength(20)]
        public string? Cedula { get; set; }

        public DateTime? FechaNacimiento { get; set; }

        [StringLength(20)]
        public string? Sexo { get; set; }

        [StringLength(20)]
        public string? Telefono { get; set; }

        [StringLength(120)]
        public string? Correo { get; set; }

        [StringLength(250)]
        public string? Direccion { get; set; }

        [StringLength(255)]
        public string? FotoPaciente { get; set; }

        [StringLength(120)]
        public string? ContactoEmergencia { get; set; }

        [StringLength(20)]
        public string? TelefonoEmergencia { get; set; }

        public bool Estado { get; set; } = true;

        public DateTime FechaRegistro { get; set; } = DateTime.Now;

        public AntecedenteClinico? AntecedenteClinico { get; set; }
    }
}