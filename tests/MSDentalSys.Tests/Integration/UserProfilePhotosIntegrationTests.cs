using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSDentalSys.Data.Models;
using MSDentalSys.Web.Security;
using Xunit;

namespace MSDentalSys.Tests.Integration;

public sealed class UserProfilePhotosIntegrationTests
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jX1sAAAAASUVORK5CYII=");

    [Theory]
    [InlineData("Initial", "Administrador")]
    [InlineData("Doctor", "Odontologo")]
    [InlineData("Reception", "Recepcionista")]
    public async Task FotoPropia_CargaReemplazaYElimina_ConservaIdentidadYDatosProtegidos(string actor, string role)
    {
        using var fixture = new PhotoFixture();
        var accounts = await fixture.SeedAsync();
        var original = accounts[actor];
        using var client = fixture.CreateClient();
        await ProfileTestSupport.LoginAsync(client, original.Email!);
        var url = $"/Usuarios/Edit/{original.Id}";
        var fields = ProfileTestSupport.EditFields(original, role);
        fields["FotoPerfil"] = "../../forged.svg";
        fields["PasswordHash"] = "forged";
        fields["Estado"] = "false";
        fields["PuedeEditarDatosAdministrativos"] = "true";
        if (role != "Administrador")
        {
            fields["Cedula"] = "999-9999999-9";
            fields["Rol"] = "Administrador";
            fields["Nombre"] = "Manipulado";
        }

        ProfileTestSupport.AssertRedirect(await PostPhotoAsync(client, url, fields, Png), $"/Usuarios/Details/{original.Id}");
        var first = await fixture.ReadAsync(original.Id);
        Assert.NotNull(first.FotoPerfil);
        Assert.Equal(Png, await File.ReadAllBytesAsync(fixture.PhotoPath(first.FotoPerfil)));
        Assert.False(Directory.Exists(Path.Combine(fixture.RootPath, "wwwroot")));
        var response = await client.GetAsync($"/Usuarios/Photo/{original.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Png, await response.Content.ReadAsByteArrayAsync());
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        foreach (var directUrl in new[] { $"/App_Data/ProfilePhotos/{first.FotoPerfil}", $"/ProfilePhotos/{first.FotoPerfil}" })
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(directUrl)).StatusCode);

        ProfileTestSupport.AssertRedirect(await PostPhotoAsync(client, url, fields, Png), $"/Usuarios/Details/{original.Id}");
        var second = await fixture.ReadAsync(original.Id);
        Assert.NotEqual(first.FotoPerfil, second.FotoPerfil);
        Assert.False(File.Exists(fixture.PhotoPath(first.FotoPerfil)));
        Assert.True(File.Exists(fixture.PhotoPath(second.FotoPerfil!)));
        Assert.Single(fixture.PhotoFiles());

        fields["EliminarFoto"] = "true";
        ProfileTestSupport.AssertRedirect(await PostPhotoAsync(client, url, fields), $"/Usuarios/Details/{original.Id}");
        var deleted = await fixture.ReadAsync(original.Id);
        Assert.Null(deleted.FotoPerfil);
        Assert.Empty(fixture.PhotoFiles());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Usuarios/Photo/{original.Id}")).StatusCode);
        Assert.Equal(original.Email, deleted.Email);
        Assert.Equal(original.UserName, deleted.UserName);
        Assert.Equal(original.Nombre, deleted.Nombre);
        Assert.Equal(original.Cedula, deleted.Cedula);
        Assert.Equal(original.PasswordHash, deleted.PasswordHash);
        Assert.Equal(original.Estado, deleted.Estado);
    }

    [Theory]
    [InlineData("Initial")]
    [InlineData("Doctor")]
    [InlineData("Reception")]
    public async Task Photo_AplicaMismosPermisosQueDetails_IncluyeAdministradoresMixtosEInactivos(string actor)
    {
        using var fixture = new PhotoFixture();
        var accounts = await fixture.SeedAsync();
        foreach (var account in accounts.Values)
            await fixture.SeedPhotoAsync(account.Id);
        using var client = fixture.CreateClient();
        await ProfileTestSupport.LoginAsync(client, accounts[actor].Email!);

        foreach (var (key, account) in accounts)
        {
            var expected = actor == "Initial" || key is "Doctor" or "Reception" or "Inactive"
                ? HttpStatusCode.OK : HttpStatusCode.NotFound;
            Assert.Equal(expected, (await client.GetAsync($"/Usuarios/Details/{account.Id}")).StatusCode);
            Assert.Equal(expected, (await client.GetAsync($"/Usuarios/Photo/{account.Id}")).StatusCode);
        }
        // Un id en query no sustituye el objetivo de la ruta autorizada.
        if (actor != "Initial")
            Assert.Equal(HttpStatusCode.NotFound,
                (await client.GetAsync($"/Usuarios/Photo/{accounts["Initial"].Id}?id={accounts[actor].Id}")).StatusCode);
        using var anonymous = fixture.CreateClient();
        ProfileTestSupport.AssertRedirect(await anonymous.GetAsync($"/Usuarios/Photo/{accounts["Doctor"].Id}"), "/Account/Login");
    }

    [Theory]
    [InlineData("Doctor")]
    [InlineData("Reception")]
    public async Task Multipart_OperativoNoPuedeCambiarFotoAjena(string actor)
    {
        using var fixture = new PhotoFixture();
        var accounts = await fixture.SeedAsync();
        using var client = fixture.CreateClient();
        await ProfileTestSupport.LoginAsync(client, accounts[actor].Email!);
        var target = accounts[actor == "Doctor" ? "Reception" : "Doctor"];
        var response = await PostPhotoAsync(client, $"/Usuarios/Edit/{target.Id}",
            ProfileTestSupport.EditFields(target, "Administrador"), Png,
            formUrl: $"/Usuarios/Edit/{accounts[actor].Id}");
        ProfileTestSupport.AssertRedirect(response, "/Account/AccessDenied");
        Assert.Null((await fixture.ReadAsync(target.Id)).FotoPerfil);
        Assert.Empty(fixture.PhotoFiles());
    }

    [Fact]
    public async Task Multipart_SinAntiforgery_NoEscribeFotoNiActualizaPerfil()
    {
        using var fixture = new PhotoFixture();
        var accounts = await fixture.SeedAsync();
        var target = accounts["Doctor"];
        using var client = fixture.CreateClient();
        await ProfileTestSupport.LoginAsync(client, target.Email!);
        var fields = ProfileTestSupport.EditFields(target, "Odontologo");
        fields["Telefono"] = "8095559999";

        var response = await PostPhotoAsync(client, $"/Usuarios/Edit/{target.Id}", fields, Png, antiforgery: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(target.PhoneNumber, (await fixture.ReadAsync(target.Id)).PhoneNumber);
        Assert.Null((await fixture.ReadAsync(target.Id)).FotoPerfil);
        Assert.Empty(fixture.PhotoFiles());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Multipart_FotoInvalida_NoModificaDatosNiFotoAnterior(bool deleteAndUpload)
    {
        using var fixture = new PhotoFixture();
        var accounts = await fixture.SeedAsync();
        var target = accounts["Doctor"];
        var oldPhoto = await fixture.SeedPhotoAsync(target.Id);
        using var client = fixture.CreateClient();
        await ProfileTestSupport.LoginAsync(client, target.Email!);
        var fields = ProfileTestSupport.EditFields(target, "Odontologo");
        fields["Telefono"] = "8095559999";
        fields["EliminarFoto"] = deleteAndUpload.ToString();

        var response = await PostPhotoAsync(client, $"/Usuarios/Edit/{target.Id}", fields,
            deleteAndUpload ? Png : "<script>invalid</script>"u8.ToArray());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("field-validation-error", await response.Content.ReadAsStringAsync());
        var stored = await fixture.ReadAsync(target.Id);
        Assert.Equal(oldPhoto, stored.FotoPerfil);
        Assert.Equal(target.PhoneNumber, stored.PhoneNumber);
        Assert.Equal(Png, await File.ReadAllBytesAsync(fixture.PhotoPath(oldPhoto)));
        Assert.Single(fixture.PhotoFiles());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_ConFoto_GuardaCuentaOEliminaArchivoSiIdentityRechazaPassword(bool weakPassword)
    {
        using var fixture = new PhotoFixture();
        await fixture.SeedAsync();
        using var client = fixture.CreateClient();
        await ProfileTestSupport.LoginAsync(client, ProfileTestSupport.InitialEmail);
        var password = weakPassword ? "weak" : ProfileTestSupport.Password;
        var fields = new Dictionary<string, string>
        {
            ["Nombre"] = "Nueva", ["Apellido"] = "Cuenta", ["Email"] = "photo.created@example.test",
            ["Cedula"] = "00312345678", ["Rol"] = "Recepcionista",
            ["Password"] = password, ["ConfirmPassword"] = password, ["FotoPerfil"] = "forged.png"
        };

        var response = await PostPhotoAsync(client, "/Usuarios/Create", fields, Png);

        using var scope = fixture.Factory.Services.CreateScope();
        var created = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
            .FindByEmailAsync(fields["Email"]);
        if (weakPassword)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Null(created);
            Assert.Empty(fixture.PhotoFiles());
        }
        else
        {
            ProfileTestSupport.AssertRedirect(response, "/Usuarios");
            Assert.NotNull(created);
            Assert.NotEqual("forged.png", created.FotoPerfil);
            Assert.Equal("003-1234567-8", created.Cedula);
            Assert.Equal(Png, await File.ReadAllBytesAsync(fixture.PhotoPath(created.FotoPerfil!)));
            Assert.Single(fixture.PhotoFiles());
        }
    }

    [Fact]
    public async Task Edit_FalloDespuesDeGuardarFotoYCorreo_RevierteDbYBorraSoloArchivoNuevo()
    {
        using var fixture = new PhotoFixture();
        var accounts = await fixture.SeedAsync();
        var target = accounts["Doctor"];
        var oldPhoto = await fixture.SeedPhotoAsync(target.Id);
        var original = await fixture.ReadAsync(target.Id);
        using var client = fixture.CreateClient();
        await ProfileTestSupport.LoginAsync(client, ProfileTestSupport.InitialEmail);
        var fields = ProfileTestSupport.EditFields(original, "Odontologo");
        fields["Email"] = "photo.changed@example.test";
        fields["Nombre"] = "Debe revertirse";
        fixture.Failure.Enabled = true;

        var response = await PostPhotoAsync(client, $"/Usuarios/Edit/{target.Id}", fields, Png);

        Assert.True(fixture.Failure.Reached);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = await fixture.ReadAsync(target.Id);
        Assert.Equal(original.FotoPerfil, stored.FotoPerfil);
        Assert.Equal(original.Email, stored.Email);
        Assert.Equal(original.UserName, stored.UserName);
        Assert.Equal(original.Nombre, stored.Nombre);
        Assert.Equal(original.SecurityStamp, stored.SecurityStamp);
        Assert.Equal(original.ConcurrencyStamp, stored.ConcurrencyStamp);
        Assert.Equal(fixture.PhotoPath(oldPhoto), Assert.Single(fixture.PhotoFiles()));
        Assert.Equal(Png, await File.ReadAllBytesAsync(fixture.PhotoPath(oldPhoto)));
    }

    private static async Task<HttpResponseMessage> PostPhotoAsync(HttpClient client, string url,
        Dictionary<string, string> fields, byte[]? bytes = null, bool antiforgery = true, string? formUrl = null)
    {
        using var content = new MultipartFormDataContent();
        foreach (var (key, value) in fields) content.Add(new StringContent(value), key);
        if (antiforgery)
        {
            var html = await ProfileTestSupport.GetHtmlAsync(client, formUrl ?? url);
            var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
            Assert.True(token.Success);
            content.Add(new StringContent(WebUtility.HtmlDecode(token.Groups[1].Value)), "__RequestVerificationToken");
        }
        if (bytes is not null)
        {
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            content.Add(file, "FotoArchivo", "original.png");
        }
        return await client.PostAsync(url, content);
    }

    private sealed class PhotoFixture : IDisposable
    {
        private readonly IdentityCookieWebApplicationFactory source = new();
        public WebApplicationFactory<Program> Factory { get; }
        public string RootPath { get; } = Path.Combine(Path.GetTempPath(), "MSDentalSys-PhotoIntegration", Guid.NewGuid().ToString("N"));
        public FailureSwitch Failure { get; } = new();

        public PhotoFixture()
        {
            Factory = source.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<UserProfilePhotoStore>();
                services.AddScoped(_ => new UserProfilePhotoStore(new PhotoEnvironment { ContentRootPath = RootPath }));
                services.AddSingleton(Failure);
                services.AddScoped<UserManager<ApplicationUser>, FailingUserNameManager>();
            }));
        }

        public HttpClient CreateClient() => Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true
        });

        public async Task<Dictionary<string, ApplicationUser>> SeedAsync()
        {
            using var scope = Factory.Services.CreateScope();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            foreach (var role in new[] { "Administrador", "Odontologo", "Recepcionista" })
                Assert.True((await roles.CreateAsync(new IdentityRole(role))).Succeeded);
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var accounts = new Dictionary<string, ApplicationUser>();
            foreach (var (key, accountRoles, active) in new (string, string[], bool)[]
            {
                ("Initial", ["Administrador"], true), ("Doctor", ["Odontologo"], true),
                ("Reception", ["Recepcionista"], true), ("MixedAdmin", ["Administrador", "Odontologo"], true),
                ("Inactive", ["Recepcionista"], false), ("NoRole", [], true)
            })
            {
                var email = key == "Initial" ? ProfileTestSupport.InitialEmail : $"photos.{key.ToLowerInvariant()}@example.test";
                var account = new ApplicationUser { Email = email, UserName = email, Nombre = key, Apellido = "Fotos", Estado = active };
                Assert.True((await users.CreateAsync(account, ProfileTestSupport.Password)).Succeeded);
                foreach (var role in accountRoles) Assert.True((await users.AddToRoleAsync(account, role)).Succeeded);
                accounts.Add(key, account);
            }
            return accounts;
        }

        public async Task<ApplicationUser> ReadAsync(string id)
        {
            using var scope = Factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
                .Users.AsNoTracking().SingleAsync(user => user.Id == id);
        }

        public async Task<string> SeedPhotoAsync(string id)
        {
            using var scope = Factory.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByIdAsync(id))!;
            var photo = await scope.ServiceProvider.GetRequiredService<UserProfilePhotoStore>().ValidateAndSaveAsync(
                new Microsoft.AspNetCore.Http.FormFile(new MemoryStream(Png), 0, Png.Length, "FotoArchivo", "seed.png")
                { Headers = new Microsoft.AspNetCore.Http.HeaderDictionary(), ContentType = "image/png" });
            Assert.Null(photo.Error);
            user.FotoPerfil = photo.FileName;
            Assert.True((await users.UpdateAsync(user)).Succeeded);
            return photo.FileName!;
        }

        public string PhotoPath(string key) => Path.Combine(RootPath, "App_Data", "ProfilePhotos", key);
        public string[] PhotoFiles() => Directory.Exists(Path.Combine(RootPath, "App_Data", "ProfilePhotos"))
            ? Directory.GetFiles(Path.Combine(RootPath, "App_Data", "ProfilePhotos")) : [];

        public void Dispose()
        {
            Factory.Dispose();
            source.Dispose();
            var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "MSDentalSys-PhotoIntegration")) + Path.DirectorySeparatorChar;
            var target = Path.GetFullPath(RootPath);
            if (target.StartsWith(root, StringComparison.OrdinalIgnoreCase) && Directory.Exists(target))
                Directory.Delete(target, recursive: true);
        }
    }

    private sealed class PhotoEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "PhotoIntegration";
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class FailureSwitch
    {
        public bool Enabled { get; set; }
        public bool Reached { get; set; }
    }

    private sealed class FailingUserNameManager(
        IUserStore<ApplicationUser> store, IOptions<IdentityOptions> options, IPasswordHasher<ApplicationUser> hasher,
        IEnumerable<IUserValidator<ApplicationUser>> validators, IEnumerable<IPasswordValidator<ApplicationUser>> passwordValidators,
        ILookupNormalizer normalizer, IdentityErrorDescriber errors, IServiceProvider services,
        ILogger<UserManager<ApplicationUser>> logger, FailureSwitch failure)
        : UserManager<ApplicationUser>(store, options, hasher, validators, passwordValidators, normalizer, errors, services, logger)
    {
        public override Task<IdentityResult> SetUserNameAsync(ApplicationUser user, string? userName)
        {
            if (!failure.Enabled) return base.SetUserNameAsync(user, userName);
            failure.Reached = true;
            return Task.FromResult(IdentityResult.Failed(new IdentityError { Code = "TestFailure", Description = "Fallo de prueba controlado." }));
        }
    }
}
