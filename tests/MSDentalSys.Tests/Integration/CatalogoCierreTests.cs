using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MSDentalSys.Tests.Integration;

public class CatalogoCierreTests
{
    [Theory]
    [InlineData("Administrador")]
    [InlineData("Recepcionista")]
    [InlineData("Odontologo")]
    [InlineData(null)]
    public async Task RutasTecnicas_NoExisten(string? role)
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        if (role != null) client.DefaultRequestHeaders.Add("X-Test-Role", role);
        var actions = factory.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items.OfType<ControllerActionDescriptor>();
        Assert.DoesNotContain(actions, a => new[] { "PrepareCatalog", "PrepareDefinitiveCatalog", "LoadInitialCatalog" }.Contains(a.ActionName));
        foreach (var path in new[] { "/Servicios/PrepareCatalog", "/Subservicios/PrepareDefinitiveCatalog", "/Subservicios/LoadInitialCatalog" })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync(path, new FormUrlEncodedContent([]))).StatusCode);
        }
    }

    [Theory]
    [InlineData("Servicios", "Nuevo servicio")]
    [InlineData("Subservicios", "Nuevo subservicio")]
    public async Task Index_SinControlesDeImplantacion_ConservaCreacion(string controller, string createText)
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-Role", "Administrador");
        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/{controller}"));
        Assert.Contains(createText, html);
        Assert.Contains($"/{controller}/Create", html);
        foreach (var text in new[] { "PrepareCatalog", "PrepareDefinitiveCatalog", "LoadInitialCatalog", "Preparar los 12", "Cargar catálogo odontológico definitivo", "Carga explícita", "carga provisional" })
            Assert.DoesNotContain(text, html);
    }
}
