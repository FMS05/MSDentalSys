using System.ComponentModel.DataAnnotations;

namespace MSDentalSys.Web.Models.ViewModels
{
    public class PacienteFormViewModel
    {
        public int PacienteId { get; set; }

        [Required(ErrorMessage = "El nombre es obligatorio.")]
        [StringLength(60, ErrorMessage = "El nombre no puede superar los 60 caracteres.")]
        [Display(Name = "Nombre")]
        public string Nombre { get; set; } = string.Empty;

        [Required(ErrorMessage = "El apellido es obligatorio.")]
        [StringLength(60, ErrorMessage = "El apellido no puede superar los 60 caracteres.")]
        [Display(Name = "Apellido")]
        public string Apellido { get; set; } = string.Empty;

        [StringLength(20, ErrorMessage = "La cédula no puede superar los 20 caracteres.")]
        [Display(Name = "Cédula")]
        public string? Cedula { get; set; }

        [DataType(DataType.Date)]
        [Display(Name = "Fecha de nacimiento")]
        public DateTime? FechaNacimiento { get; set; }

        [StringLength(20)]
        [Display(Name = "Sexo")]
        public string? Sexo { get; set; }

        [StringLength(20)]
        [Display(Name = "Teléfono")]
        public string? Telefono { get; set; }

        [StringLength(120)]
        [EmailAddress(ErrorMessage = "Ingresa un correo electrónico válido.")]
        [Display(Name = "Correo electrónico")]
        public string? Correo { get; set; }

        [StringLength(250)]
        [Display(Name = "Dirección")]
        public string? Direccion { get; set; }

        [StringLength(120)]
        [Display(Name = "Contacto de emergencia")]
        public string? ContactoEmergencia { get; set; }

        [StringLength(20)]
        [Display(Name = "Teléfono de emergencia")]
        public string? TelefonoEmergencia { get; set; }

        [StringLength(300)]
        [Display(Name = "Alergias")]
        public string? Alergias { get; set; }

        [StringLength(300)]
        [Display(Name = "Enfermedades sistémicas")]
        public string? EnfermedadesSistemicas { get; set; }

        [StringLength(300)]
        [Display(Name = "Medicamentos actuales")]
        public string? MedicamentosActuales { get; set; }

        [StringLength(300)]
        [Display(Name = "Cirugías previas")]
        public string? CirugiasPrevias { get; set; }

        [StringLength(300)]
        [Display(Name = "Hábitos relevantes")]
        public string? HabitosRelevantes { get; set; }

        [Display(Name = "Embarazo")]
        public bool? Embarazo { get; set; }

        [StringLength(500)]
        [Display(Name = "Observaciones")]
        public string? Observaciones { get; set; }
    }
}
