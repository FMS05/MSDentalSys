using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MSDentalSys.Data.InitialData;
using MSDentalSys.Data.Models;
using Xunit;

namespace MSDentalSys.Tests.Integration;

// Real Identity cookies and isolated SQLite stores exercise authorization and model binding.
public class InitialAdminProfileTests
{
    [Fact]
    public async Task PerfilInicial_DetailsYEditExplicanQueCorreoEstaBloqueado()
    {
        using var factory = new IdentityCookieWebApplicationFactory();
        var user = await ProfileTestSupport.SeedInitialAdminAsync(factory);
        using var client = ProfileTestSupport.CreateClient(factory);
        await ProfileTestSupport.LoginAsync(client, user.Email!);

        var details = await ProfileTestSupport.GetHtmlAsync(client, $"/Usuarios/Details/{user.Id}");
        var edit = await ProfileTestSupport.GetHtmlAsync(client, $"/Usuarios/Edit/{user.Id}");

        AssertProtectedEmailNotice(details);
        AssertProtectedEmailNotice(edit);
        AssertProtectedEmailInput(edit);
        Assert.Single(Regex.Matches(details, "<a\\b[^>]*href=\"/Account/ChangePassword\"", RegexOptions.IgnoreCase));
    }

    [Theory]
    [InlineData("false", false)]
    [InlineData("true", false)]
    [InlineData("false", true)]
    public async Task PerfilInicial_PostManipuladoNoCambiaEmailUserNameNiNormalizados(
        string forgedProtectionFlag, bool invalidName)
    {
        using var factory = new IdentityCookieWebApplicationFactory();
        var original = await ProfileTestSupport.SeedInitialAdminAsync(factory);
        using var client = ProfileTestSupport.CreateClient(factory);
        await ProfileTestSupport.LoginAsync(client, original.Email!);
        var fields = ProfileTestSupport.EditFields(original, "Administrador");
        fields["Email"] = "initial.changed@example.test";
        fields["UserName"] = "forged.username@example.test";
        fields["NormalizedEmail"] = "FORGED.NORMALIZED@EXAMPLE.TEST";
        fields["NormalizedUserName"] = "FORGED.NORMALIZED@EXAMPLE.TEST";
        fields["EsAdministradorInicial"] = forgedProtectionFlag;
        fields["Nombre"] = invalidName ? "" : "Nombre manipulado";

        var response = await ProfileTestSupport.PostAsync(client, $"/Usuarios/Edit/{original.Id}", fields);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Matches("(?:field-validation-error|validation-summary-errors)", html);
        AssertProtectedEmailInput(html);
        Assert.DoesNotContain("initial.changed@example.test", html, StringComparison.OrdinalIgnoreCase);
        var stored = await ProfileTestSupport.ReadAsync(factory, original.Id);
        AssertIdentityUnchanged(original, stored);
        Assert.Equal(original.Nombre, stored.Nombre);
        Assert.Equal(original.PasswordHash, stored.PasswordHash);
        await AssertSeederStillRecognizesAsync(factory, original.Id, ProfileTestSupport.Password);
    }

    [Fact]
    public async Task PerfilInicial_CambiaDatosPermitidosYPassword_SeederConservaMismaCuentaSinDuplicar()
    {
        using var factory = new IdentityCookieWebApplicationFactory();
        var original = await ProfileTestSupport.SeedInitialAdminAsync(factory);
        using var client = ProfileTestSupport.CreateClient(factory);
        await ProfileTestSupport.LoginAsync(client, original.Email!);
        var fields = ProfileTestSupport.EditFields(original, "Administrador");
        fields["Nombre"] = "  Administradora  ";
        fields["Apellido"] = "  Actualizada  ";
        fields["Telefono"] = "  809-555-0144  ";
        fields["Cedula"] = "00112345678";

        var edit = await ProfileTestSupport.PostAsync(client, $"/Usuarios/Edit/{original.Id}", fields);

        ProfileTestSupport.AssertRedirect(edit, $"/Usuarios/Details/{original.Id}");
        var updated = await ProfileTestSupport.ReadAsync(factory, original.Id);
        AssertIdentityUnchanged(original, updated);
        Assert.Equal("Administradora", updated.Nombre);
        Assert.Equal("Actualizada", updated.Apellido);
        Assert.Equal("809-555-0144", updated.PhoneNumber);
        Assert.Equal("001-1234567-8", updated.Cedula);
        Assert.Equal(original.PasswordHash, updated.PasswordHash);
        Assert.True(updated.Estado);

        var change = await ProfileTestSupport.PostAsync(client, "/Account/ChangePassword", new()
        {
            ["CurrentPassword"] = ProfileTestSupport.Password,
            ["NewPassword"] = ProfileTestSupport.NewPassword,
            ["ConfirmPassword"] = ProfileTestSupport.NewPassword
        });
        ProfileTestSupport.AssertRedirect(change, "/Account/ChangePassword");
        factory.Clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/Usuarios/Details/{original.Id}")).StatusCode);

        var afterPassword = await ProfileTestSupport.ReadAsync(factory, original.Id);
        AssertIdentityUnchanged(original, afterPassword);
        Assert.NotEqual(original.PasswordHash, afterPassword.PasswordHash);
        Assert.NotEqual(original.SecurityStamp, afterPassword.SecurityStamp);
        Assert.Equal(updated.Nombre, afterPassword.Nombre);
        Assert.Equal(updated.Apellido, afterPassword.Apellido);
        Assert.Equal(updated.Cedula, afterPassword.Cedula);
        await AssertSeederStillRecognizesAsync(factory, original.Id, ProfileTestSupport.NewPassword);

        using var newSession = ProfileTestSupport.CreateClient(factory);
        await ProfileTestSupport.LoginAsync(newSession, ProfileTestSupport.InitialEmail, ProfileTestSupport.NewPassword);
    }

