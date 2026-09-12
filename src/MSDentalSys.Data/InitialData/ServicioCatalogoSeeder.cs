using System.Data;
using Microsoft.EntityFrameworkCore;
using MSDentalSys.Data.Context;
using MSDentalSys.Data.Models;

namespace MSDentalSys.Data.InitialData;

public static class ServicioCatalogoSeeder
{
    public sealed record Entrada(int? IdHistorico, string Nombre, string? NombreHistorico = null);
    public static IReadOnlyList<Entrada> Catalogo { get; } = Array.AsReadOnly<Entrada>([
        new(2, "Odontología general", "Odontología General"),
        new(null, "Odontología estética"),
        new(5, "Rehabilitación oral / Prótesis", "Rehabilitación Oral"),
        new(null, "Implantología"), new(null, "Ortodoncia"), new(1, "Periodoncia"),
        new(3, "Endodoncia"), new(4, "Cirugía oral", "Cirugía Bucal"),
        new(null, "Odontopediatría"), new(null, "Odontología preventiva"),
        new(null, "Odontología digital"),
        new(null, "Odontología para pacientes con necesidades especiales")
    ]);

    public static async Task<bool> SeedAsync(ApplicationDbContext context)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var committed = false;
        try
        {
            var services = await context.ServiciosOdontologicos.OrderBy(s => s.ServicioOdontologicoId).ToListAsync();
            var selected = new Dictionary<Entrada, ServicioOdontologico?>();
            foreach (var entry in Catalogo)
            {
                var matches = services.Where(s => Matches(s.Nombre, entry.Nombre) ||
                    (entry.NombreHistorico is not null && Matches(s.Nombre, entry.NombreHistorico))).ToList();
                if (matches.Count > 1)
                    throw new InvalidOperationException($"Hay servicios duplicados para '{entry.Nombre}'. Revisa el catálogo antes de continuar.");
                var item = matches.SingleOrDefault();
                if (entry.IdHistorico is int id && (item is null || item.ServicioOdontologicoId != id))
                    throw new InvalidOperationException($"El servicio histórico ID {id} no corresponde a '{entry.Nombre}'. No se aplicaron cambios.");
                selected.Add(entry, item);
            }
            if (services.Any(s => !selected.Values.Contains(s)))
                throw new InvalidOperationException("Existen servicios ajenos al catálogo esperado. Revisa sus identidades antes de continuar.");

            var changed = false;
            foreach (var (entry, item) in selected)
            {
                if (item is null)
                {
                    context.ServiciosOdontologicos.Add(new ServicioOdontologico { Nombre = entry.Nombre });
                    changed = true;
                }
                else
                {
                    changed |= item.Nombre != entry.Nombre || !item.Estado;
                    item.Nombre = entry.Nombre;
                    item.Estado = true;
                }
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

    private static bool Matches(string actual, string expected) =>
        string.Equals(actual.Trim(), expected, StringComparison.OrdinalIgnoreCase);
}
