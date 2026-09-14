using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MSDentalSys.Data.Context;
using MSDentalSys.Data.Models;
using MSDentalSys.Web.Models.ViewModels;
using Xunit;

namespace MSDentalSys.Tests.Integration;

public class ServiciosLegacyIntegrationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Formularios_SinDuracion_NoRequierenNiAceptanModificarLegacy(bool edit, bool injectDuration)
    {
        using var factory = new CustomWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = new ServicioOdontologico { Nombre = "Servicio legacy", DuracionEstimadaMinutos = 123 };
        db.Add(service);
        await db.SaveChangesAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add("X-Test-Role", "Administrador");
        var route = edit ? $"/Servicios/Edit/{service.ServicioOdontologicoId}" : "/Servicios/Create";
        var html = await client.GetStringAsync(route);
        Assert.DoesNotContain("DuracionEstimadaMinutos", html);
        Assert.Null(typeof(ServicioFormViewModel).GetProperty("DuracionEstimadaMinutos"));
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success);
        var form = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value),
            ["Nombre"] = "Servicio actualizado", ["Descripcion"] = "Descripción actualizada"
        };
        if (edit) form["ServicioOdontologicoId"] = service.ServicioOdontologicoId.ToString();
        if (injectDuration) form["DuracionEstimadaMinutos"] = "999";
        var response = await client.PostAsync(route, new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var stored = await db.ServiciosOdontologicos.AsNoTracking().SingleAsync(s => s.Nombre == "Servicio actualizado");
        Assert.Equal("Descripción actualizada", stored.Descripcion);
        Assert.Equal(edit ? (int?)123 : null, stored.DuracionEstimadaMinutos);
    }

    [Fact]
    public async Task Consulta_OcultaDuracionLegacy_ConservaListadoYDuracionDeSubservicios()
    {
        using var factory = new CustomWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = new ServicioOdontologico { Nombre = "Servicio legacy", DuracionEstimadaMinutos = 123 };
        db.Add(new SubservicioOdontologico { Nombre = "Procedimiento visible", DuracionEstimadaMinutos = 45, ServicioOdontologico = service });
        await db.SaveChangesAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-Role", "Administrador");
        var index = WebUtility.HtmlDecode(await client.GetStringAsync("/Servicios"));
        var details = WebUtility.HtmlDecode(await client.GetStringAsync($"/Servicios/Details/{service.ServicioOdontologicoId}"));
        Assert.DoesNotContain("Duración estimada", index);
        Assert.DoesNotContain("Duración estimada", details);
        Assert.DoesNotContain("123 min", index);
        Assert.DoesNotContain("123 min", details);
        Assert.Contains("Procedimiento visible", details);
        Assert.Contains("45 min", details);
    }
}