    [Fact]
    public async Task PerfilInicial_EditSinAntiforgeryNoModificaCorreoNiOtrosDatos()
    {
        using var factory = new IdentityCookieWebApplicationFactory();
        var original = await ProfileTestSupport.SeedInitialAdminAsync(factory);
        using var client = ProfileTestSupport.CreateClient(factory);
        await ProfileTestSupport.LoginAsync(client, original.Email!);
        var fields = ProfileTestSupport.EditFields(original, "Administrador");
        fields["Email"] = "forged@example.test";
        fields["Nombre"] = "Manipulado";

        var response = await client.PostAsync($"/Usuarios/Edit/{original.Id}", new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var stored = await ProfileTestSupport.ReadAsync(factory, original.Id);
        AssertIdentityUnchanged(original, stored);
        Assert.Equal(original.Nombre, stored.Nombre);
    }

    private static void AssertIdentityUnchanged(ApplicationUser expected, ApplicationUser actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(ProfileTestSupport.InitialEmail, actual.Email);
        Assert.Equal(expected.Email, actual.Email);
        Assert.Equal(expected.UserName, actual.UserName);
        Assert.Equal(expected.NormalizedEmail, actual.NormalizedEmail);
        Assert.Equal(expected.NormalizedUserName, actual.NormalizedUserName);
    }

    private static void AssertProtectedEmailInput(string html)
    {
        var input = Assert.Single(Regex.Matches(html, "<input\\b[^>]*>", RegexOptions.IgnoreCase).Cast<Match>(),
            match => Regex.IsMatch(match.Value, "\\bvalue=\"admin@msdentalsys\\.local\"", RegexOptions.IgnoreCase));
        Assert.Matches("(?i)\\breadonly(?:\\s|=|/?>)", input.Value);
    }

    private static void AssertProtectedEmailNotice(string html)
    {
        var text = WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " "));
        Assert.Matches("(?is)correo[^.]{0,240}(?:no[^.]{0,60}(?:modifica|editable)|bloqueado|fijo|protegido)", text);
    }

    private static async Task AssertSeederStillRecognizesAsync(
        IdentityCookieWebApplicationFactory factory, string originalId, string password)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        // No seed password: finding the same account is mandatory, not recreating one.
        var noSeedPassword = new ConfigurationBuilder().Build();
        await AdminSeeder.SeedAsync(users, noSeedPassword);
        await AdminSeeder.SeedAsync(users, noSeedPassword);
        var admin = await users.FindByEmailAsync(ProfileTestSupport.InitialEmail);
        Assert.NotNull(admin);
        Assert.Equal(originalId, admin.Id);
        Assert.True(await users.IsInRoleAsync(admin, "Administrador"));
        Assert.True(await users.CheckPasswordAsync(admin, password));
        Assert.Single(await users.GetUsersInRoleAsync("Administrador"));
        Assert.Equal(1, await users.Users.CountAsync());
        Assert.Equal(ProfileTestSupport.InitialEmail, admin.UserName);
        Assert.Equal(users.NormalizeEmail(ProfileTestSupport.InitialEmail), admin.NormalizedEmail);
        Assert.Equal(users.NormalizeName(ProfileTestSupport.InitialEmail), admin.NormalizedUserName);
    }
}

internal static class ProfileTestSupport
{
    internal const string Password = "ProfileOriginal7!";
    internal const string NewPassword = "ProfileReplacement8!";
    internal const string InitialEmail = "admin@msdentalsys.local";

    internal static HttpClient CreateClient(IdentityCookieWebApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true
        });

    internal static async Task<string> GetHtmlAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    internal static async Task<HttpResponseMessage> PostAsync(HttpClient client, string url,
        Dictionary<string, string> fields, string? formUrl = null)
    {
        var html = await GetHtmlAsync(client, formUrl ?? url);
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success, "El formulario debe emitir un token antiforgery real.");
        return await client.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string>(fields)
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value)
        }));
    }

    internal static async Task LoginAsync(HttpClient client, string email, string password = Password)
    {
        var response = await PostAsync(client, "/Account/Login", new()
        {
            ["Email"] = email, ["Password"] = password, ["RememberMe"] = "true"
        });
        AssertRedirect(response, "/Dashboard");
    }

    internal static void AssertRedirect(HttpResponseMessage response, string path)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(path, new Uri(new Uri("https://localhost"), response.Headers.Location!).AbsolutePath);
    }

    internal static Dictionary<string, string> EditFields(ApplicationUser user, string role) => new()
    {
        ["Id"] = user.Id, ["Nombre"] = user.Nombre, ["Apellido"] = user.Apellido,
        ["Email"] = user.Email!, ["Telefono"] = user.PhoneNumber ?? "", ["Cedula"] = user.Cedula ?? "",
        ["Rol"] = role
    };

    internal static async Task<ApplicationUser> SeedInitialAdminAsync(IdentityCookieWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { "Administrador", "Odontologo", "Recepcionista" })
            Assert.True((await roles.CreateAsync(new IdentityRole(role))).Succeeded);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SeedAdmin:Password"] = Password
        }).Build();
        await AdminSeeder.SeedAsync(users, configuration);
        return (await users.FindByEmailAsync(InitialEmail))!;
    }

    internal static async Task<ApplicationUser> ReadAsync(IdentityCookieWebApplicationFactory factory, string id)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
            .Users.AsNoTracking().SingleAsync(user => user.Id == id);
    }
}
