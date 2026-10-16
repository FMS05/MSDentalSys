namespace MSDentalSys.Web.Models.ViewModels;

// Contrato de lectura: no expone entidades, datos clínicos ni propiedades de Identity.
public sealed class CitaCalendarioEventoViewModel
{
    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Start { get; init; } = string.Empty;
    public string? End { get; init; }
    public bool DuracionConocida { get; init; }
    public string Estado { get; init; } = string.Empty;
    public string OdontologoId { get; init; } = string.Empty;
    public string OdontologoNombre { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
}
