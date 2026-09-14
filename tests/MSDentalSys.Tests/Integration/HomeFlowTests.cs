using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MSDentalSys.Data.Context;
using MSDentalSys.Data.Models;
using Xunit;

namespace MSDentalSys.Tests.Integration;

public class HomeFlowTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory factory;
    public HomeFlowTests(CustomWebApplicationFactory factory) => this.factory = factory;

    [Theory]
    [InlineData(null)]
    [InlineData("Administrador")]
    [InlineData("Recepcionista")]
    [InlineData("Odontologo")]
    public async Task Raiz_RedirigeAlDestinoCorrectoSinBucle(string? role)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (role is not null)
        {
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = new ApplicationUser { Id = $"integration-{role.ToLowerInvariant()}",
                UserName = $"{role}@example.test", Nombre = "Usuario", Apellido = role };
            var identityRole = new IdentityRole(role) { NormalizedName = role.ToUpperInvariant() };
            db.Users.Add(user);
            db.Roles.Add(identityRole);
            db.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = identityRole.Id });
            await db.SaveChangesAsync();
        }
        var response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(role is null ? "/Account/Login" : "/Dashboard", response.Headers.Location!.OriginalString);
        var destination = await client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, destination.StatusCode);
        var html = await destination.Content.ReadAsStringAsync();
        Assert.DoesNotContain("/Home/Privacy", html);
        Assert.DoesNotContain("building Web apps", html);
        var login = await client.GetAsync("/Account/Login");
        Assert.Equal(role is null ? HttpStatusCode.OK : HttpStatusCode.Redirect, login.StatusCode);
        if (role is not null) Assert.Equal("/Dashboard", login.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Privacy_RetiradaYErrorConservadoSinInstruccionesTecnicas()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Home/Privacy")).StatusCode);
        var error = await client.GetAsync("/Home/Error");
        Assert.Equal(HttpStatusCode.OK, error.StatusCode);
        var html = WebUtility.HtmlDecode(await error.Content.ReadAsStringAsync());
        Assert.Contains("No se pudo completar la solicitud.", html);
        Assert.Contains("Identificador de solicitud:", html);
        Assert.DoesNotContain("ASPNETCORE_ENVIRONMENT", html);
        Assert.DoesNotContain("Development Mode", html);
        Assert.DoesNotContain("StackTrace", html);
        Assert.True(error.Headers.CacheControl!.NoStore);
    }
}
