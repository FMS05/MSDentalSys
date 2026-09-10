using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MSDentalSys.Data.Context;
using MSDentalSys.Data.Models;
using MSDentalSys.Web.Controllers;
using MSDentalSys.Web.Models.ViewModels;
using Xunit;

namespace MSDentalSys.Tests.Controllers;

public class UsuariosControllerTests
{
    private const string Password = "Test1234!";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("correo-invalido")]
    public async Task Create_EmailInvalidoConModelStateValidado_DevuelveVistaSinCrearUsuario(string? email)
    {
        await using var database = await TestDatabase.CreateAsync();
        var controller = database.CreateController();
        var model = CreateModel("Usuario", "Prueba", email!, "Odontologo");
        ValidateCreateModel(controller, model);
        Assert.False(controller.ModelState.IsValid);

        var result = await controller.Create(model);

        Assert.Same(model, Assert.IsType<ViewResult>(result).Model);
        Assert.NotEmpty(controller.ModelState[nameof(model.Email)]!.Errors);
        Assert.Empty(await database.Context.Users.ToListAsync());
        Assert.False(controller.TempData.ContainsKey("SuccessMessage"));
    }

    [Fact]
    public async Task Create_EmailConEspaciosExternos_NormalizaEmailYUserName()
    {
        await using var database = await TestDatabase.CreateAsync();
        var controller = database.CreateController();
        var model = CreateModel("Usuario", "Prueba", " usuario@correo.com ", "Odontologo");
        ValidateCreateModel(controller, model);
        Assert.True(controller.ModelState.IsValid);

        var result = await controller.Create(model);

        Assert.IsType<RedirectToActionResult>(result);
        var user = await database.Context.Users.SingleAsync();
        Assert.Equal("usuario@correo.com", user.Email);
        Assert.Equal("usuario@correo.com", user.UserName);
        Assert.Equal(new[] { "Odontologo" }, await database.UserManager.GetRolesAsync(user));
    }

    private static void ValidateCreateModel(UsuariosController controller, UsuarioCreateViewModel model)
    {
        // Las llamadas directas no ejecutan DataAnnotations; trasladamos sus errores a ModelState.
        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), errors, validateAllProperties: true);
        foreach (var error in errors)
        {
            foreach (var member in error.MemberNames.DefaultIfEmpty(string.Empty))
            {
                controller.ModelState.AddModelError(member, error.ErrorMessage!);
            }
        }
    }

    [Fact]
    public async Task Create_OdontologoValido_CreaUsuarioYAsignaRol()
    {
        await using var database = await TestDatabase.CreateAsync();
        var controller = database.CreateController();
        var email = "odontologo.nuevo@example.test";

        var result = await controller.Create(CreateModel(
            "Odontólogo",
            "Prueba",
            email,
            "Odontologo"));

        Assert.IsType<RedirectToActionResult>(result);
        var user = await database.UserManager.FindByEmailAsync(email);
        Assert.NotNull(user);
        Assert.Equal(email, user!.UserName);
        Assert.Equal(email, user.Email);
        Assert.Equal("Odontólogo", user.Nombre);
        Assert.Equal("Prueba", user.Apellido);
        Assert.True(user.Estado);
        Assert.NotEqual(default, user.FechaCreacion);
        Assert.Contains("Odontologo", await database.UserManager.GetRolesAsync(user));
        Assert.True(await database.UserManager.CheckPasswordAsync(user, Password));
    }

    [Fact]
    public async Task Create_RecepcionistaValido_CreaUsuarioActivoConRolCorrecto()
    {
        await using var database = await TestDatabase.CreateAsync();
        var controller = database.CreateController();
        var email = "recepcionista.nueva@example.test";

        var result = await controller.Create(CreateModel(
            "Recepcionista",
            "Prueba",
            email,
            "Recepcionista"));

        Assert.IsType<RedirectToActionResult>(result);
        var user = await database.UserManager.FindByEmailAsync(email);
        Assert.NotNull(user);
        Assert.True(user!.Estado);
        Assert.Equal(["Recepcionista"], await database.UserManager.GetRolesAsync(user));
    }

    [Fact]
    public async Task Create_CorreoDuplicado_NoCreaSegundoUsuarioYAgregaError()
    {
        await using var database = await TestDatabase.CreateAsync();
        var email = "usuario.existente@example.test";
        await database.CreateUserAsync(email, "Odontologo", "Existente", "Prueba");
        var controller = database.CreateController();

        var result = await controller.Create(CreateModel(
            "Segundo",
            "Usuario",
            email,
            "Recepcionista"));

        Assert.IsType<ViewResult>(result);
        Assert.Single(controller.ModelState[nameof(UsuarioCreateViewModel.Email)]!.Errors);
        Assert.Contains("ya existe", controller.ModelState[nameof(UsuarioCreateViewModel.Email)]!.Errors[0].ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await database.Context.Users.CountAsync());
    }

    [Fact]
    public async Task Create_RolAdministrador_EsRechazado()
    {
        await using var database = await TestDatabase.CreateAsync();
        var controller = database.CreateController();
        var email = "administrador.no.permitido@example.test";

        var result = await controller.Create(CreateModel(
            "Administrador",
            "No Permitido",
            email,
            "Administrador"));

        Assert.IsType<ViewResult>(result);
        Assert.Single(controller.ModelState[nameof(UsuarioCreateViewModel.Rol)]!.Errors);
        Assert.Contains("rol", controller.ModelState[nameof(UsuarioCreateViewModel.Rol)]!.Errors[0].ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Null(await database.UserManager.FindByEmailAsync(email));
    }

    [Fact]
    public async Task Edit_OdontologoACambiaRecepcionista_CambiaRolYConservaDatos()
    {
        await using var database = await TestDatabase.CreateAsync();
        var user = await database.CreateUserAsync(
            "odontologo.editar@example.test",
            "Odontologo",
            "Nombre Original",
            "Apellido Original");
        var controller = database.CreateController();

        var result = await controller.Edit(user.Id, new UsuarioEditViewModel
        {
            Id = user.Id,
            Email = user.Email!,
            Nombre = user.Nombre,
            Apellido = user.Apellido,
            Telefono = "809-555-0109",
            Rol = "Recepcionista"
        });

        Assert.IsType<RedirectToActionResult>(result);
        var stored = await database.UserManager.FindByIdAsync(user.Id);
        Assert.NotNull(stored);
        Assert.Equal("Nombre Original", stored!.Nombre);
        Assert.Equal("Apellido Original", stored.Apellido);
        Assert.Equal("809-555-0109", stored.PhoneNumber);
        Assert.Equal(["Recepcionista"], await database.UserManager.GetRolesAsync(stored));
    }

    [Fact]
    public async Task Deactivate_UsuarioNoAdministrador_DesactivaSinEliminarlo()
    {
        await using var database = await TestDatabase.CreateAsync();
        var user = await database.CreateUserAsync(
            "usuario.desactivar@example.test",
            "Recepcionista",
            "Usuario",
            "Desactivable");
        var controller = database.CreateController();

        var result = await controller.Deactivate(user.Id);

        Assert.IsType<RedirectToActionResult>(result);
        var stored = await database.UserManager.FindByIdAsync(user.Id);
        Assert.NotNull(stored);
        Assert.False(stored!.Estado);
        Assert.Equal(1, await database.Context.Users.CountAsync(u => u.Id == user.Id));
    }

    [Fact]
    public async Task Activate_UsuarioInactivo_ActivaYConservaDatos()
    {
        await using var database = await TestDatabase.CreateAsync();
        var user = await database.CreateUserAsync(
            "usuario.activar@example.test",
            "Odontologo",
            "Usuario",
            "Activable",
            false);
        var controller = database.CreateController();

        var result = await controller.Activate(user.Id);

        Assert.IsType<RedirectToActionResult>(result);
        var stored = await database.UserManager.FindByIdAsync(user.Id);
        Assert.NotNull(stored);
        Assert.True(stored!.Estado);
        Assert.Equal("Usuario", stored.Nombre);
        Assert.Equal("Activable", stored.Apellido);
        Assert.Equal(["Odontologo"], await database.UserManager.GetRolesAsync(stored));
    }

    [Fact]
    public async Task Deactivate_AdministradorInicial_EsRechazadoYPermaneceActivo()
    {
        await using var database = await TestDatabase.CreateAsync();
        var admin = await database.CreateUserAsync(
            "admin@msdentalsys.local",
            "Administrador",
            "Administrador",
            "Sistema");
        var controller = database.CreateController();

        var result = await controller.Deactivate(admin.Id);

        Assert.IsType<RedirectToActionResult>(result);
        var stored = await database.UserManager.FindByIdAsync(admin.Id);
        Assert.NotNull(stored);
        Assert.True(stored!.Estado);
        Assert.Equal(["Administrador"], await database.UserManager.GetRolesAsync(stored));
        Assert.Contains("no puede ser desactivado", controller.TempData["ErrorMessage"]?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await database.Context.Users.CountAsync(u => u.Id == admin.Id));
    }

    [Fact]
    public async Task Edit_AdministradorInicial_CambioDeRolEsRechazado()
    {
        await using var database = await TestDatabase.CreateAsync();
        var admin = await database.CreateUserAsync(
            "admin@msdentalsys.local",
            "Administrador",
            "Administrador",
            "Sistema");
        var controller = database.CreateController();

        var result = await controller.Edit(admin.Id, new UsuarioEditViewModel
        {
            Id = admin.Id,
            Email = admin.Email!,
            Nombre = admin.Nombre,
            Apellido = admin.Apellido,
            Rol = "Odontologo"
        });

        Assert.IsType<ViewResult>(result);
        var stored = await database.UserManager.FindByIdAsync(admin.Id);
        Assert.NotNull(stored);
        var roles = await database.UserManager.GetRolesAsync(stored!);
        Assert.Equal(["Administrador"], roles);
        Assert.DoesNotContain("Odontologo", roles);
        Assert.Contains("no se puede cambiar el rol", controller.ModelState[nameof(UsuarioEditViewModel.Rol)]!.Errors[0].ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Deactivate_UsuarioActivo_RenuevaSecurityStamp()
    {
        await using var database = await TestDatabase.CreateAsync();
        var user = await database.CreateUserAsync("sello@example.test", "Recepcionista", "Usuario", "Prueba");
        var stamp = user.SecurityStamp;
        Assert.IsType<RedirectToActionResult>(await database.CreateController().Deactivate(user.Id));
        database.Context.ChangeTracker.Clear();
        var stored = await database.Context.Users.SingleAsync(u => u.Id == user.Id);
        Assert.False(stored.Estado);
        Assert.NotEqual(stamp, stored.SecurityStamp);
    }

    [Fact]
    public async Task Activate_TrasDesactivacion_ConservaSecurityStampRevocado()
    {
        await using var database = await TestDatabase.CreateAsync();
        var user = await database.CreateUserAsync("sello@example.test", "Recepcionista", "Usuario", "Prueba");
        var originalStamp = user.SecurityStamp;
        Assert.IsType<RedirectToActionResult>(await database.CreateController().Deactivate(user.Id));
        var revokedStamp = user.SecurityStamp;
        Assert.NotEqual(originalStamp, revokedStamp);
        Assert.IsType<RedirectToActionResult>(await database.CreateController().Activate(user.Id));
        database.Context.ChangeTracker.Clear();
        var stored = await database.Context.Users.SingleAsync(u => u.Id == user.Id);
        Assert.True(stored.Estado);
        Assert.Equal(revokedStamp, stored.SecurityStamp);
    }

    [Fact]
    public async Task Edit_CambioEfectivoDeRol_RenuevaSecurityStamp()
    {
        await using var database = await TestDatabase.CreateAsync();
        var user = await database.CreateUserAsync("sello@example.test", "Recepcionista", "Usuario", "Prueba");
        var stamp = user.SecurityStamp;
        var result = await database.CreateController().Edit(user.Id, new UsuarioEditViewModel
        {
            Id = user.Id, Email = user.Email!, Nombre = user.Nombre, Apellido = user.Apellido, Rol = "Odontologo"
        });
        Assert.IsType<RedirectToActionResult>(result);
        database.Context.ChangeTracker.Clear();
        var stored = await database.Context.Users.SingleAsync(u => u.Id == user.Id);
        Assert.Equal(["Odontologo"], await database.UserManager.GetRolesAsync(stored));
        Assert.NotEqual(stamp, stored.SecurityStamp);
    }

    [Fact]
    public async Task Edit_DatosPersonalesSinCambioDeRol_ConservaSecurityStamp()
    {
        await using var database = await TestDatabase.CreateAsync();
        var user = await database.CreateUserAsync("sello@example.test", "Recepcionista", "Usuario", "Prueba");
        var stamp = user.SecurityStamp;
        var result = await database.CreateController().Edit(user.Id, new UsuarioEditViewModel
        {
            Id = user.Id, Email = user.Email!, Nombre = "Nuevo", Apellido = "Apellido", Telefono = "809-555-0101", Rol = "Recepcionista"
        });
        Assert.IsType<RedirectToActionResult>(result);
        database.Context.ChangeTracker.Clear();
        var stored = await database.Context.Users.SingleAsync(u => u.Id == user.Id);
        Assert.Equal("Nuevo", stored.Nombre);
        Assert.Equal("Apellido", stored.Apellido);
        Assert.Equal("809-555-0101", stored.PhoneNumber);
        Assert.Equal(["Recepcionista"], await database.UserManager.GetRolesAsync(stored));
        Assert.Equal(stamp, stored.SecurityStamp);
    }

    [Fact]
    public async Task Deactivate_AdministradorInicialRechazado_ConservaSecurityStamp()
    {
        await using var database = await TestDatabase.CreateAsync();
        var user = await database.CreateUserAsync("admin@msdentalsys.local", "Administrador", "Administrador", "Sistema");
        var stamp = user.SecurityStamp;
        var controller = database.CreateController();
        Assert.IsType<RedirectToActionResult>(await controller.Deactivate(user.Id));
        Assert.NotNull(controller.TempData["ErrorMessage"]);
        database.Context.ChangeTracker.Clear();
        var stored = await database.Context.Users.SingleAsync(u => u.Id == user.Id);
        Assert.True(stored.Estado);
        Assert.Equal(stamp, stored.SecurityStamp);
    }

    [Fact]
    public async Task Edit_AdministradorInicialRechazado_ConservaSecurityStamp()
    {
        await using var database = await TestDatabase.CreateAsync();
        var user = await database.CreateUserAsync("admin@msdentalsys.local", "Administrador", "Administrador", "Sistema");
        var stamp = user.SecurityStamp;
        var controller = database.CreateController();
        var result = await controller.Edit(user.Id, new UsuarioEditViewModel
        {
            Id = user.Id, Email = user.Email!, Nombre = user.Nombre, Apellido = user.Apellido, Rol = "Odontologo"
        });
        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        database.Context.ChangeTracker.Clear();
        var stored = await database.Context.Users.SingleAsync(u => u.Id == user.Id);
        Assert.Equal(["Administrador"], await database.UserManager.GetRolesAsync(stored));
        Assert.Equal(stamp, stored.SecurityStamp);
    }

    private static UsuarioCreateViewModel CreateModel(string firstName, string lastName, string email, string role)
    {
        return new UsuarioCreateViewModel
        {
            Nombre = firstName,
            Apellido = lastName,
            Email = email,
            Telefono = "809-555-0110",
            Rol = role,
            Password = Password,
            ConfirmPassword = Password
        };
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _services;

        private TestDatabase(SqliteConnection connection, ApplicationDbContext context, ServiceProvider services)
        {
            _connection = connection;
            Context = context;
            _services = services;
        }

        public ApplicationDbContext Context { get; }
        public UserManager<ApplicationUser> UserManager => _services.GetRequiredService<UserManager<ApplicationUser>>();
        private RoleManager<IdentityRole> RoleManager => _services.GetRequiredService<RoleManager<IdentityRole>>();

        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new ApplicationDbContext(options);
            await context.Database.EnsureCreatedAsync();

            var services = new ServiceCollection()
                .AddSingleton(context)
                .AddLogging()
                .AddIdentityCore<ApplicationUser>(identityOptions =>
                {
                    identityOptions.Password.RequiredLength = 8;
                    identityOptions.Password.RequireDigit = true;
                    identityOptions.Password.RequireUppercase = true;
                    identityOptions.Password.RequireLowercase = true;
                    identityOptions.Password.RequireNonAlphanumeric = true;
                })
                .AddRoles<IdentityRole>()
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .Services
                .BuildServiceProvider();

            var database = new TestDatabase(connection, context, services);
            await database.CreateRolesAsync();
            return database;
        }

        public async Task<ApplicationUser> CreateUserAsync(
            string email,
            string role,
            string firstName,
            string lastName,
            bool state = true)
        {
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                Nombre = firstName,
                Apellido = lastName,
                Estado = state,
                FechaCreacion = DateTime.Now
            };
            var createResult = await UserManager.CreateAsync(user, Password);
            if (!createResult.Succeeded)
            {
                throw new InvalidOperationException(string.Join("; ", createResult.Errors.Select(error => error.Description)));
            }

            var roleResult = await UserManager.AddToRoleAsync(user, role);
            if (!roleResult.Succeeded)
            {
                throw new InvalidOperationException(string.Join("; ", roleResult.Errors.Select(error => error.Description)));
            }

            return user;
        }

        public UsuariosController CreateController()
        {
            var httpContext = new DefaultHttpContext();
            var controller = new UsuariosController(UserManager)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = httpContext
                }
            };
            controller.TempData = new TempDataDictionary(
                httpContext,
                new RecordingTempDataProvider(new Dictionary<string, object?>()));
            return controller;
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
            _services.Dispose();
        }

        private async Task CreateRolesAsync()
        {
            foreach (var roleName in new[] { "Administrador", "Odontologo", "Recepcionista" })
            {
                var result = await RoleManager.CreateAsync(new IdentityRole(roleName));
                if (!result.Succeeded)
                {
                    throw new InvalidOperationException(string.Join("; ", result.Errors.Select(error => error.Description)));
                }
            }
        }
    }

    private sealed class RecordingTempDataProvider : ITempDataProvider
    {
        private readonly IDictionary<string, object?> _values;

        public RecordingTempDataProvider(IDictionary<string, object?> values)
        {
            _values = values;
        }

        public IDictionary<string, object?> LoadTempData(HttpContext context) => _values;

        public void SaveTempData(HttpContext context, IDictionary<string, object?> values)
        {
            foreach (var pair in values)
            {
                _values[pair.Key] = pair.Value;
            }
        }
    }
}
