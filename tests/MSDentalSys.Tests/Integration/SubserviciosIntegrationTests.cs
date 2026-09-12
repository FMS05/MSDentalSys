using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MSDentalSys.Data.Context;
using MSDentalSys.Data.Models;
using Xunit;

namespace MSDentalSys.Tests.Integration;

public partial class SubserviciosIntegrationTests
{
    [Theory]
    [InlineData("Administrador")]
    [InlineData("Recepcionista")]
    [InlineData("Odontologo")]
    public async Task Consulta_CatalogoYDetalle_Disponibles(string role)
    {
        using var factory = new CustomWebApplicationFactory();
        var id = await SeedAsync(factory);
        using var client = Client(factory, role);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Subservicios")).StatusCode);
        var detail = await client.GetAsync($"/Subservicios/Details/{id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Contains("Procedimiento HTTP", await detail.Content.ReadAsStringAsync());
        using var scope = factory.Services.CreateScope();
        var parent = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().ServiciosOdontologicos.SingleAsync();
        var service = await client.GetAsync($"/Servicios/Details/{parent.ServicioOdontologicoId}");
        Assert.Equal(HttpStatusCode.OK, service.StatusCode);
        Assert.Contains("Procedimiento HTTP", await service.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("Recepcionista")]
    [InlineData("Odontologo")]
    public async Task Administracion_OtrosRolesRechazados(string role)
    {
        using var factory = new CustomWebApplicationFactory();
        var id = await SeedAsync(factory); using var client = Client(factory, role);
        foreach (var route in new[] { "/Subservicios/Create", $"/Subservicios/Edit/{id}" })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(route)).StatusCode);
        foreach (var route in new[] { "/Subservicios/Create", $"/Subservicios/Edit/{id}", $"/Subservicios/Activate/{id}", $"/Subservicios/Deactivate/{id}", "/Subservicios/LoadInitialCatalog" })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(route, new FormUrlEncodedContent([]))).StatusCode);
    }

    [Fact]
    public async Task Administrador_FormulariosYAntiforgeryReal()
    {
        using var factory = new CustomWebApplicationFactory();
        var id = await SeedAsync(factory); using var client = Client(factory, "Administrador");
        foreach (var route in new[] { "/Subservicios/Create", $"/Subservicios/Edit/{id}" })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(route)).StatusCode);
        foreach (var route in new[] { "/Subservicios/Create", $"/Subservicios/Edit/{id}", $"/Subservicios/Activate/{id}", $"/Subservicios/Deactivate/{id}", "/Subservicios/LoadInitialCatalog" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(route, new FormUrlEncodedContent([]))).StatusCode);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var parent = await context.ServiciosOdontologicos.SingleAsync();
        var html = await client.GetStringAsync("/Subservicios/Create");
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success);
        var response = await client.PostAsync("/Subservicios/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value),
            ["ServicioOdontologicoId"] = parent.ServicioOdontologicoId.ToString(),
            ["Nombre"] = "Creado por HTTP", ["DuracionEstimadaMinutos"] = "45", ["Clasificacion"] = "1"
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.True(await context.SubserviciosOdontologicos.AnyAsync(s => s.Nombre == "Creado por HTTP"));
    }

    [Fact]
    public async Task Anonimo_RedireccionaLogin()
    {
        using var factory = new CustomWebApplicationFactory(); using var client = Client(factory, null);
        var response = await client.GetAsync("/Subservicios");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", response.Headers.Location!.OriginalString);
    }

    private static HttpClient Client(CustomWebApplicationFactory factory, string? role)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        if (role is not null) client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }
    private static async Task<int> SeedAsync(CustomWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var item = new SubservicioOdontologico { Nombre = "Procedimiento HTTP", DuracionEstimadaMinutos = 30,
            ServicioOdontologico = new ServicioOdontologico { Nombre = "Servicio HTTP" } };
        context.Add(item); await context.SaveChangesAsync(); return item.SubservicioOdontologicoId;
    }
}
