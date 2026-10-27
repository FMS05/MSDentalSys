using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using MSDentalSys.Data.Models;
using Xunit;

namespace MSDentalSys.Tests.Integration;

// Real Identity cookies, antiforgery and SQLite stores exercise the complete HTTP pipeline.
// These credentials belong exclusively to isolated, in-memory test accounts.
public class PasswordFlowsIntegrationTests
{
    private const string Password = "OriginalTest7!";
    private const string NewPassword = "ReplacementTest8!";
    private const string WrongPassword = "IncorrectTest9!";
    private const string ChangeUrl = "/Account/ChangePassword";
    private const string AdminEmail = "password.admin@example.test";
    private const string OtherAdminEmail = "password.otheradmin@example.test";
    private const string InitialAdminEmail = "admin@msdentalsys.local";
    private const string DoctorEmail = "password.doctor@example.test";
    private const string ReceptionEmail = "password.reception@example.test";
    private const string AuthCookieName = ".AspNetCore.Identity.Application=";

    [Theory]
    [InlineData("/Account/ChangePassword")]
    [InlineData("/Usuarios/ResetPassword/missing-user")]
    public async Task Anonimo_NoAccedePorGetNiPost(string url)
    {
        using var factory = new IdentityCookieWebApplicationFactory();
        using var client = CreateClient(factory);
        AssertRedirectTo(await client.GetAsync(url), "/Account/Login");
        AssertRedirectTo(await client.PostAsync(url, Form(ChangeFields())), "/Account/Login");
    }

