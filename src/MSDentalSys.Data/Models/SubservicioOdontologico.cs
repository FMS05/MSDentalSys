using System.ComponentModel.DataAnnotations;

namespace MSDentalSys.Data.Models;

public class SubservicioOdontologico
{
    public int SubservicioOdontologicoId { get; set; }
    public int ServicioOdontologicoId { get; set; }
    [Required, StringLength(100)]
    public string Nombre { get; set; } = string.Empty;
    [StringLength(300)]
    public string? Descripcion { get; set; }
    [Range(1, 1440)]
    public int DuracionEstimadaMinutos { get; set; }
    public ClasificacionSubservicio? Clasificacion { get; set; }
    public bool Estado { get; set; } = true;
    public DateTime FechaCreacion { get; set; } = DateTime.Now;
    // Identidad del catálogo inicial; no editable desde formularios.
    [StringLength(40)]
    public string? CodigoCatalogo { get; set; }
    public ServicioOdontologico ServicioOdontologico { get; set; } = null!;
    public ICollection<Cita> Citas { get; set; } = new List<Cita>();
}
