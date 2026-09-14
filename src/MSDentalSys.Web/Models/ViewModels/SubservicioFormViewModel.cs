using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using MSDentalSys.Data.Models;

namespace MSDentalSys.Web.Models.ViewModels;

public class SubservicioFormViewModel
{
    public int SubservicioOdontologicoId { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "Selecciona un servicio válido.")]
    [Display(Name = "Servicio")]
    public int ServicioOdontologicoId { get; set; }
    [Required, StringLength(100)]
    public string Nombre { get; set; } = string.Empty;
    [StringLength(300), Display(Name = "Descripción")]
    public string? Descripcion { get; set; }
    [Required, Range(1, 1440), Display(Name = "Duración estimada (minutos)")]
    public int? DuracionEstimadaMinutos { get; set; }
    [Required(ErrorMessage = "Selecciona una clasificación."), EnumDataType(typeof(ClasificacionSubservicio))]
    [Display(Name = "Clasificación")]
    public ClasificacionSubservicio? Clasificacion { get; set; }
    public string? ServicioNombre { get; set; }
    public IEnumerable<SelectListItem> Servicios { get; set; } = [];
}
