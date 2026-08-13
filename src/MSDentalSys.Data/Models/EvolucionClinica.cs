using System.ComponentModel.DataAnnotations;

namespace MSDentalSys.Data.Models
{
    public class EvolucionClinica
    {
        public int EvolucionClinicaId { get; set; }

        public int AtencionOdontologicaId { get; set; }

        public DateTime FechaEvolucion { get; set; } = DateTime.Now;

        [Required]
        [StringLength(500)]
        public string Descripcion { get; set; } = string.Empty;

        public AtencionOdontologica AtencionOdontologica { get; set; } = null!;
    }
}
