using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MSDentalSys.Data.Models;
using Xunit;

namespace MSDentalSys.Tests.Integration;

public class SessionRevocationTests
{
    private const string Password = "Test1234!";
    private const string AdminEmail = "admin@msdentalsys.local";
    private const string UserEmail = "sesion@example.test";

    [Fact]
    public async Task Desactivar_RevocaCookiesDeDosNavegadoresTrasIntervalo()
    {
        using var factory = new IdentityCookieWebApplicationFactory();
        var userId = await SeedUsersAsync(factory);
        using var admin = CreateClient(factory);
        using var first = CreateClient(factory);
        using var second = CreateClient(factory);
        await LoginAsync(admin, AdminEmail);
        await LoginAsync(first, UserEmail);
        await LoginAsync(second, UserEmail);
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/Pacientes/Create")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/Pacientes/Create")).StatusCode);

        var response = await PostFormAsync(admin, "/Usuarios", $"/Usuarios/Deactivate/{userId}", []);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        // La revocación es periódica, no una comprobación de BD en cada petición.
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/Pacientes/Create")).StatusCode);
        factory.Clock.Advance(TimeSpan.FromSeconds(61));

        AssertLoginRedirect(await first.GetAsync("/Pacientes/Create"));
        AssertLoginRedirect(await second.GetAsync("/Pacientes/Create"));
    }

    [Fact]
    public async Task CambiarRol_RevocaCookieAnteriorYNuevoLoginUsaPermisosActuales()
    {
        using var factory = new IdentityCookieWebApplicationFactory();
        var userId = await SeedUsersAsync(factory);
        using var admin = CreateClient(factory);
        using var user = CreateClient(factory);
        await LoginAsync(admin, AdminEmail);
        await LoginAsync(user, UserEmail);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/Pacientes/Create")).StatusCode);

        var response = await PostFormAsync(admin, $"/Usuarios/Edit/{userId}", $"/Usuarios/Edit/{userId}",
        [
            new("Id", userId), new("Email", UserEmail), new("Nombre", "Usuario"),
            new("Apellido", "Prueba"), new("Rol", "Odontologo")
        ]);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        factory.Clock.Advance(TimeSpan.FromSeconds(61));

        AssertLoginRedirect(await user.GetAsync("/Pacientes/Create"));
        await LoginAsync(user, UserEmail);
        var forbidden = await user.GetAsync("/Pacientes/Create");
        Assert.Equal(HttpStatusCode.Redirect, forbidden.StatusCode);
        Assert.Equal("/Account/AccessDenied", new Uri(user.BaseAddress!, forbidden.Headers.Location!).AbsolutePath);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/Servicios")).StatusCode);
    }

    [Fact]
    public async Task UsuarioActivoSinCambios_ConservaSesionTrasRevalidacion()
    {
        using var factory = new IdentityCookieWebApplicationFactory();
        await SeedUsersAsync(factory);
        using var user = CreateClient(factory);
        await LoginAsync(user, UserEmail);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/Pacientes/Create")).StatusCode);

        factory.Clock.Advance(TimeSpan.FromSeconds(61));

        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/Pacientes/Create")).StatusCode);
    }

    private static HttpClient CreateClient(IdentityCookieWebApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });

    private static async Task LoginAsync(HttpClient client, string email)
    {
        var response = await PostFormAsync(client, "/Account/Login", "/Account/Login",
            [new("Email", email), new("Password", Password), new("RememberMe", "true")]);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Dashboard", response.Headers.Location!.OriginalString);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), value =>
            value.StartsWith(".AspNetCore.Identity.Application=", StringComparison.Ordinal));
    }

    private static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client, string formUrl, string postUrl, List<KeyValuePair<string, string>> fields)
    {
        var page = await client.GetAsync(formUrl);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success, "El formulario debe emitir un token antiforgery real.");
        fields.Add(new("__RequestVerificationToken", WebUtility.HtmlDecode(token.Groups[1].Value)));
        return await client.PostAsync(postUrl, new FormUrlEncodedContent(fields));
    }

    private static void AssertLoginRedirect(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", new Uri(new Uri("https://localhost"), response.Headers.Location!).AbsolutePath);
    }

    private static async Task<string> SeedUsersAsync(IdentityCookieWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var role in new[] { "Administrador", "Recepcionista", "Odontologo" })
            Assert.True((await roles.CreateAsync(new IdentityRole(role))).Succeeded);

        var admin = new ApplicationUser { UserName = AdminEmail, Email = AdminEmail, Nombre = "Admin", Apellido = "Prueba", Estado = true };
        Assert.True((await users.CreateAsync(admin, Password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(admin, "Administrador")).Succeeded);
        var user = new ApplicationUser { UserName = UserEmail, Email = UserEmail, Nombre = "Usuario", Apellido = "Prueba", Estado = true };
        Assert.True((await users.CreateAsync(user, Password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, "Recepcionista")).Succeeded);
        return user.Id;
    }
}
