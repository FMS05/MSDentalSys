using System.ComponentModel.DataAnnotations;

namespace MSDentalSys.Data.Models
{
    public class Tratamiento
    {
        public int TratamientoId { get; set; }

        public int AtencionOdontologicaId { get; set; }

        public int ServicioOdontologicoId { get; set; }

        public DateTime FechaInicio { get; set; } = DateTime.Today;

        [Required]
        [StringLength(20)]
        public string EstadoTratamiento { get; set; } = "Planificado";

        [StringLength(400)]
        public string? Observaciones { get; set; }

        public AtencionOdontologica AtencionOdontologica { get; set; } = null!;

        public ServicioOdontologico ServicioOdontologico { get; set; } = null!;
    }
}
