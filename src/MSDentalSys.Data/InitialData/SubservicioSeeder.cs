using Microsoft.EntityFrameworkCore;
using MSDentalSys.Data.Context;
using MSDentalSys.Data.Models;

namespace MSDentalSys.Data.InitialData;

public static class SubservicioSeeder
{
    private const int MaxAttempts = 3;

    public sealed record Entrada(string Codigo, string Servicio, string Nombre, int Minutos);
    public static IReadOnlyList<Entrada> Catalogo { get; } = Array.AsReadOnly<Entrada>([
        new("SUB-01-01", "Odontología General", "Consulta y evaluación general", 30),
        new("SUB-01-02", "Odontología General", "Profilaxis / limpieza dental", 45),
        new("SUB-01-03", "Odontología General", "Restauración dental", 45),
        new("SUB-01-04", "Odontología General", "Extracción simple", 45),
        new("SUB-02-01", "Odontología Estética", "Evaluación estética", 30),
        new("SUB-02-02", "Odontología Estética", "Blanqueamiento dental", 60),
        new("SUB-02-03", "Odontología Estética", "Resina estética", 45),
        new("SUB-02-04", "Odontología Estética", "Carillas dentales", 60),
        new("SUB-03-01", "Endodoncia", "Evaluación endodóntica", 30),
        new("SUB-03-02", "Endodoncia", "Tratamiento de conducto unirradicular", 60),
        new("SUB-03-03", "Endodoncia", "Tratamiento de conducto multirradicular", 90),
        new("SUB-03-04", "Endodoncia", "Retratamiento endodóntico", 90),
        new("SUB-04-01", "Cirugía Bucal", "Evaluación quirúrgica", 30),
        new("SUB-04-02", "Cirugía Bucal", "Extracción quirúrgica", 60),
        new("SUB-04-03", "Cirugía Bucal", "Extracción de tercer molar", 90),
        new("SUB-04-04", "Cirugía Bucal", "Control postoperatorio", 30),
        new("SUB-05-01", "Ortodoncia", "Evaluación ortodóncica", 30),
        new("SUB-05-02", "Ortodoncia", "Colocación de brackets", 90),
        new("SUB-05-03", "Ortodoncia", "Ajuste de ortodoncia", 45),
        new("SUB-05-04", "Ortodoncia", "Retiro de brackets", 60),
        new("SUB-05-05", "Ortodoncia", "Evaluación/colocación de retenedores", 45),
        new("SUB-06-01", "Odontopediatría", "Consulta odontopediátrica", 30),
        new("SUB-06-02", "Odontopediatría", "Profilaxis infantil", 30),
        new("SUB-06-03", "Odontopediatría", "Aplicación de flúor", 30),
        new("SUB-06-04", "Odontopediatría", "Sellantes dentales", 30),
        new("SUB-06-05", "Odontopediatría", "Restauración pediátrica", 45),
        new("SUB-06-06", "Odontopediatría", "Extracción de diente temporal", 30),
        new("SUB-07-01", "Periodoncia", "Evaluación periodontal", 30),
        new("SUB-07-02", "Periodoncia", "Raspado y alisado radicular", 60),
        new("SUB-07-03", "Periodoncia", "Mantenimiento periodontal", 45),
        new("SUB-07-04", "Periodoncia", "Tratamiento periodontal", 60),
        new("SUB-08-01", "Rehabilitación Oral", "Evaluación protésica", 30),
        new("SUB-08-02", "Rehabilitación Oral", "Preparación para corona", 60),
        new("SUB-08-03", "Rehabilitación Oral", "Colocación de corona", 45),
        new("SUB-08-04", "Rehabilitación Oral", "Prótesis parcial", 60),
        new("SUB-08-05", "Rehabilitación Oral", "Prótesis total", 60),
        new("SUB-09-01", "Diseño de Sonrisa", "Evaluación estética de sonrisa", 30),
        new("SUB-09-02", "Diseño de Sonrisa", "Planificación de sonrisa", 60),
        new("SUB-09-03", "Diseño de Sonrisa", "Mock-up / prueba estética", 60),
        new("SUB-09-04", "Diseño de Sonrisa", "Procedimiento estético de sonrisa", 90),
        new("SUB-10-01", "Implantología", "Evaluación para implante", 30),
        new("SUB-10-02", "Implantología", "Planificación de implante", 45),
        new("SUB-10-03", "Implantología", "Colocación de implante", 90),
        new("SUB-10-04", "Implantología", "Control postoperatorio", 30),
        new("SUB-10-05", "Implantología", "Colocación de corona sobre implante", 60),
    ]);

