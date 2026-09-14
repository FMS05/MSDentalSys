using System.Data;
using Microsoft.EntityFrameworkCore;
using MSDentalSys.Data.Context;
using MSDentalSys.Data.Models;

namespace MSDentalSys.Data.InitialData;

public static class SubservicioCatalogoSeeder
{
    // Solo mensajes controlados del catálogo; no contienen datos clínicos ni errores SQL.
    public sealed class CatalogoValidationException(string message) : InvalidOperationException(message);
    private sealed record Historico(int Id, string Servicio, string Nombre, int Minutos, string? Codigo);
    private static readonly Historico[] Historicos = [
        new(1, "Odontología general", "Consulta y evaluación general", 30, "MSCD-PROC-0001"),
        new(2, "Periodoncia", "Raspado y pulido dental", 45, null),
        new(3, "Endodoncia", "Tratamiento de conducto", 90, "MSCD-PROC-0076"),
        new(4, "Cirugía oral", "Extracción dental simple", 45, null),
        new(5, "Rehabilitación oral / Prótesis", "Colocación de corona dental", 45, null)
    ];

    public static void ValidateCatalog()
    {
        var entries = SubservicioCatalogo.Entradas;
        if (entries.Count != 139 || entries.Count(e => e.Clasificacion == ClasificacionSubservicio.Principal) != 85 ||
            entries.Count(e => e.Clasificacion == ClasificacionSubservicio.Complementario) != 54 ||
            !entries.Select(e => e.Codigo).Order().SequenceEqual(Enumerable.Range(1, 139).Select(i => $"MSCD-PROC-{i:0000}")) ||
            entries.Select(e => (e.Servicio, e.Nombre)).Distinct().Count() != 139 ||
            entries.Any(e => string.IsNullOrWhiteSpace(e.Nombre) || e.Nombre.Length > 100 ||
                string.IsNullOrWhiteSpace(e.Descripcion) || e.Descripcion.Length > 300 || e.Minutos is < 1 or > 1440 ||
                !ServicioCatalogoSeeder.Catalogo.Any(s => s.Nombre == e.Servicio)))
            throw new CatalogoValidationException("El catálogo fuente no cumple sus invariantes.");
    }

    public static async Task<bool> SeedAsync(ApplicationDbContext context)
    {
        ValidateCatalog();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var committed = false;
        try
        {
            var services = await context.ServiciosOdontologicos.AsNoTracking().ToListAsync();
            if (services.Count != 12 || ServicioCatalogoSeeder.Catalogo.Any(e =>
                services.Count(s => s.Nombre == e.Nombre && s.Estado &&
                    (!e.IdHistorico.HasValue || e.IdHistorico == s.ServicioOdontologicoId)) != 1))
                throw new CatalogoValidationException("Los doce servicios definitivos no están preparados correctamente.");
            var parents = services.ToDictionary(s => s.Nombre, s => s.ServicioOdontologicoId);
            var items = await context.SubserviciosOdontologicos.ToListAsync();
            if (items.Where(s => s.CodigoCatalogo != null).GroupBy(s => s.CodigoCatalogo, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                throw new CatalogoValidationException("Existen códigos duplicados.");

            foreach (var old in Historicos)
            {
                var item = items.SingleOrDefault(s => s.SubservicioOdontologicoId == old.Id);
                if (item is null || item.ServicioOdontologicoId != parents[old.Servicio])
                    throw new CatalogoValidationException("Una identidad histórica está ausente o tiene otro servicio padre.");
                if (old.Codigo != null && item.CodigoCatalogo == old.Codigo)
                    continue; // Se valida contra la entrada definitiva más abajo.
                // Los legados 2/4/5 conservan su clasificación; no identifica el procedimiento.
                if (item.Nombre != old.Nombre || item.DuracionEstimadaMinutos != old.Minutos ||
                    item.CodigoCatalogo != null || (old.Codigo != null && item.Clasificacion != null))
                    throw new CatalogoValidationException($"El subservicio histórico ID {old.Id} no coincide con la conciliación aprobada: verifica nombre, duración, código y clasificación cuando sea reutilizable.");
            }

            var selected = new Dictionary<SubservicioCatalogo.Entrada, SubservicioOdontologico?>();
            foreach (var entry in SubservicioCatalogo.Entradas)
            {
                var old = Historicos.SingleOrDefault(h => h.Codigo == entry.Codigo);
                var item = items.SingleOrDefault(s => s.CodigoCatalogo == entry.Codigo);
                if (item is not null && (item.ServicioOdontologicoId != parents[entry.Servicio] ||
                    item.Nombre != entry.Nombre || (old != null && item.SubservicioOdontologicoId != old.Id)))
                    throw new CatalogoValidationException("Un código identifica otro procedimiento o servicio.");
                item ??= old == null ? null : items.Single(s => s.SubservicioOdontologicoId == old.Id);
                // Consulta en BD para respetar la collation del proveedor, incluidos inactivos.
                var excludedId = item?.SubservicioOdontologicoId ?? 0;
                if (await context.SubserviciosOdontologicos.AnyAsync(s => s.ServicioOdontologicoId == parents[entry.Servicio] &&
                    s.Nombre == entry.Nombre && s.SubservicioOdontologicoId != excludedId))
                    throw new CatalogoValidationException("Existe una colisión de nombre en el servicio destino.");
                selected.Add(entry, item);
            }
            if (items.Any(s => !Historicos.Any(h => h.Id == s.SubservicioOdontologicoId) && !selected.Values.Contains(s)))
                throw new CatalogoValidationException("Existen procedimientos ajenos al catálogo aprobado; no se mezclarán catálogos.");

            var changed = false;
            foreach (var (entry, existing) in selected)
            {
                var item = existing;
                if (item is null)
                {
                    item = new SubservicioOdontologico { ServicioOdontologicoId = parents[entry.Servicio], CodigoCatalogo = entry.Codigo };
                    context.SubserviciosOdontologicos.Add(item);
                    changed = true;
                }
                changed |= item.Nombre != entry.Nombre || item.Descripcion != entry.Descripcion ||
                    item.Clasificacion != entry.Clasificacion || item.DuracionEstimadaMinutos != entry.Minutos ||
                    item.CodigoCatalogo != entry.Codigo || !item.Estado;
                item.Nombre = entry.Nombre;
                item.Descripcion = entry.Descripcion;
                item.Clasificacion = entry.Clasificacion;
                item.DuracionEstimadaMinutos = entry.Minutos;
                item.CodigoCatalogo = entry.Codigo;
                item.Estado = true;
            }
            foreach (var old in Historicos.Where(h => h.Codigo == null))
            {
                var item = items.Single(s => s.SubservicioOdontologicoId == old.Id);
                changed |= item.Estado;
                item.Estado = false;
            }
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
            committed = true;
            return changed;
        }
        finally
        {
            if (!committed)
            {
                try { await transaction.RollbackAsync(); }
                finally { context.ChangeTracker.Clear(); }
            }
        }
    }
}
