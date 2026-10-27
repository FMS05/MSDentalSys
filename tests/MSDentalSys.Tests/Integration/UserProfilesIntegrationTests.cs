using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MSDentalSys.Data.Context;
using MSDentalSys.Data.Models;
using Xunit;

namespace MSDentalSys.Tests.Integration;

public class UserProfilesIntegrationTests
{
    [Theory]
    [InlineData("Doctor")]
    [InlineData("Reception")]
    public async Task Operativos_ListanYVenSoloCuentasOperativas_DetailsAjenoEsSoloLectura(string actor)
    {
        using var factory = new IdentityCookieWebApplicationFactory();
        var accounts = await SeedAsync(factory);
        using var client = ProfileTestSupport.CreateClient(factory);
        await ProfileTestSupport.LoginAsync(client, accounts[actor].Email!);
        var index = await ProfileTestSupport.GetHtmlAsync(client, "/Usuarios");

        foreach (var visible in new[] { "Doctor", "Reception", "Inactive" })
            Assert.Contains(accounts[visible].Email!, index, StringComparison.OrdinalIgnoreCase);
        foreach (var hidden in new[] { "Initial", "SecondaryAdmin", "MixedAdmin", "NoRole" })
        {
            Assert.DoesNotContain(accounts[hidden].Email!, index, StringComparison.OrdinalIgnoreCase);
            AssertDenied(await client.GetAsync($"/Usuarios/Details/{accounts[hidden].Id}"));
        }

        var own = await ProfileTestSupport.GetHtmlAsync(client, $"/Usuarios/Details/{accounts[actor].Id}");
        Assert.Contains($"/Usuarios/Edit/{accounts[actor].Id}", own, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(own, "<a\\b[^>]*href=\"/Account/ChangePassword\"", RegexOptions.IgnoreCase));
        var other = accounts[actor == "Doctor" ? "Reception" : "Doctor"];
        var details = await ProfileTestSupport.GetHtmlAsync(client, $"/Usuarios/Details/{other.Id}");
        Assert.Contains(other.Email!, details, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain($"/Usuarios/Edit/{other.Id}", details, StringComparison.Ordinal);
        Assert.DoesNotContain("/Account/ChangePassword", details, StringComparison.Ordinal);
        Assert.DoesNotContain("/Usuarios/ResetPassword/", details, StringComparison.Ordinal);
        Assert.DoesNotContain("/Usuarios/Create", index, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Doctor")]
    [InlineData("Reception")]
    public async Task Operativos_NoEditanOtraCuentaNiInvocanOperacionesAdministrativas(string actor)
    {
        using var factory = new IdentityCookieWebApplicationFactory();
        var accounts = await SeedAsync(factory);
        using var client = ProfileTestSupport.CreateClient(factory);
        await ProfileTestSupport.LoginAsync(client, accounts[actor].Email!);
        var target = accounts[actor == "Doctor" ? "Reception" : "Doctor"];
        var ownForm = $"/Usuarios/Edit/{accounts[actor].Id}";
        var fields = ProfileTestSupport.EditFields(target, "Administrador");
        fields["Email"] = "idor.changed@example.test";
        fields["EsPerfilPropio"] = "true";
        fields["PuedeEditar"] = "true";
        fields["PuedeEditarDatosAdministrativos"] = "true";

        AssertDenied(await client.GetAsync($"/Usuarios/Edit/{target.Id}"));
        AssertDenied(await ProfileTestSupport.PostAsync(client, $"/Usuarios/Edit/{target.Id}", fields, ownForm));
        AssertDenied(await client.GetAsync("/Usuarios/Create"));
        foreach (var action in new[] { "Create", "Activate", "Deactivate", "ResetPassword" })
        {
            var url = action == "Create" ? "/Usuarios/Create" : $"/Usuarios/{action}/{target.Id}";
            AssertDenied(await ProfileTestSupport.PostAsync(client, url, fields, ownForm));
        }

        var stored = await ProfileTestSupport.ReadAsync(factory, target.Id);
        Assert.Equal(target.Email, stored.Email);
        Assert.Equal(target.Estado, stored.Estado);
        Assert.Equal(target.PasswordHash, stored.PasswordHash);
        Assert.Equal(target.SecurityStamp, stored.SecurityStamp);
    }

    [Fact]
    public async Task Edit_RutaIdNoSeSustituyePorQueryOFormulario()
    {
        using var factory = new IdentityCookieWebApplicationFactory();
        var accounts = await SeedAsync(factory);
        var self = accounts["Reception"];
        var target = accounts["Doctor"];
        using var client = ProfileTestSupport.CreateClient(factory);
        await ProfileTestSupport.LoginAsync(client, self.Email!);
        var fields = ProfileTestSupport.EditFields(self, "Recepcionista");
        fields["Email"] = "route.changed@example.test";

        var mismatch = await ProfileTestSupport.PostAsync(client,
            $"/Usuarios/Edit/{target.Id}?id={self.Id}", fields, $"/Usuarios/Edit/{self.Id}");
        AssertDenied(mismatch);
        Assert.Equal(self.Email, (await ProfileTestSupport.ReadAsync(factory, self.Id)).Email);
        Assert.Equal(target.Email, (await ProfileTestSupport.ReadAsync(factory, target.Id)).Email);
    }

    [Theory]
    [InlineData("Doctor", "Odontologo")]
    [InlineData("Reception", "Recepcionista")]
    public async Task Operativo_EditPropioIgnoraDatosAdministrativosYSincronizaCorreoConservandoSesion(
        string actor, string role)
    {
        using var factory = new IdentityCookieWebApplicationFactory();
        var accounts = await SeedAsync(factory);
        var original = accounts[actor];
        using var client = ProfileTestSupport.CreateClient(factory);
        await ProfileTestSupport.LoginAsync(client, original.Email!);
        var fields = ProfileTestSupport.EditFields(original, role);
        const string newEmail = "self.changed@example.test";
        fields["Email"] = newEmail;
        fields["Telefono"] = "809-555-0133";
        fields["Nombre"] = new string('X', 100);
        fields["Apellido"] = "";
        fields["Cedula"] = "not-a-cedula";
        fields["Rol"] = "Administrador";
        fields["Estado"] = "false";
        fields["PuedeEditarDatosAdministrativos"] = "true";
        fields["FotoPerfil"] = "/forged/avatar.svg";
        fields["PasswordHash"] = "forged-hash";
        fields["SecurityStamp"] = "forged-stamp";
        fields["EsAdministradorInicial"] = "true";

        var response = await ProfileTestSupport.PostAsync(client, $"/Usuarios/Edit/{original.Id}", fields);

        ProfileTestSupport.AssertRedirect(response, $"/Usuarios/Details/{original.Id}");
        var stored = await ProfileTestSupport.ReadAsync(factory, original.Id);
        AssertIdentityEmail(stored, newEmail);
        Assert.Equal("809-555-0133", stored.PhoneNumber);
        Assert.Equal(original.Nombre, stored.Nombre);
        Assert.Equal(original.Apellido, stored.Apellido);
        Assert.Equal(original.Cedula, stored.Cedula);
        Assert.Equal(original.Estado, stored.Estado);
        Assert.Equal(original.FotoPerfil, stored.FotoPerfil);
        Assert.Equal(original.PasswordHash, stored.PasswordHash);
        Assert.NotEqual("forged-stamp", stored.SecurityStamp);
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.Equal(new[] { role }, await users.GetRolesAsync((await users.FindByIdAsync(original.Id))!));
        }

        factory.Clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/Usuarios/Details/{original.Id}")).StatusCode);
        using var fresh = ProfileTestSupport.CreateClient(factory);
        await ProfileTestSupport.LoginAsync(fresh, newEmail);
        using var oldLogin = ProfileTestSupport.CreateClient(factory);
        var rejected = await ProfileTestSupport.PostAsync(oldLogin, "/Account/Login", new()
        {
            ["Email"] = original.Email!, ["Password"] = ProfileTestSupport.Password
        });
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        ProfileTestSupport.AssertRedirect(await oldLogin.GetAsync("/Usuarios"), "/Account/Login");
    }

    [Theory]
    [InlineData("Doctor", "Odontologo")]
    [InlineData("Reception", "Recepcionista")]
    public async Task Administrador_EditaCorreoOperativoConIdentity(string targetName, string role)
    {
        using var factory = new IdentityCookieWebApplicationFactory();
        var accounts = await SeedAsync(factory);
        var original = accounts[targetName];
        using var client = ProfileTestSupport.CreateClient(factory);
        await ProfileTestSupport.LoginAsync(client, ProfileTestSupport.InitialEmail);
        var fields = ProfileTestSupport.EditFields(original, role);
        fields["Email"] = "admin.changed@example.test";
        fields["Nombre"] = "Nombre actualizado";

        ProfileTestSupport.AssertRedirect(await ProfileTestSupport.PostAsync(client,
            $"/Usuarios/Edit/{original.Id}", fields), $"/Usuarios/Details/{original.Id}");

        var stored = await ProfileTestSupport.ReadAsync(factory, original.Id);
        AssertIdentityEmail(stored, fields["Email"]);
        Assert.Equal("Nombre actualizado", stored.Nombre);
        Assert.Equal(original.Estado, stored.Estado);
        using var fresh = ProfileTestSupport.CreateClient(factory);
        await ProfileTestSupport.LoginAsync(fresh, fields["Email"]);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Usuarios")).StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OtroAdministrador_CorreoEditable_ConservaRolYProhibicionReset(bool selfEdit)
    {
        using var factory = new IdentityCookieWebApplicationFactory();
        var accounts = await SeedAsync(factory);
        var target = accounts["SecondaryAdmin"];
        using var client = ProfileTestSupport.CreateClient(factory);
        await ProfileTestSupport.LoginAsync(client, selfEdit ? target.Email! : ProfileTestSupport.InitialEmail);
        var fields = ProfileTestSupport.EditFields(target, "Administrador");
        fields["Email"] = "secondary.changed@example.test";
        var edit = await ProfileTestSupport.GetHtmlAsync(client, $"/Usuarios/Edit/{target.Id}");
        var emailInput = Regex.Match(edit, "<input\\b[^>]*name=\"Email\"[^>]*>", RegexOptions.IgnoreCase);
        Assert.True(emailInput.Success);
        Assert.DoesNotMatch("(?i)\\breadonly(?:\\s|=|/?>)", emailInput.Value);

        ProfileTestSupport.AssertRedirect(await ProfileTestSupport.PostAsync(client,
            $"/Usuarios/Edit/{target.Id}", fields), $"/Usuarios/Details/{target.Id}");

        AssertIdentityEmail(await ProfileTestSupport.ReadAsync(factory, target.Id), fields["Email"]);
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True(await users.IsInRoleAsync((await users.FindByIdAsync(target.Id))!, "Administrador"));
        }
        var details = await ProfileTestSupport.GetHtmlAsync(client, $"/Usuarios/Details/{target.Id}");
        Assert.DoesNotContain($"/Usuarios/ResetPassword/{target.Id}", details, StringComparison.Ordinal);
        AssertDenied(await client.GetAsync($"/Usuarios/ResetPassword/{target.Id}"));
    }

    [Fact]
    public async Task Edit_CorreoDuplicadoNoModificaNingunDato()
    {
        using var factory = new IdentityCookieWebApplicationFactory();
        var accounts = await SeedAsync(factory);
        var original = accounts["Doctor"];
        using var client = ProfileTestSupport.CreateClient(factory);
        await ProfileTestSupport.LoginAsync(client, ProfileTestSupport.InitialEmail);
        var fields = ProfileTestSupport.EditFields(original, "Odontologo");
        fields["Email"] = accounts["Reception"].Email!.ToUpperInvariant();
        fields["Nombre"] = "No debe persistir";

        var response = await ProfileTestSupport.PostAsync(client, $"/Usuarios/Edit/{original.Id}", fields);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Matches("(?:field-validation-error|validation-summary-errors)", await response.Content.ReadAsStringAsync());
        var stored = await ProfileTestSupport.ReadAsync(factory, original.Id);
        AssertIdentityEmail(stored, original.Email!);
        Assert.Equal(original.Nombre, stored.Nombre);
        Assert.Equal(original.SecurityStamp, stored.SecurityStamp);
    }

    [Fact]
    public async Task Edit_UserNameDuplicadoTrasSetEmail_RevierteCorreoYDatosEnTransaccion()
    {
        using var factory = new IdentityCookieWebApplicationFactory();
        var accounts = await SeedAsync(factory);
        var original = accounts["Doctor"];
        const string reservedUserName = "reserved.username@example.test";
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var other = (await users.FindByIdAsync(accounts["Reception"].Id))!;
            Assert.True((await users.SetUserNameAsync(other, reservedUserName)).Succeeded);
        }
        using var client = ProfileTestSupport.CreateClient(factory);
        await ProfileTestSupport.LoginAsync(client, ProfileTestSupport.InitialEmail);
        var fields = ProfileTestSupport.EditFields(original, "Odontologo");
        fields["Email"] = reservedUserName;
        fields["Nombre"] = "No debe persistir";

        var response = await ProfileTestSupport.PostAsync(client, $"/Usuarios/Edit/{original.Id}", fields);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = await ProfileTestSupport.ReadAsync(factory, original.Id);
        AssertIdentityEmail(stored, original.Email!);
        Assert.Equal(original.Nombre, stored.Nombre);
        Assert.Equal(original.SecurityStamp, stored.SecurityStamp);
        Assert.Equal(original.ConcurrencyStamp, stored.ConcurrencyStamp);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0011234567")]
    [InlineData("001123456789")]
    [InlineData("001-1234567-X")]
    [InlineData("0011-234567-8")]
    [InlineData("123456789012345678901")]
    public async Task Create_CedulaObligatoriaYFormatoValidadoEnServidor(string cedula)
    {
        using var factory = new IdentityCookieWebApplicationFactory();
        await SeedAsync(factory);
        using var client = ProfileTestSupport.CreateClient(factory);
        await ProfileTestSupport.LoginAsync(client, ProfileTestSupport.InitialEmail);
        var fields = CreateFields(cedula);

        var response = await ProfileTestSupport.PostAsync(client, "/Usuarios/Create", fields);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Matches("(?:field-validation-error|validation-summary-errors)", html);
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Null(await users.FindByEmailAsync(fields["Email"]));
    }

    [Theory]
    [InlineData("22212345678")]
    [InlineData("222-1234567-8")]
    public async Task Create_NormalizaCedulaYPermiteMismaCedulaDePaciente(string cedula)
    {
        using var factory = new IdentityCookieWebApplicationFactory();
        await SeedAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.Pacientes.Add(new Paciente { Nombre = "Paciente", Apellido = "Existente", Cedula = "222-1234567-8" });
            await context.SaveChangesAsync();
        }
        using var client = ProfileTestSupport.CreateClient(factory);
        await ProfileTestSupport.LoginAsync(client, ProfileTestSupport.InitialEmail);
        var fields = CreateFields(cedula);

        ProfileTestSupport.AssertRedirect(await ProfileTestSupport.PostAsync(client, "/Usuarios/Create", fields), "/Usuarios");

        using var verification = factory.Services.CreateScope();
        var users = verification.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var created = await users.FindByEmailAsync(fields["Email"]);
        Assert.NotNull(created);
        Assert.Equal("222-1234567-8", created.Cedula);
        AssertIdentityEmail(created, fields["Email"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CedulaDuplicadaIncluyeInactivosYHistoricosSinGuiones(bool edit)
    {
        using var factory = new IdentityCookieWebApplicationFactory();
        var accounts = await SeedAsync(factory);
        var target = accounts["Doctor"];
        using var client = ProfileTestSupport.CreateClient(factory);
        await ProfileTestSupport.LoginAsync(client, ProfileTestSupport.InitialEmail);
        // Inactive account stores the historical compact value 99912345678.
        var fields = edit ? ProfileTestSupport.EditFields(target, "Odontologo") : CreateFields("999-1234567-8");
        fields["Cedula"] = "999-1234567-8";
        var url = edit ? $"/Usuarios/Edit/{target.Id}" : "/Usuarios/Create";

        var response = await ProfileTestSupport.PostAsync(client, url, fields);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Matches("(?:field-validation-error|validation-summary-errors)", await response.Content.ReadAsStringAsync());
        if (edit)
            Assert.Equal(target.Cedula, (await ProfileTestSupport.ReadAsync(factory, target.Id)).Cedula);
        else
        {
            using var scope = factory.Services.CreateScope();
            Assert.Null(await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
                .FindByEmailAsync(fields["Email"]));
        }
    }

    [Theory]
    [InlineData("00112345678", "001-1234567-8")]
    [InlineData("001-1234567-8", "001-1234567-8")]
    [InlineData("", null)]
    public async Task EditAdmin_CedulaOpcionalYExcluyePropiaCuentaDeDuplicados(string cedula, string? expected)
    {
        using var factory = new IdentityCookieWebApplicationFactory();
        var accounts = await SeedAsync(factory);
        var target = accounts["Doctor"];
        using var client = ProfileTestSupport.CreateClient(factory);
        await ProfileTestSupport.LoginAsync(client, ProfileTestSupport.InitialEmail);
        var fields = ProfileTestSupport.EditFields(target, "Odontologo");
        fields["Cedula"] = cedula;

        ProfileTestSupport.AssertRedirect(await ProfileTestSupport.PostAsync(client,
            $"/Usuarios/Edit/{target.Id}", fields), $"/Usuarios/Details/{target.Id}");

        Assert.Equal(expected, (await ProfileTestSupport.ReadAsync(factory, target.Id)).Cedula);
    }

    [Fact]
    public async Task Administrador_NoPuedeQuitarRolInicialPorEditNiCambiarCorreoDesdeOtraCuenta()
    {
        using var factory = new IdentityCookieWebApplicationFactory();
        var accounts = await SeedAsync(factory);
        var initial = accounts["Initial"];
        using var client = ProfileTestSupport.CreateClient(factory);
        await ProfileTestSupport.LoginAsync(client, accounts["SecondaryAdmin"].Email!);
        var fields = ProfileTestSupport.EditFields(initial, "Recepcionista");
        fields["Email"] = "replaced.initial@example.test";
        fields["EsAdministradorInicial"] = "false";

        var response = await ProfileTestSupport.PostAsync(client, $"/Usuarios/Edit/{initial.Id}", fields);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = await ProfileTestSupport.ReadAsync(factory, initial.Id);
        AssertIdentityEmail(stored, ProfileTestSupport.InitialEmail);
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Equal(new[] { "Administrador" }, await users.GetRolesAsync((await users.FindByIdAsync(initial.Id))!));
    }

    private static void AssertIdentityEmail(ApplicationUser user, string email)
    {
        Assert.Equal(email, user.Email);
        Assert.Equal(email, user.UserName);
        Assert.Equal(email.ToUpperInvariant(), user.NormalizedEmail);
        Assert.Equal(email.ToUpperInvariant(), user.NormalizedUserName);
    }

    private static void AssertDenied(HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden) return;
        ProfileTestSupport.AssertRedirect(response, "/Account/AccessDenied");
    }

    private static Dictionary<string, string> CreateFields(string cedula) => new()
    {
        ["Nombre"] = "Nueva", ["Apellido"] = "Cuenta", ["Email"] = "created.profile@example.test",
        ["Cedula"] = cedula, ["Telefono"] = "809-555-0123", ["Rol"] = "Recepcionista",
        ["Password"] = ProfileTestSupport.Password, ["ConfirmPassword"] = ProfileTestSupport.Password
    };

    private static async Task<Dictionary<string, ApplicationUser>> SeedAsync(IdentityCookieWebApplicationFactory factory)
    {
        var initial = await ProfileTestSupport.SeedInitialAdminAsync(factory);
        var accounts = new Dictionary<string, ApplicationUser> { ["Initial"] = initial };
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var (key, roles, cedula, active) in new (string, string[], string?, bool)[]
        {
            ("Doctor", ["Odontologo"], "00112345678", true),
            ("Reception", ["Recepcionista"], "002-1234567-8", true),
            ("Inactive", ["Recepcionista"], "99912345678", false),
            ("SecondaryAdmin", ["Administrador"], null, true),
            ("MixedAdmin", ["Administrador", "Odontologo"], null, true),
            ("NoRole", [], null, true)
        })
        {
            var email = $"profile.{key.ToLowerInvariant()}@example.test";
            var user = new ApplicationUser
            {
                Email = email, UserName = email, Nombre = key, Apellido = "Prueba", Cedula = cedula,
                Estado = active, PhoneNumber = "809-555-0100"
            };
            Assert.True((await users.CreateAsync(user, ProfileTestSupport.Password)).Succeeded);
            foreach (var role in roles) Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
            accounts.Add(key, user);
        }
        return accounts;
    }
}
