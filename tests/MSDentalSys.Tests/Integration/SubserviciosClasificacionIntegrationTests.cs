using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MSDentalSys.Data.Context;
using Xunit;

namespace MSDentalSys.Tests.Integration;

public partial class SubserviciosIntegrationTests
{
    [Theory]
    [InlineData("1", true)]
    [InlineData("2", true)]
    [InlineData("99", false)]
    [InlineData("manipulado", false)]
    [InlineData("", false)]
    public async Task Clasificacion_Http_EditHistorico_ValidaBinding(string value, bool valid)
    {
        using var factory = new CustomWebApplicationFactory();
        var id = await SeedAsync(factory);
        using var client = Client(factory, "Administrador");
        var html = await client.GetStringAsync($"/Subservicios/Edit/{id}");
        Assert.Contains("Sin clasificar", html);
        Assert.Contains("name=\"Clasificacion\"", html);
        Assert.Contains("Principal", html); Assert.Contains("Complementario", html);
        Assert.Contains("Sin clasificar", await client.GetStringAsync($"/Subservicios/Details/{id}"));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var item = await db.SubserviciosOdontologicos.AsNoTracking().SingleAsync();
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success);
        var response = await client.PostAsync($"/Subservicios/Edit/{id}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value),
            ["SubservicioOdontologicoId"] = id.ToString(),
            ["ServicioOdontologicoId"] = item.ServicioOdontologicoId.ToString(),
            ["Nombre"] = item.Nombre, ["DuracionEstimadaMinutos"] = "30", ["Clasificacion"] = value
        }));
        Assert.Equal(valid ? HttpStatusCode.Redirect : HttpStatusCode.OK, response.StatusCode);
        var stored = await db.SubserviciosOdontologicos.AsNoTracking().SingleAsync();
        if (valid)
        {
            Assert.Equal(int.Parse(value), (int?)stored.Clasificacion);
            Assert.Contains(stored.Clasificacion!.ToString()!, await client.GetStringAsync($"/Subservicios/Details/{id}"));
        }
        else Assert.Null(stored.Clasificacion);
    }
}