    [Theory]
    [InlineData(AdminEmail)]
    [InlineData(DoctorEmail)]
    [InlineData(ReceptionEmail)]
    public async Task Account_CadaRolAccedeYCambiaSuPassword(string email)
    {
        using var factory = new IdentityCookieWebApplicationFactory();
        var ids = await SeedAsync(factory);
        using var client = CreateClient(factory);
        await LoginAsync(client, email);

        var page = await client.GetAsync(ChangeUrl);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        AssertPasswordInputs(html, "CurrentPassword", "NewPassword", "ConfirmPassword");
        Assert.Equal("current-password", InputAttribute(html, "CurrentPassword", "autocomplete"));
        Assert.Equal("new-password", InputAttribute(html, "NewPassword", "autocomplete"));
        Assert.Equal("new-password", InputAttribute(html, "ConfirmPassword", "autocomplete"));
        Assert.Empty(Regex.Matches(html, "<a\\b[^>]*href=\"/Account/ChangePassword\"", RegexOptions.IgnoreCase));
        var profile = await client.GetAsync($"/Usuarios/Details/{ids[email]}");
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        var profileHtml = await profile.Content.ReadAsStringAsync();
        Assert.Single(Regex.Matches(profileHtml, "<a\\b[^>]*href=\"/Account/ChangePassword\"", RegexOptions.IgnoreCase));

        var response = await client.PostAsync(ChangeUrl, Form(WithToken(ChangeFields(), html)));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await AssertPasswordAsync(factory, ids[email], NewPassword, true);
        await AssertPasswordAsync(factory, ids[email], Password, false);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(ChangeUrl)).StatusCode);
        await AssertLoginOutcomeAsync(factory, email, Password, false);
        await AssertLoginOutcomeAsync(factory, email, NewPassword, true);
    }

    [Theory]
    [InlineData(WrongPassword, NewPassword, NewPassword)]
    [InlineData(Password, Password, Password)]
    [InlineData(Password, "weak", "weak")]
    [InlineData(Password, "lowercaseonly8!", "lowercaseonly8!")]
    [InlineData(Password, NewPassword, "DifferentTest4!")]
    [InlineData("", NewPassword, NewPassword)]
    [InlineData(Password, "", "")]
    public async Task Account_ValidacionRechazaSinCambiarCredencialNiRepoblarPasswords(
        string current, string replacement, string confirmation)
    {
        using var factory = new IdentityCookieWebApplicationFactory();
        var ids = await SeedAsync(factory);
        using var client = CreateClient(factory);
        await LoginAsync(client, ReceptionEmail);
        var response = await PostAsync(client, ChangeUrl, ChangeUrl,
            [new("CurrentPassword", current), new("NewPassword", replacement), new("ConfirmPassword", confirmation)]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("validation", html, StringComparison.OrdinalIgnoreCase);
        Assert.Matches("(?:field-validation-error|validation-summary-errors)", html);
        AssertPasswordInputs(html, "CurrentPassword", "NewPassword", "ConfirmPassword");
        AssertNoSecrets(html, current, replacement, confirmation);
        await AssertPasswordAsync(factory, ids[ReceptionEmail], Password, true);
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByIdAsync(ids[ReceptionEmail]))!;
        Assert.Equal(0, await users.GetAccessFailedCountAsync(user));
    }

    [Fact]
    public async Task Account_UserIdInyectadoNoModificaOtraCuenta()
    {
        using var factory = new IdentityCookieWebApplicationFactory();
        var ids = await SeedAsync(factory);
        using var client = CreateClient(factory);
        await LoginAsync(client, ReceptionEmail);
        var fields = ChangeFields();
        fields.Add(new("UserId", ids[DoctorEmail]));
        fields.Add(new("Id", ids[DoctorEmail]));
        var response = await PostAsync(client, ChangeUrl,
            $"{ChangeUrl}?UserId={ids[DoctorEmail]}&id={ids[DoctorEmail]}", fields);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await AssertPasswordAsync(factory, ids[ReceptionEmail], NewPassword, true);
        await AssertPasswordAsync(factory, ids[DoctorEmail], Password, true);
        await AssertPasswordAsync(factory, ids[DoctorEmail], NewPassword, false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Account_RefrescaSesionActualConservaRememberMeYRevocaOtraCookie(bool rememberMe)
    {
        using var factory = new IdentityCookieWebApplicationFactory();
        await SeedAsync(factory);
        using var current = CreateClient(factory);
        using var other = CreateClient(factory);
        await LoginAsync(current, ReceptionEmail, rememberMe: rememberMe);
        await LoginAsync(other, ReceptionEmail, rememberMe: rememberMe);
        var response = await PostAsync(current, ChangeUrl, ChangeUrl, ChangeFields());

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var authCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(AuthCookieName, StringComparison.Ordinal));
        Assert.Equal(rememberMe, authCookie.Contains("expires=", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(HttpStatusCode.OK, (await current.GetAsync(ChangeUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync(ChangeUrl)).StatusCode);
        factory.Clock.Advance(TimeSpan.FromSeconds(61));

        Assert.Equal(HttpStatusCode.OK, (await current.GetAsync(ChangeUrl)).StatusCode);
        AssertRedirectTo(await other.GetAsync(ChangeUrl), "/Account/Login");
    }

    [Theory]
    [InlineData(false, "GET")]
    [InlineData(true, "GET")]
    [InlineData(false, "POST")]
    [InlineData(true, "POST")]
    public async Task Account_UsuarioDesactivadoOEliminadoDuranteSesionCierraSesion(bool delete, string method)
    {
        using var factory = new IdentityCookieWebApplicationFactory();
        var ids = await SeedAsync(factory);
        using var client = CreateClient(factory);
        await LoginAsync(client, ReceptionEmail);
        var form = await GetHtmlAsync(client, ChangeUrl);
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByIdAsync(ids[ReceptionEmail]))!;
            if (delete) Assert.True((await users.DeleteAsync(user)).Succeeded);
            else
            {
                user.Estado = false;
                Assert.True((await users.UpdateAsync(user)).Succeeded);
            }
        }

        // No clock advance: the action must reject this stale principal itself.
        var response = method == "GET"
            ? await client.GetAsync(ChangeUrl)
            : await client.PostAsync(ChangeUrl, Form(WithToken(ChangeFields(), form)));
        AssertRedirectTo(response, "/Account/Login");
        AssertRedirectTo(await client.GetAsync(ChangeUrl), "/Account/Login");
        if (!delete) await AssertPasswordAsync(factory, ids[ReceptionEmail], Password, true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AmbosFlujos_PostExigeAntiforgery(bool reset)
    {
        using var factory = new IdentityCookieWebApplicationFactory();
        var ids = await SeedAsync(factory);
        using var client = CreateClient(factory);
        await LoginAsync(client, AdminEmail);
        var target = reset ? ids[DoctorEmail] : ids[AdminEmail];
        var url = reset ? ResetUrl(target) : ChangeUrl;
        var fields = reset ? ResetFields() : ChangeFields();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(url, Form(fields))).StatusCode);
        fields.Add(new("__RequestVerificationToken", "invalid-antiforgery-test"));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(url, Form(fields))).StatusCode);
        await AssertPasswordAsync(factory, target, Password, true);
    }

    [Fact]
    public async Task Account_RateLimitCincoPostPorUsuarioNoLimitaGetNiOtroUsuarioNiLogin()
    {
        using var factory = new IdentityCookieWebApplicationFactory();
        var ids = await SeedAsync(factory);
        using var first = CreateClient(factory);
        using var sameUser = CreateClient(factory);
        using var differentUser = CreateClient(factory);
        await LoginAsync(first, ReceptionEmail);
        await LoginAsync(sameUser, ReceptionEmail);
        await LoginAsync(differentUser, DoctorEmail);
        var firstForm = await GetHtmlAsync(first, ChangeUrl);
        var sameUserForm = await GetHtmlAsync(sameUser, ChangeUrl);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var response = await first.PostAsync(ChangeUrl, Form(WithToken(ChangeFields(WrongPassword), firstForm)));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var bypassFields = ChangeFields(WrongPassword);
        bypassFields.Add(new("UserId", ids[DoctorEmail]));
        var limited = await sameUser.PostAsync($"{ChangeUrl}?UserId={ids[DoctorEmail]}", Form(WithToken(bypassFields, sameUserForm)));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.TryGetValues("Retry-After", out var retryValues));
        Assert.True(int.TryParse(Assert.Single(retryValues), out var seconds));
        Assert.InRange(seconds, 1, 60);
        var body = WebUtility.HtmlDecode(await limited.Content.ReadAsStringAsync());
        Assert.Contains("intentos", body, StringComparison.OrdinalIgnoreCase);
        AssertNoSecrets(body, Password, WrongPassword, NewPassword);
        for (var request = 0; request < 6; request++)
            Assert.Equal(HttpStatusCode.OK, (await first.GetAsync(ChangeUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(differentUser, ChangeUrl, ChangeUrl, ChangeFields(WrongPassword))).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByIdAsync(ids[ReceptionEmail]))!;
            Assert.Equal(0, await users.GetAccessFailedCountAsync(user));
            Assert.False(await users.IsLockedOutAsync(user));
        }
        // Successful Login remains available even when this user's ChangePassword bucket is exhausted.
        await AssertLoginOutcomeAsync(factory, ReceptionEmail, Password, true);
        using var login = CreateClient(factory);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var response = await PostAsync(login, "/Account/Login", "/Account/Login",
                [new("Email", ReceptionEmail), new("Password", WrongPassword)]);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True(await users.IsLockedOutAsync((await users.FindByIdAsync(ids[ReceptionEmail]))!));
        }
        var lockedOut = await PostAsync(login, "/Account/Login", "/Account/Login",
            [new("Email", ReceptionEmail), new("Password", Password)]);
        Assert.Equal(HttpStatusCode.OK, lockedOut.StatusCode);
        Assert.Contains("bloqueada", WebUtility.HtmlDecode(await lockedOut.Content.ReadAsStringAsync()), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(DoctorEmail)]
    [InlineData(ReceptionEmail)]
    public async Task Usuarios_ResetSoloAdministradorGetYPost(string actor)
    {
        using var factory = new IdentityCookieWebApplicationFactory();
        var ids = await SeedAsync(factory);
        using var client = CreateClient(factory);
        await LoginAsync(client, actor);
        var form = await GetHtmlAsync(client, ChangeUrl);

        AssertRedirectTo(await client.GetAsync(ResetUrl(ids[DoctorEmail])), "/Account/AccessDenied");
        AssertRedirectTo(await client.PostAsync(ResetUrl(ids[DoctorEmail]), Form(WithToken(ResetFields(), form))), "/Account/AccessDenied");
        await AssertPasswordAsync(factory, ids[DoctorEmail], Password, true);
    }

    [Theory]
    [InlineData(DoctorEmail)]
    [InlineData(ReceptionEmail)]
    public async Task Usuarios_AdminRestableceObjetivoPermitidoRevocaSusCookiesYConservaSesionActor(string targetEmail)
    {
        using var factory = new IdentityCookieWebApplicationFactory();
        var ids = await SeedAsync(factory);
        using var admin = CreateClient(factory);
        using var oldSession = CreateClient(factory);
        using var anotherOldSession = CreateClient(factory);
        await LoginAsync(admin, AdminEmail);
        await LoginAsync(oldSession, targetEmail);
        await LoginAsync(anotherOldSession, targetEmail);
        var url = ResetUrl(ids[targetEmail]);
        var html = await GetHtmlAsync(admin, url);
        Assert.Contains(targetEmail, html, StringComparison.Ordinal);
        AssertPasswordInputs(html, "NewPassword", "ConfirmPassword");
        Assert.Equal("new-password", InputAttribute(html, "NewPassword", "autocomplete"));
        Assert.Equal("new-password", InputAttribute(html, "ConfirmPassword", "autocomplete"));
        Assert.DoesNotContain("name=\"CurrentPassword\"", html, StringComparison.OrdinalIgnoreCase);
        AssertNoInternalIdentityFields(html);
        var details = await GetHtmlAsync(admin, $"/Usuarios/Details/{ids[targetEmail]}");
        Assert.Contains(url, details, StringComparison.Ordinal);

        var response = await admin.PostAsync(url, Form(WithToken(ResetFields(), html)));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        AssertNoSecrets(response.Headers.ToString(), Password, NewPassword);
        AssertNoInternalIdentityFields(response.Headers.Location!.OriginalString);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies)
            && cookies.Any(value => value.StartsWith(AuthCookieName, StringComparison.Ordinal)));
        AssertSafeTempData(factory, response);
        var responseHtml = await GetHtmlAsync(admin, response.Headers.Location.OriginalString);
        AssertNoSecrets(responseHtml, Password, NewPassword);
        AssertNoInternalIdentityFields(responseHtml);
        await AssertPasswordAsync(factory, ids[targetEmail], NewPassword, true);
        await AssertPasswordAsync(factory, ids[targetEmail], Password, false);
        await AssertPasswordAsync(factory, ids[AdminEmail], Password, true);
        Assert.Equal(HttpStatusCode.OK, (await oldSession.GetAsync(ChangeUrl)).StatusCode);

        factory.Clock.Advance(TimeSpan.FromSeconds(61));
        AssertRedirectTo(await oldSession.GetAsync(ChangeUrl), "/Account/Login");
        AssertRedirectTo(await anotherOldSession.GetAsync(ChangeUrl), "/Account/Login");
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/Usuarios")).StatusCode);
        await AssertLoginOutcomeAsync(factory, targetEmail, Password, false);
        await AssertLoginOutcomeAsync(factory, targetEmail, NewPassword, true);
    }

    [Theory]
    [InlineData(AdminEmail)]
    [InlineData(OtherAdminEmail)]
    [InlineData(InitialAdminEmail)]
    public async Task Usuarios_ProhibeResetDeCualquierAdministradorInclusoActorAunqueClienteFalsifiqueRol(string targetEmail)
    {
        using var factory = new IdentityCookieWebApplicationFactory();
        var ids = await SeedAsync(factory);
        using var admin = CreateClient(factory);
        await LoginAsync(admin, AdminEmail);
        var url = ResetUrl(ids[targetEmail]);
        AssertRedirectTo(await admin.GetAsync(url), "/Account/AccessDenied");
        var fields = ResetFields();
        fields.Add(new("Rol", "Odontologo"));
        fields.Add(new("Estado", "true"));
        AssertRedirectTo(await PostAsync(admin, ChangeUrl, url, fields), "/Account/AccessDenied");
        var details = await GetHtmlAsync(admin, $"/Usuarios/Details/{ids[targetEmail]}");
        Assert.DoesNotContain(url, details, StringComparison.Ordinal);
        await AssertPasswordAsync(factory, ids[targetEmail], Password, true);
        await AssertPasswordAsync(factory, ids[targetEmail], NewPassword, false);
    }

    [Fact]
    public async Task Usuarios_CookieConRolAnteriorNoPermiteResetPropioTrasDegradarAdministrador()
    {
        using var factory = new IdentityCookieWebApplicationFactory();
        var ids = await SeedAsync(factory);
        using var admin = CreateClient(factory);
        await LoginAsync(admin, AdminEmail);
        var form = await GetHtmlAsync(admin, ChangeUrl);
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByIdAsync(ids[AdminEmail]))!;
            Assert.True((await users.RemoveFromRoleAsync(user, "Administrador")).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, "Odontologo")).Succeeded);
        }
        // The old cookie still has Administrador until the periodic stamp validation.
        var url = ResetUrl(ids[AdminEmail]);
        AssertRedirectTo(await admin.GetAsync(url), "/Account/AccessDenied");
        AssertRedirectTo(await admin.PostAsync(url, Form(WithToken(ResetFields(), form))), "/Account/AccessDenied");
        await AssertPasswordAsync(factory, ids[AdminEmail], Password, true);
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown-user")]
    [InlineData("%20%20%20")]
    public async Task Usuarios_ObjetivoVacioInexistenteOInvalidoEs404(string id)
    {
        using var factory = new IdentityCookieWebApplicationFactory();
        await SeedAsync(factory);
        using var admin = CreateClient(factory);
        await LoginAsync(admin, AdminEmail);
        var url = ResetUrl(id);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(admin, ChangeUrl, url, ResetFields())).StatusCode);
    }

    [Fact]
    public async Task Usuarios_IdentificadoresEnFormYQueryNoSustituyenObjetivoDeRuta()
    {
        using var factory = new IdentityCookieWebApplicationFactory();
        var ids = await SeedAsync(factory);
        using var admin = CreateClient(factory);
        await LoginAsync(admin, AdminEmail);
        var fields = ResetFields();
        fields.Add(new("Id", ids[OtherAdminEmail]));
        fields.Add(new("UserId", ids[OtherAdminEmail]));
        fields.Add(new("Rol", "Administrador"));
        fields.Add(new("Estado", "false"));
        fields.Add(new("NombreCompleto", "Nombre falsificado"));
        var url = ResetUrl(ids[DoctorEmail]);
        var response = await PostAsync(admin, url, $"{url}?id={ids[OtherAdminEmail]}", fields);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await AssertPasswordAsync(factory, ids[DoctorEmail], NewPassword, true);
        await AssertPasswordAsync(factory, ids[OtherAdminEmail], Password, true);
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var doctor = (await users.FindByIdAsync(ids[DoctorEmail]))!;
        Assert.True(doctor.Estado);
        Assert.Equal("Usuario", doctor.Nombre);
        Assert.True(await users.IsInRoleAsync(doctor, "Odontologo"));
    }

    [Theory]
    [InlineData("weak", "weak")]
    [InlineData("lowercaseonly8!", "lowercaseonly8!")]
    [InlineData(NewPassword, "DifferentTest4!")]
    [InlineData("", "")]
    public async Task Usuarios_PasswordOConfirmacionInvalidaNoCambiaCredencialNiRepuebla(string replacement, string confirmation)
    {
        using var factory = new IdentityCookieWebApplicationFactory();
        var ids = await SeedAsync(factory);
        using var admin = CreateClient(factory);
        await LoginAsync(admin, AdminEmail);
        var url = ResetUrl(ids[DoctorEmail]);
        var response = await PostAsync(admin, url, url,
            [new("NewPassword", replacement), new("ConfirmPassword", confirmation), new("Email", "forged@example.test")]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Matches("(?:field-validation-error|validation-summary-errors)", html);
        AssertPasswordInputs(html, "NewPassword", "ConfirmPassword");
        AssertNoSecrets(html, replacement, confirmation);
        AssertNoInternalIdentityFields(html);
        Assert.Contains(DoctorEmail, html, StringComparison.Ordinal);
        Assert.DoesNotContain("forged@example.test", html, StringComparison.Ordinal);
        await AssertPasswordAsync(factory, ids[DoctorEmail], Password, true);
    }

    [Theory]
    [InlineData(DoctorEmail, false)]
    [InlineData(ReceptionEmail, false)]
    [InlineData(DoctorEmail, true)]
    [InlineData(ReceptionEmail, true)]
    public async Task Usuarios_ResetCuentaInactivaCambiaPasswordSinReactivar(string targetEmail, bool deactivateAfterGet)
    {
        using var factory = new IdentityCookieWebApplicationFactory();
        var ids = await SeedAsync(factory);
        using var admin = CreateClient(factory);
        await LoginAsync(admin, AdminEmail);
        var url = ResetUrl(ids[targetEmail]);
        var html = deactivateAfterGet ? await GetHtmlAsync(admin, url) : null;
        if (deactivateAfterGet)
            Assert.Contains("<dd>Activo</dd>", html!, StringComparison.Ordinal);

        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByIdAsync(ids[targetEmail]))!;
            user.Estado = false;
            Assert.True((await users.UpdateAsync(user)).Succeeded);
        }
        if (!deactivateAfterGet)
        {
            html = await GetHtmlAsync(admin, url);
            Assert.Contains("inactiva", WebUtility.HtmlDecode(html), StringComparison.OrdinalIgnoreCase);
        }
        var fields = ResetFields();
        fields.Add(new("Estado", "true"));
        var response = await admin.PostAsync(url, Form(WithToken(fields, html!)));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var confirmation = WebUtility.HtmlDecode(await GetHtmlAsync(admin, response.Headers.Location!.OriginalString));
        Assert.Contains("La cuenta continúa inactiva.", confirmation, StringComparison.Ordinal);
        await AssertPasswordAsync(factory, ids[targetEmail], NewPassword, true);
        await AssertPasswordAsync(factory, ids[targetEmail], Password, false);
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.False((await users.FindByIdAsync(ids[targetEmail]))!.Estado);
        }
        await AssertLoginOutcomeAsync(factory, targetEmail, NewPassword, false);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies)
            && cookies.Any(value => value.StartsWith(AuthCookieName, StringComparison.Ordinal)));
        factory.Clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/Usuarios")).StatusCode);
        await AssertPasswordAsync(factory, ids[AdminEmail], Password, true);
    }

    [Theory]
    [InlineData("administrator")]
    [InlineData("no-role")]
    [InlineData("deleted")]
    public async Task Usuarios_PostReconsultaObjetivoCuandoCambiaDespuesDelGet(string change)
    {
        using var factory = new IdentityCookieWebApplicationFactory();
        var ids = await SeedAsync(factory);
        using var admin = CreateClient(factory);
        await LoginAsync(admin, AdminEmail);
        var url = ResetUrl(ids[DoctorEmail]);
        var html = await GetHtmlAsync(admin, url);
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByIdAsync(ids[DoctorEmail]))!;
            if (change == "deleted") Assert.True((await users.DeleteAsync(user)).Succeeded);
            else if (change == "administrator") Assert.True((await users.AddToRoleAsync(user, "Administrador")).Succeeded);
            else Assert.True((await users.RemoveFromRoleAsync(user, "Odontologo")).Succeeded);
        }
        var response = await admin.PostAsync(url, Form(WithToken(ResetFields(), html)));

        if (change == "deleted") Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        else
        {
            AssertRedirectTo(response, "/Account/AccessDenied");
            AssertRedirectTo(await admin.GetAsync(url), "/Account/AccessDenied");
            await AssertPasswordAsync(factory, ids[DoctorEmail], Password, true);
            var details = await GetHtmlAsync(admin, $"/Usuarios/Details/{ids[DoctorEmail]}");
            Assert.DoesNotContain(url, details, StringComparison.Ordinal);
        }
    }

    private static string ResetUrl(string id) => $"/Usuarios/ResetPassword/{id}";

    private static List<KeyValuePair<string, string>> ChangeFields(string current = Password) =>
        [new("CurrentPassword", current), new("NewPassword", NewPassword), new("ConfirmPassword", NewPassword)];

    private static List<KeyValuePair<string, string>> ResetFields() =>
        [new("NewPassword", NewPassword), new("ConfirmPassword", NewPassword)];

    private static FormUrlEncodedContent Form(IEnumerable<KeyValuePair<string, string>> fields) => new(fields);

    private static HttpClient CreateClient(IdentityCookieWebApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true
        });

    private static async Task<string> GetHtmlAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static List<KeyValuePair<string, string>> WithToken(List<KeyValuePair<string, string>> fields, string html)
    {
        var input = Regex.Match(html, "<input\\b[^>]*name=\"__RequestVerificationToken\"[^>]*>", RegexOptions.IgnoreCase);
        Assert.True(input.Success, "El formulario debe emitir un token antiforgery real.");
        var token = Regex.Match(input.Value, "\\bvalue=\"([^\"]+)\"", RegexOptions.IgnoreCase);
        Assert.True(token.Success);
        return [.. fields, new("__RequestVerificationToken", WebUtility.HtmlDecode(token.Groups[1].Value))];
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string formUrl, string postUrl,
        List<KeyValuePair<string, string>> fields) =>
        await client.PostAsync(postUrl, Form(WithToken(fields, await GetHtmlAsync(client, formUrl))));

    private static async Task LoginAsync(HttpClient client, string email, string password = Password, bool rememberMe = false)
    {
        var response = await PostAsync(client, "/Account/Login", "/Account/Login",
            [new("Email", email), new("Password", password), new("RememberMe", rememberMe ? "true" : "false")]);
        AssertRedirectTo(response, "/Dashboard");
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), value => value.StartsWith(AuthCookieName, StringComparison.Ordinal));
    }

    private static async Task AssertLoginOutcomeAsync(IdentityCookieWebApplicationFactory factory, string email,
        string password, bool succeeds)
    {
        using var client = CreateClient(factory);
        var response = await PostAsync(client, "/Account/Login", "/Account/Login",
            [new("Email", email), new("Password", password)]);
        if (succeeds) AssertRedirectTo(response, "/Dashboard");
        else
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            AssertRedirectTo(await client.GetAsync(ChangeUrl), "/Account/Login");
        }
    }

    private static void AssertRedirectTo(HttpResponseMessage response, string path)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(path, new Uri(new Uri("https://localhost"), response.Headers.Location!).AbsolutePath);
    }

    private static string InputAttribute(string html, string name, string attribute)
    {
        var input = Regex.Match(html, $"<input\\b[^>]*name=\"{Regex.Escape(name)}\"[^>]*>", RegexOptions.IgnoreCase);
        Assert.True(input.Success, $"Falta el campo {name}.");
        var match = Regex.Match(input.Value, $"\\b{Regex.Escape(attribute)}=\"([^\"]*)\"", RegexOptions.IgnoreCase);
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : string.Empty;
    }

    private static void AssertPasswordInputs(string html, params string[] names)
    {
        foreach (var name in names)
        {
            Assert.Equal("password", InputAttribute(html, name, "type"));
            Assert.Equal(string.Empty, InputAttribute(html, name, "value"));
        }
        Assert.Equal(names.Length, Regex.Matches(html, "<input\\b[^>]*type=\"password\"[^>]*>", RegexOptions.IgnoreCase).Count);
    }

    private static void AssertNoSecrets(string text, params string[] secrets)
    {
        var decoded = WebUtility.HtmlDecode(text);
        foreach (var secret in secrets.Where(value => !string.IsNullOrEmpty(value)).Distinct())
            Assert.DoesNotContain(secret, decoded, StringComparison.Ordinal);
    }

    private static void AssertNoInternalIdentityFields(string text)
    {
        foreach (var field in new[] { "PasswordHash", "SecurityStamp", "ResetToken", "PasswordResetToken" })
            Assert.DoesNotContain(field, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch("(?i)\\bname=\"token\"", text);
        Assert.DoesNotMatch("(?i)[?&]token=", text);
    }

    private static void AssertSafeTempData(IdentityCookieWebApplicationFactory factory, HttpResponseMessage response)
    {
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ITempDataProvider>();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        if (response.Headers.TryGetValues("Set-Cookie", out var values))
            context.Request.Headers.Cookie = string.Join("; ", values.Select(value => value.Split(';', 2)[0]));
        var data = provider.LoadTempData(context);
        Assert.Single(data);
        Assert.True(data.ContainsKey("SuccessMessage"));
        var message = Assert.IsType<string>(data["SuccessMessage"]);
        AssertNoSecrets(message, Password, NewPassword);
        AssertNoInternalIdentityFields(message);
    }

    private static async Task AssertPasswordAsync(IdentityCookieWebApplicationFactory factory, string id, string password, bool valid)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByIdAsync(id);
        Assert.NotNull(user);
        Assert.Equal(valid, await users.CheckPasswordAsync(user, password));
    }

    private static async Task<Dictionary<string, string>> SeedAsync(IdentityCookieWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var role in new[] { "Administrador", "Odontologo", "Recepcionista" })
            Assert.True((await roles.CreateAsync(new IdentityRole(role))).Succeeded);
        var ids = new Dictionary<string, string>();
        foreach (var (email, role) in new[]
        {
            (AdminEmail, "Administrador"), (OtherAdminEmail, "Administrador"),
            (InitialAdminEmail, "Administrador"),
            (DoctorEmail, "Odontologo"), (ReceptionEmail, "Recepcionista")
        })
        {
            var user = new ApplicationUser
            {
                Email = email, UserName = email, Nombre = "Usuario", Apellido = "Prueba", Estado = true
            };
            Assert.True((await users.CreateAsync(user, Password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
            ids.Add(email, user.Id);
        }
        return ids;
    }
}
