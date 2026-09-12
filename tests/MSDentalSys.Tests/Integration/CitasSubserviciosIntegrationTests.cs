using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MSDentalSys.Data.Context;
using MSDentalSys.Data.Models;
using Xunit;

namespace MSDentalSys.Tests.Integration;

public partial class SubserviciosIntegrationTests
{
    [Theory]
    [InlineData(null, HttpStatusCode.Redirect)]
    [InlineData("Odontologo", HttpStatusCode.Forbidden)]
    [InlineData("Paciente", HttpStatusCode.Forbidden)]
    [InlineData("Administrador", HttpStatusCode.OK)]
    [InlineData("Recepcionista", HttpStatusCode.OK)]
    public async Task ParaCitas_AutorizaSoloCreadores(string? role, HttpStatusCode expected)
    {
        using var factory = new CustomWebApplicationFactory();
        await SeedAsync(factory);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var parent = await db.ServiciosOdontologicos.SingleAsync();
        using var client = Client(factory, role);
        var response = await client.GetAsync($"/Subservicios/ParaCitas?servicioOdontologicoId={parent.ServicioOdontologicoId}");
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task ParaCitas_FiltraOrdenaYExponeSoloDatosDeSeleccion()
    {
        using var factory = new CustomWebApplicationFactory();
        await SeedAsync(factory);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var parent = await db.ServiciosOdontologicos.SingleAsync();
        db.AddRange(
            new SubservicioOdontologico { Nombre = "A primero", ServicioOdontologicoId = parent.ServicioOdontologicoId, DuracionEstimadaMinutos = 90 },
            new SubservicioOdontologico { Nombre = "Inactivo", ServicioOdontologicoId = parent.ServicioOdontologicoId, Estado = false, DuracionEstimadaMinutos = 45 },
            new SubservicioOdontologico { Nombre = "Otro", ServicioOdontologico = new ServicioOdontologico { Nombre = "Otro padre" }, DuracionEstimadaMinutos = 45 });
        await db.SaveChangesAsync();
        using var client = Client(factory, "Recepcionista");
        using var json = JsonDocument.Parse(await client.GetStringAsync($"/Subservicios/ParaCitas?servicioOdontologicoId={parent.ServicioOdontologicoId}"));
        Assert.Equal(2, json.RootElement.GetArrayLength());
        Assert.Equal("A primero", json.RootElement[0].GetProperty("nombre").GetString());
        Assert.Equal(90, json.RootElement[0].GetProperty("duracionEstimadaMinutos").GetInt32());
        Assert.Equal(30, json.RootElement[1].GetProperty("duracionEstimadaMinutos").GetInt32());
        foreach (var item in json.RootElement.EnumerateArray())
            Assert.Equal(new[] { "duracionEstimadaMinutos", "nombre", "subservicioOdontologicoId" }, item.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Subservicios/ParaCitas?servicioOdontologicoId=999999")).StatusCode);
        parent.Estado = false; await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Subservicios/ParaCitas?servicioOdontologicoId={parent.ServicioOdontologicoId}")).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateHttp_ValidaRequerido_IgnoraDuracionCliente_YDetailsRenderiza(bool omitSubservice)
    {
        using var factory = new CustomWebApplicationFactory();
        var subId = await SeedAsync(factory);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var parent = await db.ServiciosOdontologicos.SingleAsync();
        var patient = new Paciente { Nombre = "Paciente", Apellido = "HTTP" };
        var dentist = new ApplicationUser { Id = "dentist-http", UserName = "dentist", Nombre = "Doctor", Apellido = "HTTP", Estado = true };
        var role = new Microsoft.AspNetCore.Identity.IdentityRole("Odontologo") { NormalizedName = "ODONTOLOGO" };
        db.AddRange(patient, dentist, role);
        db.UserRoles.Add(new Microsoft.AspNetCore.Identity.IdentityUserRole<string> { UserId = dentist.Id, RoleId = role.Id });
        await db.SaveChangesAsync();
        using var client = Client(factory, "Administrador");
        var html = await client.GetStringAsync("/Citas/Create");
        Assert.Contains("id=\"SubservicioOdontologicoId\"", html);
        Assert.Contains("data-endpoint=\"/Subservicios/ParaCitas\"", html);
        Assert.Contains("/js/citas-subservicios.js", html);
        var script = await client.GetStringAsync("/js/citas-subservicios.js");
        Assert.Contains("servicio.addEventListener('change'", script);
        Assert.Contains("pending?.abort()", script);
        Assert.Contains("subservicio.replaceChildren(new Option('Seleccione un subservicio', ''))", script);
        Assert.Contains("pending === request && servicio.value === serviceId", script);
        Assert.DoesNotContain("name=\"DuracionProgramadaMinutos\"", html);
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success);
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value),
            ["PacienteId"] = patient.PacienteId.ToString(), ["OdontologoId"] = dentist.Id,
            ["ServicioOdontologicoId"] = parent.ServicioOdontologicoId.ToString(),
            ["FechaHoraInicio"] = "2030-03-01T09:00", ["DuracionProgramadaMinutos"] = "999"
        };
        if (!omitSubservice) form["SubservicioOdontologicoId"] = subId.ToString();
        var response = await client.PostAsync("/Citas/Create", new FormUrlEncodedContent(form));
        Assert.Equal(omitSubservice ? HttpStatusCode.OK : HttpStatusCode.Redirect, response.StatusCode);
        if (omitSubservice) Assert.Empty(await db.Citas.ToListAsync());
        else
        {
            var cita = await db.Citas.SingleAsync();
            Assert.Equal(subId, cita.SubservicioOdontologicoId);
            Assert.Equal(30, cita.DuracionProgramadaMinutos);
            var details = WebUtility.HtmlDecode(await client.GetStringAsync($"/Citas/Details/{cita.CitaId}"));
            Assert.Contains("Procedimiento HTTP", details);
            Assert.Contains("30 min", details);
            cita.SubservicioOdontologicoId = null; cita.DuracionProgramadaMinutos = null;
            await db.SaveChangesAsync();
            Assert.Contains("No registrado", await client.GetStringAsync($"/Citas/Details/{cita.CitaId}"));
        }
    }
}
