using System.ComponentModel.DataAnnotations;

namespace MSDentalSys.Data.Models
{
    public class ServicioOdontologico
    {
        public int ServicioOdontologicoId { get; set; }

        [Required]
        [StringLength(100)]
        public string Nombre { get; set; } = string.Empty;

        [StringLength(300)]
        public string? Descripcion { get; set; }

        public int? DuracionEstimadaMinutos { get; set; }

        public bool Estado { get; set; } = true;

        public DateTime FechaCreacion { get; set; } = DateTime.Now;

        public ICollection<Cita> Citas { get; set; } = new List<Cita>();
    }
}