    public static async Task<int> SeedAsync(ApplicationDbContext context)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                return await SeedAttemptAsync(context);
            }
            catch (DbUpdateException ex) when (IsCatalogRace(ex))
            {
                context.ChangeTracker.Clear();
                if (await CatalogoCompletoAsync(context))
                    return 0;
                if (attempt == MaxAttempts)
                    throw;
                await Task.Delay(25 * attempt);
            }
        }

        throw new InvalidOperationException("No se pudo completar la carga del catálogo.");
    }

    private static async Task<int> SeedAttemptAsync(ApplicationDbContext context)
    {
        await using var transaction = await context.Database.BeginTransactionAsync();
        var committed = false;
        try
        {
            var added = 0;
            foreach (var entry in Catalogo)
            {
                var known = await context.SubserviciosOdontologicos.SingleOrDefaultAsync(s => s.CodigoCatalogo == entry.Codigo);
                var parents = await context.ServiciosOdontologicos.Where(s => s.Nombre == entry.Servicio).ToListAsync();
                if (parents.Count != 1)
                    throw new InvalidOperationException($"El servicio '{entry.Servicio}' debe existir una sola vez; encontrados: {parents.Count}. No se cargó el catálogo.");
                var parent = parents[0];
                if (known is not null)
                {
                    if (known.ServicioOdontologicoId != parent.ServicioOdontologicoId)
                        throw new InvalidOperationException($"Inconsistencia del catálogo: el código '{entry.Codigo}' está asociado al servicio ID {known.ServicioOdontologicoId}, pero la entrada espera el servicio '{entry.Servicio}' ID {parent.ServicioOdontologicoId}.");
                    continue;
                }
                if (!parent.Estado)
                    throw new InvalidOperationException($"El servicio '{entry.Servicio}' está inactivo. No se cargó el catálogo.");
                var existing = await context.SubserviciosOdontologicos.SingleOrDefaultAsync(s =>
                    s.ServicioOdontologicoId == parent.ServicioOdontologicoId && s.Nombre == entry.Nombre);
                if (existing is not null)
                {
                    if (existing.CodigoCatalogo is not null)
                        throw new InvalidOperationException($"El subservicio '{entry.Nombre}' ya corresponde a otra entrada del catálogo.");
                    existing.CodigoCatalogo = entry.Codigo;
                }
                else
                {
                    context.SubserviciosOdontologicos.Add(new SubservicioOdontologico
                    {
                        ServicioOdontologicoId = parent.ServicioOdontologicoId,
                        Nombre = entry.Nombre, DuracionEstimadaMinutos = entry.Minutos,
                        CodigoCatalogo = entry.Codigo
                    });
                    added++;
                }
            }
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
            committed = true;
            return added;
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

    private static async Task<bool> CatalogoCompletoAsync(ApplicationDbContext context)
    {
        foreach (var entry in Catalogo)
        {
            var known = await context.SubserviciosOdontologicos
                .AsNoTracking()
                .SingleOrDefaultAsync(s => s.CodigoCatalogo == entry.Codigo);
            if (known is null)
                return false;

            var parentId = await context.ServiciosOdontologicos
                .Where(s => s.Nombre == entry.Servicio)
                .Select(s => (int?)s.ServicioOdontologicoId)
                .SingleOrDefaultAsync();
            if (parentId is null || known.ServicioOdontologicoId != parentId.Value)
                return false;
        }

        return true;
    }

    private static bool IsCatalogRace(DbUpdateException exception)
    {
        var message = exception.InnerException?.Message ?? exception.Message;
        return message.Contains("IX_SubserviciosOdontologicos_CodigoCatalogo", StringComparison.Ordinal) ||
            message.Contains("UX_Subservicios_Servicio_Nombre", StringComparison.Ordinal) ||
            message.Contains("SubserviciosOdontologicos.CodigoCatalogo", StringComparison.Ordinal) ||
            message.Contains("SubserviciosOdontologicos.ServicioOdontologicoId, SubserviciosOdontologicos.Nombre", StringComparison.Ordinal);
    }
}
