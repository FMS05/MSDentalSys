using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MSDentalSys.Data.Context;
using MSDentalSys.Data.InitialData;
using MSDentalSys.Data.Models;
using Xunit;

namespace MSDentalSys.Tests.Integration;

public class ServicioCatalogoTests
{
    private static async Task SeedHistory(ApplicationDbContext db)
    {
        string[] names = ["Periodoncia", "Odontología General", "Endodoncia", "Cirugía Bucal", "Rehabilitación Oral"];
        for (var i = 0; i < names.Length; i++)
            db.Add(new ServicioOdontologico { ServicioOdontologicoId = i + 1, Nombre = names[i], Estado = false, DuracionEstimadaMinutos = 77 });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Conciliacion_ConservaIdentidadesDatosYRelaciones_EsIdempotente()
    {
        using var factory = new CustomWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await SeedHistory(db);
        var sub = new SubservicioOdontologico { ServicioOdontologicoId = 1, Nombre = "Histórico", DuracionEstimadaMinutos = 45, CodigoCatalogo = "LEGACY" };
        db.Add(sub); await db.SaveChangesAsync();
        var cita = new Cita { ServicioOdontologicoId = 1, SubservicioOdontologicoId = sub.SubservicioOdontologicoId,
            DuracionProgramadaMinutos = 45, Paciente = new Paciente { Nombre = "Paciente", Apellido = "Prueba" },
            Odontologo = new ApplicationUser { UserName = "doctor", Nombre = "Doctor", Apellido = "Prueba" } };
        db.Add(cita); await db.SaveChangesAsync();
        var treatment = new Tratamiento { ServicioOdontologicoId = 1, AtencionOdontologica = new AtencionOdontologica
            { CitaId = cita.CitaId, PacienteId = cita.PacienteId, OdontologoId = cita.OdontologoId } };
        db.Add(treatment); await db.SaveChangesAsync();
        Assert.True(await ServicioCatalogoSeeder.SeedAsync(db));
        var rows = await db.ServiciosOdontologicos.AsNoTracking().OrderBy(s => s.ServicioOdontologicoId).ToListAsync();
        Assert.Equal(12, rows.Count);
        Assert.Equal(new[] { "Periodoncia", "Odontología general", "Endodoncia", "Cirugía oral", "Rehabilitación oral / Prótesis" }, rows.Take(5).Select(s => s.Nombre));
        Assert.All(rows, s => Assert.True(s.Estado));
        Assert.All(rows.Take(5), s => Assert.Equal(77, s.DuracionEstimadaMinutos));
        Assert.All(rows.Skip(5), s => Assert.Null(s.DuracionEstimadaMinutos));
        Assert.False(await ServicioCatalogoSeeder.SeedAsync(db));
        var repeated = await db.ServiciosOdontologicos.AsNoTracking().OrderBy(s => s.ServicioOdontologicoId).ToListAsync();
        Assert.Equal(rows.Select(s => (s.ServicioOdontologicoId, s.Nombre, s.FechaCreacion)), repeated.Select(s => (s.ServicioOdontologicoId, s.Nombre, s.FechaCreacion)));
        var stored = await db.SubserviciosOdontologicos.AsNoTracking().SingleAsync();
        Assert.Equal(sub.SubservicioOdontologicoId, stored.SubservicioOdontologicoId);
        Assert.Equal(1, stored.ServicioOdontologicoId); Assert.Equal("Histórico", stored.Nombre);
        Assert.Null(stored.Clasificacion); Assert.Equal("LEGACY", stored.CodigoCatalogo); Assert.Equal(45, stored.DuracionEstimadaMinutos);
        Assert.Equal(1, (await db.Citas.AsNoTracking().SingleAsync()).ServicioOdontologicoId);
        Assert.Equal(45, (await db.Citas.AsNoTracking().SingleAsync()).DuracionProgramadaMinutos);
        Assert.Equal(1, (await db.Tratamientos.AsNoTracking().SingleAsync()).ServicioOdontologicoId);
    }

    [Theory]
    [InlineData("identidad")]
    [InlineData("ausente")]
    [InlineData("duplicado")]
    [InlineData("inactivoDuplicado")]
    [InlineData("ajeno")]
    public async Task Incompatibilidad_AbortaSinCambiosParciales(string scenario)
    {
        using var factory = new CustomWebApplicationFactory(); using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); await SeedHistory(db);
        if (scenario == "identidad") (await db.ServiciosOdontologicos.FindAsync(4))!.Nombre = "Ajeno";
        else if (scenario == "ausente") db.Remove((await db.ServiciosOdontologicos.FindAsync(5))!);
        else db.Add(new ServicioOdontologico { Nombre = scenario == "ajeno" ? "Ajeno" : "Cirugía oral", Estado = scenario != "inactivoDuplicado" });
        await db.SaveChangesAsync();
        var before = await db.ServiciosOdontologicos.AsNoTracking().OrderBy(s => s.ServicioOdontologicoId).Select(s => new { s.ServicioOdontologicoId, s.Nombre, s.Estado }).ToListAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => ServicioCatalogoSeeder.SeedAsync(db));
        var after = await db.ServiciosOdontologicos.AsNoTracking().OrderBy(s => s.ServicioOdontologicoId).Select(s => new { s.ServicioOdontologicoId, s.Nombre, s.Estado }).ToListAsync();
        Assert.Equal(before, after); Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task ErrorDespuesDeGuardar_RollbackReal()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var interceptor = new FailAfterSave();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).AddInterceptors(interceptor).Options);
        await db.Database.EnsureCreatedAsync(); await SeedHistory(db);
        interceptor.Enabled = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => ServicioCatalogoSeeder.SeedAsync(db));
        Assert.Equal(5, await db.ServiciosOdontologicos.CountAsync());
        Assert.Equal("Cirugía Bucal", (await db.ServiciosOdontologicos.AsNoTracking().SingleAsync(s => s.ServicioOdontologicoId == 4)).Nombre);
        Assert.All(await db.ServiciosOdontologicos.AsNoTracking().ToListAsync(), s => Assert.False(s.Estado));
    }

    private sealed class FailAfterSave : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public bool Enabled { get; set; }
        public override ValueTask<int> SavedChangesAsync(Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (Enabled) throw new InvalidOperationException("Fallo de prueba después de persistir y antes del commit");
            return ValueTask.FromResult(result);
        }
    }

    [Theory]
    [InlineData("Administrador", HttpStatusCode.BadRequest)]
    [InlineData("Recepcionista", HttpStatusCode.Forbidden)]
    [InlineData("Odontologo", HttpStatusCode.Forbidden)]
    [InlineData(null, HttpStatusCode.Redirect)]
    public async Task Endpoint_Protegido(string? role, HttpStatusCode expected)
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        if (role is not null) client.DefaultRequestHeaders.Add("X-Test-Role", role);
        Assert.Equal(expected, (await client.PostAsync("/Servicios/PrepareCatalog", new FormUrlEncodedContent([]))).StatusCode);
    }

    [Fact]
    public async Task Administrador_PostReal_MensajesIdempotenciaYLegacyDeshabilitado()
    {
        using var factory = new CustomWebApplicationFactory(); using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); await SeedHistory(db);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Test-Role", "Administrador");
        var html = await client.GetStringAsync("/Servicios");
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\""); Assert.True(token.Success);
        var form = new Dictionary<string, string> { ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value) };
        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Servicios/PrepareCatalog", new FormUrlEncodedContent(form))).StatusCode);
            Assert.Contains(i == 0 ? "fue preparado correctamente" : "ya se encuentra actualizado", WebUtility.HtmlDecode(await client.GetStringAsync("/Servicios")));
        }
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Subservicios/LoadInitialCatalog", new FormUrlEncodedContent(form))).StatusCode);
        Assert.Empty(await db.SubserviciosOdontologicos.ToListAsync());
        Assert.DoesNotContain("action=\"/Subservicios/LoadInitialCatalog\"", await client.GetStringAsync("/Subservicios"));
    }
}
