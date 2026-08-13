using System.ComponentModel.DataAnnotations;

namespace MSDentalSys.Data.Models
{
    public class Diagnostico
    {
        public int DiagnosticoId { get; set; }

        public int AtencionOdontologicaId { get; set; }

        [Required]
        [StringLength(300)]
        public string Descripcion { get; set; } = string.Empty;

        [StringLength(300)]
        public string? Observaciones { get; set; }

        public AtencionOdontologica AtencionOdontologica { get; set; } = null!;
    }
}
