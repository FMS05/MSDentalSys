using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using MSDentalSys.Data.Context;
using MSDentalSys.Data.InitialData;
using MSDentalSys.Data.Models;
using Xunit;

namespace MSDentalSys.Tests.Integration;

public class SubservicioCatalogoTests
{
    [Theory]
    [InlineData(4, ClasificacionSubservicio.Principal)]
    [InlineData(2, ClasificacionSubservicio.Complementario)]
    [InlineData(5, ClasificacionSubservicio.Principal)]
    public async Task LegacyClasificado_SePreservaInactivo_YCargaCompletaEsIdempotente(int id, ClasificacionSubservicio clasificacion)
    {
        using var factory = new CustomWebApplicationFactory(); using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); await SeedHistory(db);
        var legacy = await db.SubserviciosOdontologicos.SingleAsync(s => s.SubservicioOdontologicoId == id);
        legacy.Clasificacion = clasificacion;
        await db.SaveChangesAsync();
        var expected = (legacy.SubservicioOdontologicoId, legacy.ServicioOdontologicoId, legacy.Nombre,
            legacy.Descripcion, legacy.DuracionEstimadaMinutos, legacy.CodigoCatalogo, legacy.Clasificacion, legacy.FechaCreacion);
        if (id == 4)
        {
            Assert.Equal(4, legacy.ServicioOdontologicoId); Assert.Equal("Extracción dental simple", legacy.Nombre);
            Assert.Equal(45, legacy.DuracionEstimadaMinutos); Assert.Null(legacy.CodigoCatalogo); Assert.True(legacy.Estado);
        }
        Assert.True(await SubservicioCatalogoSeeder.SeedAsync(db));
        Assert.False(await SubservicioCatalogoSeeder.SeedAsync(db));
        var stored = await db.SubserviciosOdontologicos.AsNoTracking().SingleAsync(s => s.SubservicioOdontologicoId == id);
        Assert.Equal(expected, (stored.SubservicioOdontologicoId, stored.ServicioOdontologicoId, stored.Nombre,
            stored.Descripcion, stored.DuracionEstimadaMinutos, stored.CodigoCatalogo, stored.Clasificacion, stored.FechaCreacion));
        Assert.False(stored.Estado);
        var rows = await db.SubserviciosOdontologicos.AsNoTracking().ToListAsync();
        Assert.Equal(142, rows.Count);
        var definitive = rows.Where(s => s.CodigoCatalogo?.StartsWith("MSCD-PROC-", StringComparison.Ordinal) == true).ToList();
        Assert.Equal(139, definitive.Count); Assert.All(definitive, s => Assert.True(s.Estado));
        Assert.Equal(85, definitive.Count(s => s.Clasificacion == ClasificacionSubservicio.Principal));
        Assert.Equal(54, definitive.Count(s => s.Clasificacion == ClasificacionSubservicio.Complementario));
        Assert.Equal(3, rows.Count(s => s.CodigoCatalogo == null && !s.Estado));
    }

    private static async Task SeedHistory(ApplicationDbContext db)
    {
        string[] names = ["Periodoncia", "Odontología general", "Endodoncia", "Cirugía oral", "Rehabilitación oral / Prótesis"];
        for (var i = 0; i < 5; i++) db.Add(new ServicioOdontologico { ServicioOdontologicoId = i + 1, Nombre = names[i] });
        await db.SaveChangesAsync(); await ServicioCatalogoSeeder.SeedAsync(db);
        string[] procedures = ["Consulta y evaluación general", "Raspado y pulido dental", "Tratamiento de conducto", "Extracción dental simple", "Colocación de corona dental"];
        int[] parents = [2, 1, 3, 4, 5]; int[] minutes = [30, 45, 90, 45, 45];
        for (var i = 0; i < 5; i++) db.Add(new SubservicioOdontologico { SubservicioOdontologicoId = i + 1,
            ServicioOdontologicoId = parents[i], Nombre = procedures[i], DuracionEstimadaMinutos = minutes[i], Descripcion = "Descripción histórica" });
        await db.SaveChangesAsync();
    }

    [Fact]
    public void Fuente_CoincideExactamenteConEspecificacion()
    {
        SubservicioCatalogoSeeder.ValidateCatalog();
        var canonical = string.Join("\n", SubservicioCatalogo.Entradas.Select(e => $"{e.Codigo}|{e.Servicio}|{e.Nombre}|{e.Clasificacion}|{e.Minutos}|{e.Descripcion}"));
        // Hash calculado independientemente a partir del documento aprobado, no del archivo C#.
        Assert.Equal("D407B6E4560D7D5F28DA0919567CC6A126C74813265B79E66145B3E8F98EEFAE", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));
    }

    [Theory]
    [InlineData(1, 14, "Odontología general")]
    [InlineData(15, 27, "Odontología estética")]
    [InlineData(28, 39, "Rehabilitación oral / Prótesis")]
    [InlineData(40, 49, "Implantología")]
    [InlineData(50, 62, "Ortodoncia")]
    [InlineData(63, 75, "Periodoncia")]
    [InlineData(76, 85, "Endodoncia")]
    [InlineData(86, 96, "Cirugía oral")]
    [InlineData(97, 109, "Odontopediatría")]
    [InlineData(110, 120, "Odontología preventiva")]
    [InlineData(121, 130, "Odontología digital")]
    [InlineData(131, 139, "Odontología para pacientes con necesidades especiales")]
    public void Fuente_LimitesYClasificacionesPorServicio(int first, int last, string parent)
    {
        var entries = SubservicioCatalogo.Entradas.Where(e => e.Servicio == parent).ToList();
        Assert.Equal($"MSCD-PROC-{first:0000}", entries.First().Codigo);
        Assert.Equal($"MSCD-PROC-{last:0000}", entries.Last().Codigo);
        Assert.Equal(last - first + 1, entries.Count);
        Assert.Equal(ClasificacionSubservicio.Principal, entries.First().Clasificacion);
        Assert.Equal(ClasificacionSubservicio.Complementario, entries.Last().Clasificacion);
    }

    [Fact]
    public async Task CargaExacta_Idempotencia_HistoricosYSnapshots()
    {
        using var factory = new CustomWebApplicationFactory(); using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); await SeedHistory(db);
        var before = await db.SubserviciosOdontologicos.AsNoTracking().OrderBy(s => s.SubservicioOdontologicoId).ToListAsync();
        var patient = new Paciente { Nombre = "Paciente", Apellido = "Prueba" };
        var dentist = new ApplicationUser { UserName = "doctor", Nombre = "Doctor", Apellido = "Prueba" };
        foreach (var old in before) db.Add(new Cita { Paciente = patient, Odontologo = dentist, ServicioOdontologicoId = old.ServicioOdontologicoId,
            SubservicioOdontologicoId = old.SubservicioOdontologicoId, DuracionProgramadaMinutos = 20,
            FechaHoraInicio = new DateTime(2030, 1, 1, old.SubservicioOdontologicoId, 0, 0) });
        await db.SaveChangesAsync();
        Assert.True(await SubservicioCatalogoSeeder.SeedAsync(db));
        var rows = await db.SubserviciosOdontologicos.AsNoTracking().Include(s => s.ServicioOdontologico).OrderBy(s => s.SubservicioOdontologicoId).ToListAsync();
        Assert.Equal(142, rows.Count); Assert.Equal(139, rows.Count(s => s.Estado));
        Assert.Equal(85, rows.Count(s => s.Clasificacion == ClasificacionSubservicio.Principal));
        Assert.Equal(54, rows.Count(s => s.Clasificacion == ClasificacionSubservicio.Complementario));
        Assert.Equal("MSCD-PROC-0001", rows.Single(s => s.SubservicioOdontologicoId == 1).CodigoCatalogo);
        Assert.Equal("MSCD-PROC-0076", rows.Single(s => s.SubservicioOdontologicoId == 3).CodigoCatalogo);
        foreach (var entry in SubservicioCatalogo.Entradas)
        {
            var row = Assert.Single(rows, s => s.CodigoCatalogo == entry.Codigo);
            Assert.Equal(entry.Nombre, row.Nombre); Assert.Equal(entry.Descripcion, row.Descripcion);
            Assert.Equal(entry.Minutos, row.DuracionEstimadaMinutos); Assert.Equal(entry.Clasificacion, row.Clasificacion);
            Assert.Equal(entry.Servicio, row.ServicioOdontologico.Nombre); Assert.True(row.Estado);
        }
        foreach (var id in new[] { 2, 4, 5 })
        {
            var old = before.Single(s => s.SubservicioOdontologicoId == id); var row = rows.Single(s => s.SubservicioOdontologicoId == id);
            Assert.False(row.Estado); Assert.Null(row.CodigoCatalogo); Assert.Null(row.Clasificacion);
            Assert.Equal(old.Nombre, row.Nombre); Assert.Equal(old.Descripcion, row.Descripcion);
            Assert.Equal(old.DuracionEstimadaMinutos, row.DuracionEstimadaMinutos); Assert.Equal(old.FechaCreacion, row.FechaCreacion);
        }
        Assert.False(await SubservicioCatalogoSeeder.SeedAsync(db));
        Assert.Equal(rows.Select(s => (s.SubservicioOdontologicoId, s.CodigoCatalogo, s.FechaCreacion)),
            (await db.SubserviciosOdontologicos.AsNoTracking().OrderBy(s => s.SubservicioOdontologicoId).ToListAsync()).Select(s => (s.SubservicioOdontologicoId, s.CodigoCatalogo, s.FechaCreacion)));
        var citas = await db.Citas.AsNoTracking().Include(c => c.SubservicioOdontologico).ToListAsync();
        Assert.Equal(5, citas.Count); Assert.All(citas, c => { Assert.NotNull(c.SubservicioOdontologico); Assert.Equal(20, c.DuracionProgramadaMinutos); });
        var edited = await db.SubserviciosOdontologicos.SingleAsync(s => s.CodigoCatalogo == "MSCD-PROC-0001");
        edited.Descripcion = "Cambio manual"; edited.Clasificacion = ClasificacionSubservicio.Complementario; edited.DuracionEstimadaMinutos = 99; edited.Estado = false;
        await db.SaveChangesAsync(); Assert.True(await SubservicioCatalogoSeeder.SeedAsync(db));
        Assert.Equal(30, edited.DuracionEstimadaMinutos); Assert.True(edited.Estado);
        Assert.All(await db.Citas.AsNoTracking().ToListAsync(), c => Assert.Equal(20, c.DuracionProgramadaMinutos));
    }

    [Theory]
    [InlineData("padre")]
    [InlineData("nombre")]
    [InlineData("codigoDuplicado")]
    [InlineData("historico")]
    [InlineData("servicioInactivo")]
    [InlineData("ajeno")]
    [InlineData("significado")]
    public async Task Colisiones_AbortanSinCambios(string scenario)
    {
        using var factory = new CustomWebApplicationFactory(); using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); await SeedHistory(db);
        if (scenario == "historico") (await db.SubserviciosOdontologicos.FindAsync(1))!.Nombre = "Ajeno";
        else if (scenario == "servicioInactivo") (await db.ServiciosOdontologicos.FindAsync(1))!.Estado = false;
        else
        {
            db.Add(new SubservicioOdontologico { ServicioOdontologicoId = scenario == "padre" ? 1 : 2,
                Nombre = scenario is "padre" or "nombre" ? "Limpiezas dentales y profilaxis" : "Ajeno",
                DuracionEstimadaMinutos = 45, Estado = false,
            CodigoCatalogo = scenario is "padre" or "significado" or "codigoDuplicado" ? "MSCD-PROC-0002" : null });
        }
        await db.SaveChangesAsync();
        if (scenario == "codigoDuplicado")
        {
            // El índice existente impide que un código duplicado llegue a persistirse.
            db.Add(new SubservicioOdontologico { ServicioOdontologicoId = 2, Nombre = "Otro duplicado", DuracionEstimadaMinutos = 45, CodigoCatalogo = "MSCD-PROC-0002" });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();
        }
        var before = await Snapshot(db);
        await Assert.ThrowsAsync<SubservicioCatalogoSeeder.CatalogoValidationException>(() => SubservicioCatalogoSeeder.SeedAsync(db));
        Assert.Equal(before, await Snapshot(db)); Assert.Empty(db.ChangeTracker.Entries());
    }

    private static async Task<string> Snapshot(ApplicationDbContext db) => JsonSerializer.Serialize(await db.SubserviciosOdontologicos.AsNoTracking()
        .OrderBy(s => s.SubservicioOdontologicoId).Select(s => new { s.SubservicioOdontologicoId, s.ServicioOdontologicoId, s.Nombre, s.Descripcion, s.Estado, s.CodigoCatalogo, s.Clasificacion, s.DuracionEstimadaMinutos }).ToListAsync());

    [Fact]
    public async Task FalloTrasSaveChanges_RollbackCompleto()
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var interceptor = new FailAfterSave();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).AddInterceptors(interceptor).Options);
        await db.Database.EnsureCreatedAsync(); await SeedHistory(db); var before = await Snapshot(db);
        interceptor.Enabled = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => SubservicioCatalogoSeeder.SeedAsync(db));
        Assert.Equal(before, await Snapshot(db));
    }
    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public bool Enabled { get; set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        { if (Enabled) throw new InvalidOperationException("Fallo de prueba antes del commit"); return ValueTask.FromResult(result); }
    }

    [Theory]
    [InlineData("Administrador", HttpStatusCode.BadRequest)]
    [InlineData("Recepcionista", HttpStatusCode.Forbidden)]
    [InlineData("Odontologo", HttpStatusCode.Forbidden)]
    [InlineData(null, HttpStatusCode.Redirect)]
    public async Task Post_RequiereAdminYAntiforgery(string? role, HttpStatusCode expected)
    {
        using var factory = new CustomWebApplicationFactory(); using var client = Client(factory, role);
        Assert.Equal(expected, (await client.PostAsync("/Subservicios/PrepareDefinitiveCatalog", new FormUrlEncodedContent([]))).StatusCode);
    }

    [Theory]
    [InlineData("Administrador")]
    [InlineData("Recepcionista")]
    [InlineData("Odontologo")]
    public async Task Http_CargaAdmin_ConsultaYSeleccionSinLegacy(string role)
    {
        using var factory = new CustomWebApplicationFactory(); using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); await SeedHistory(db);
        using var admin = Client(factory, "Administrador");
        var html = await admin.GetStringAsync("/Subservicios");
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\""); Assert.True(token.Success);
        for (var i = 0; i < 2; i++)
        {
            var response = await admin.PostAsync("/Subservicios/PrepareDefinitiveCatalog", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value) }));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains(i == 0 ? "fue cargado correctamente" : "ya se encuentra actualizado", WebUtility.HtmlDecode(await admin.GetStringAsync("/Subservicios")));
        }
        using var client = Client(factory, role);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Subservicios")).StatusCode);
        Assert.Contains("Raspado y pulido dental", await client.GetStringAsync("/Subservicios/Details/2"));
        if (role == "Odontologo")
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Subservicios/ParaCitas?servicioOdontologicoId=1")).StatusCode);
        else
        {
            foreach (var parent in await db.ServiciosOdontologicos.AsNoTracking().ToListAsync())
            {
                using var json = JsonDocument.Parse(await client.GetStringAsync($"/Subservicios/ParaCitas?servicioOdontologicoId={parent.ServicioOdontologicoId}"));
                var expected = SubservicioCatalogo.Entradas.Where(e => e.Servicio == parent.Nombre).ToList();
                Assert.Equal(expected.Count, json.RootElement.GetArrayLength());
                foreach (var row in json.RootElement.EnumerateArray())
                {
                    var entry = Assert.Single(expected, e => e.Nombre == row.GetProperty("nombre").GetString());
                    Assert.Equal(entry.Minutos, row.GetProperty("duracionEstimadaMinutos").GetInt32());
                    Assert.DoesNotContain(row.GetProperty("subservicioOdontologicoId").GetInt32(), new[] { 2, 4, 5 });
                }
            }
        }
    }

    private static HttpClient Client(CustomWebApplicationFactory factory, string? role)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        if (role != null) client.DefaultRequestHeaders.Add("X-Test-Role", role); return client;
    }
}
