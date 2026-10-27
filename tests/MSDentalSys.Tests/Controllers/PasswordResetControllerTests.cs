using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSDentalSys.Data.Context;
using MSDentalSys.Data.Models;
using MSDentalSys.Tests.Integration;
using MSDentalSys.Web.Controllers;
using MSDentalSys.Web.Models.ViewModels;
using Xunit;

namespace MSDentalSys.Tests.Controllers;

public class PasswordResetControllerTests
{
    private const string OriginalPassword = "Original123!";
    private const string NewPassword = "Renovada456!";

    [Theory]
    [InlineData("Odontologo", true)]
    [InlineData("Recepcionista", true)]
    [InlineData("Odontologo", false)]
    [InlineData("Recepcionista", false)]
    public async Task Reset_Permitido_ActualizaCredencialYStampConservaEstado(string role, bool active)
    {
        await using var test = await ResetContext.CreateAsync();
        var user = await test.CreateUserAsync(role, active);
        var stamp = await test.Users.GetSecurityStampAsync(user);
        var controller = test.Controller();

        var response = Assert.IsType<RedirectToActionResult>(await controller.ResetPassword(user.Id, Model()));

        Assert.Equal(nameof(UsuariosController.Details), response.ActionName);
        Assert.Equal(user.Id, response.RouteValues!["id"]);
        test.Database.ChangeTracker.Clear();
        var saved = (await test.Users.FindByIdAsync(user.Id))!;
        Assert.Equal(active, saved.Estado);
        Assert.True(await test.Users.CheckPasswordAsync(saved, NewPassword));
        Assert.False(await test.Users.CheckPasswordAsync(saved, OriginalPassword));
        Assert.NotEqual(stamp, await test.Users.GetSecurityStampAsync(saved));
        var message = Assert.IsType<string>(controller.TempData["SuccessMessage"]);
        if (!active) Assert.Contains("continúa inactiva", message);
        Assert.Equal(1, test.Users.TokenGenerationCount);
        Assert.NotNull(test.Users.LastGeneratedToken);
        Assert.DoesNotContain(test.Users.LastGeneratedToken!, message);
        Assert.DoesNotContain(NewPassword, message);
        Assert.Single(controller.TempData);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Reset_AdministradorObjetivo_ProhibidoInclusoActorYMultiplesRoles(bool sameActor, bool multipleRoles)
    {
        await using var test = await ResetContext.CreateAsync();
        var admin = await test.CreateUserAsync("Administrador");
        if (multipleRoles) Assert.True((await test.Users.AddToRoleAsync(admin, "Odontologo")).Succeeded);
        var controller = test.Controller(sameActor ? admin.Id : "otro-administrador");

        Assert.IsType<ForbidResult>(await controller.ResetPassword(admin.Id));
        Assert.IsType<ForbidResult>(await controller.ResetPassword(admin.Id, Model()));
        Assert.Equal(0, test.Users.TokenGenerationCount);
        Assert.True(await test.Users.CheckPasswordAsync(admin, OriginalPassword));
        var details = Assert.IsType<UsuarioDetailsViewModel>(Assert.IsType<ViewResult>(await controller.Details(admin.Id)).Model);
        Assert.False(details.PuedeRestablecerPassword);
    }

    [Fact]
    public async Task Reset_NoIntentaRechazarLaClaveAnteriorQueElActorNoConoce()
    {
        await using var test = await ResetContext.CreateAsync();
        var user = await test.CreateUserAsync("Odontologo");
        var stamp = await test.Users.GetSecurityStampAsync(user);

        Assert.IsType<RedirectToActionResult>(await test.Controller().ResetPassword(user.Id, Model(OriginalPassword)));

        Assert.True(await test.Users.CheckPasswordAsync(user, OriginalPassword));
        Assert.NotEqual(stamp, await test.Users.GetSecurityStampAsync(user));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-existe")]
    [InlineData("../../Administrador")]
    public async Task Reset_IdAusenteOInexistente_Devuelve404(string? id)
    {
        await using var test = await ResetContext.CreateAsync();
        var controller = test.Controller();
        Assert.IsType<NotFoundResult>(await controller.ResetPassword(id));
        Assert.IsType<NotFoundResult>(await controller.ResetPassword(id, Model()));
        Assert.Equal(0, test.Users.TokenGenerationCount);
    }

    [Fact]
    public async Task Reset_ActorConCookieDeAdministradorYRolDegradado_NoPuedeRestablecerse()
    {
        await using var test = await ResetContext.CreateAsync();
        var actor = await test.CreateUserAsync("Odontologo");
        var controller = test.Controller(actor.Id);

        Assert.IsType<ForbidResult>(await controller.ResetPassword(actor.Id));
        Assert.IsType<ForbidResult>(await controller.ResetPassword(actor.Id, Model()));
        var details = Assert.IsType<UsuarioDetailsViewModel>(Assert.IsType<ViewResult>(await controller.Details(actor.Id)).Model);
        Assert.False(details.PuedeRestablecerPassword);
        Assert.Equal(0, test.Users.TokenGenerationCount);
    }

    [Fact]
    public async Task Reset_SinRolPermitido_Prohibido()
    {
        await using var test = await ResetContext.CreateAsync();
        var user = await test.CreateUserAsync("Odontologo");
        Assert.True((await test.Users.RemoveFromRoleAsync(user, "Odontologo")).Succeeded);
        var controller = test.Controller();
        Assert.IsType<ForbidResult>(await controller.ResetPassword(user.Id));
        Assert.IsType<ForbidResult>(await controller.ResetPassword(user.Id, Model()));
        var details = Assert.IsType<UsuarioDetailsViewModel>(Assert.IsType<ViewResult>(await controller.Details(user.Id)).Model);
        Assert.False(details.PuedeRestablecerPassword);
    }

    [Theory]
    [InlineData("Odontologo")]
    [InlineData("Recepcionista")]
    public async Task Reset_GetYDetalle_UsanInformacionRealSinSecretos(string role)
    {
        await using var test = await ResetContext.CreateAsync();
        var user = await test.CreateUserAsync(role, false);
        var controller = test.Controller();
        var model = Assert.IsType<ResetPasswordViewModel>(Assert.IsType<ViewResult>(await controller.ResetPassword(user.Id)).Model);
        Assert.Equal(user.Id, model.Id);
        Assert.Equal("Usuario Prueba", model.NombreCompleto);
        Assert.Equal(user.Email, model.Email);
        Assert.False(model.Estado);
        Assert.Empty(model.NewPassword);
        Assert.Empty(model.ConfirmPassword);
        var details = Assert.IsType<UsuarioDetailsViewModel>(Assert.IsType<ViewResult>(await controller.Details(user.Id)).Model);
        Assert.True(details.PuedeRestablecerPassword);
        Assert.Equal(0, test.Users.TokenGenerationCount);
    }

    [Theory]
    [InlineData("corta")]
    [InlineData("SinDigitos!")]
    [InlineData("sinmayusculas123!")]
    [InlineData("SINMINUSCULAS123!")]
    [InlineData("SinSimbolos123")]
    public async Task Reset_IdentityRechazaComplejidad_ConservaCredencialYSinRepoblar(string password)
    {
        await using var test = await ResetContext.CreateAsync();
        var user = await test.CreateUserAsync("Odontologo");
        var stamp = await test.Users.GetSecurityStampAsync(user);
        var controller = test.Controller();
        controller.ModelState.SetModelValue("NewPassword", password, password);
        controller.ModelState.SetModelValue("ConfirmPassword", password, password);
        // La llamada directa no ejecuta la validación MVC: ambos campos cumplen las
        // anotaciones requerido/confirmación; la complejidad corresponde a Identity.
        foreach (var entry in controller.ModelState.Values)
            entry.ValidationState = ModelValidationState.Valid;

        var result = await controller.ResetPassword(user.Id, Model(password));

        AssertBlankPasswords(controller, result);
        Assert.NotEmpty(controller.ModelState["NewPassword"]!.Errors);
        var saved = (await test.Users.FindByIdAsync(user.Id))!;
        Assert.Equal(stamp, await test.Users.GetSecurityStampAsync(saved));
        Assert.True(await test.Users.CheckPasswordAsync(saved, OriginalPassword));
        Assert.Empty(controller.TempData);
    }

    [Theory]
    [InlineData(NewPassword, "Diferente789!")]
    [InlineData("", "")]
    public async Task Reset_ValidacionModelo_NoGeneraTokenNiRepuebla(string password, string confirmation)
    {
        await using var test = await ResetContext.CreateAsync();
        var user = await test.CreateUserAsync("Recepcionista");
        var controller = test.Controller();
        var model = Model(password);
        model.ConfirmPassword = confirmation;
        var errors = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), errors, true));
        foreach (var error in errors)
            foreach (var member in error.MemberNames.DefaultIfEmpty(string.Empty))
                controller.ModelState.AddModelError(member, error.ErrorMessage!);
        controller.ModelState.SetModelValue("NewPassword", password, password);
        controller.ModelState.SetModelValue("ConfirmPassword", confirmation, confirmation);

        AssertBlankPasswords(controller, await controller.ResetPassword(user.Id, model));
        Assert.Equal(0, test.Users.TokenGenerationCount);
        Assert.True(await test.Users.CheckPasswordAsync(user, OriginalPassword));
    }

    [Fact]
    public async Task Reset_DatosInyectadosEnModelo_NoCambianObjetivoNiMetadatos()
    {
        await using var test = await ResetContext.CreateAsync();
        var target = await test.CreateUserAsync("Recepcionista", false);
        var other = await test.CreateUserAsync("Administrador");
        var model = Model();
        model.Id = other.Id;
        model.NombreCompleto = "Nombre inyectado";
        model.Email = other.Email!;
        model.Estado = true;

        Assert.IsType<RedirectToActionResult>(await test.Controller(other.Id).ResetPassword(target.Id, model));

        test.Database.ChangeTracker.Clear();
        var saved = (await test.Users.FindByIdAsync(target.Id))!;
        Assert.False(saved.Estado);
        Assert.Equal(target.Email, saved.Email);
        Assert.Equal("Usuario", saved.Nombre);
        Assert.True(await test.Users.CheckPasswordAsync(saved, NewPassword));
        Assert.True(await test.Users.CheckPasswordAsync((await test.Users.FindByIdAsync(other.Id))!, OriginalPassword));
    }

    [Fact]
    public async Task Reset_CambioConcurrenteDeCuenta_NoSobrescribeEstadoNiCredencial()
    {
        await using var test = await ResetContext.CreateAsync();
        var user = await test.CreateUserAsync("Odontologo");
        test.Users.BeforeReset = async () =>
        {
            using var otherScope = test.Factory.Services.CreateScope();
            var otherManager = otherScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var concurrent = (await otherManager.FindByIdAsync(user.Id))!;
            concurrent.Estado = false;
            concurrent.Nombre = "Actualizado";
            Assert.True((await otherManager.UpdateAsync(concurrent)).Succeeded);
        };
        var controller = test.Controller();

        var result = await controller.ResetPassword(user.Id, Model());

        AssertBlankPasswords(controller, result);
        Assert.Contains(controller.ModelState[string.Empty]!.Errors, error => error.ErrorMessage.Contains("cuenta cambió"));
        var viewModel = Assert.IsType<ResetPasswordViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.False(viewModel.Estado);
        Assert.Equal("Actualizado Prueba", viewModel.NombreCompleto);
        var saved = (await test.Users.FindByIdAsync(user.Id))!;
        Assert.False(saved.Estado);
        Assert.True(await test.Users.CheckPasswordAsync(saved, OriginalPassword));
        Assert.False(await test.Users.CheckPasswordAsync(saved, NewPassword));
        Assert.Empty(controller.TempData);
    }

    [Fact]
    public async Task Reset_PromocionConcurrenteAAdministrador_RechazaSinModificarCredencial()
    {
        await using var test = await ResetContext.CreateAsync();
        var user = await test.CreateUserAsync("Recepcionista");
        test.Users.BeforeReset = async () =>
        {
            using var otherScope = test.Factory.Services.CreateScope();
            var otherManager = otherScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await otherManager.AddToRoleAsync((await otherManager.FindByIdAsync(user.Id))!, "Administrador")).Succeeded);
        };

        Assert.IsType<ForbidResult>(await test.Controller().ResetPassword(user.Id, Model()));

        var saved = (await test.Users.FindByIdAsync(user.Id))!;
        Assert.True(await test.Users.IsInRoleAsync(saved, "Administrador"));
        Assert.True(await test.Users.CheckPasswordAsync(saved, OriginalPassword));
        Assert.False(await test.Users.CheckPasswordAsync(saved, NewPassword));
    }

    [Fact]
    public async Task Reset_EliminacionConcurrente_Devuelve404SinRecrearCuenta()
    {
        await using var test = await ResetContext.CreateAsync();
        var user = await test.CreateUserAsync("Odontologo");
        test.Users.BeforeReset = async () =>
        {
            using var otherScope = test.Factory.Services.CreateScope();
            var otherManager = otherScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await otherManager.DeleteAsync((await otherManager.FindByIdAsync(user.Id))!)).Succeeded);
        };

        Assert.IsType<NotFoundResult>(await test.Controller().ResetPassword(user.Id, Model()));
        Assert.Null(await test.Users.FindByIdAsync(user.Id));
    }

    [Fact]
    public void Reset_AccionesExigenAdministradorEIdDeRutaYPostAntiforgery()
    {
        var actions = typeof(UsuariosController).GetMethods().Where(method => method.Name == nameof(UsuariosController.ResetPassword)).ToArray();
        Assert.Equal(2, actions.Length);
        foreach (var action in actions)
        {
            Assert.Equal("Administrador", Assert.Single(action.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Roles);
            Assert.Single(action.GetParameters()[0].GetCustomAttributes(typeof(FromRouteAttribute), true));
            var cache = Assert.Single(action.GetCustomAttributes(typeof(ResponseCacheAttribute), true).Cast<ResponseCacheAttribute>());
            Assert.True(cache.NoStore);
        }
        var post = actions.Single(action => action.GetParameters().Length == 2);
        Assert.Single(post.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), true));
    }

    private static ResetPasswordViewModel Model(string password = NewPassword) => new()
    {
        NewPassword = password,
        ConfirmPassword = password
    };

    private static void AssertBlankPasswords(UsuariosController controller, IActionResult result)
    {
        var model = Assert.IsType<ResetPasswordViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Empty(model.NewPassword);
        Assert.Empty(model.ConfirmPassword);
        Assert.All(controller.ModelState.Values, value =>
        {
            Assert.Null(value.RawValue);
            Assert.Null(value.AttemptedValue);
        });
    }

    private sealed class ResetContext : IAsyncDisposable
    {
        public IdentityCookieWebApplicationFactory Factory { get; } = new();
        private readonly IServiceScope scope;
        public ApplicationDbContext Database { get; }
        public ResetUserManager Users { get; }

        private ResetContext()
        {
            scope = Factory.Services.CreateScope();
            Database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Users = new ResetUserManager(scope.ServiceProvider);
        }

        public static async Task<ResetContext> CreateAsync()
        {
            var test = new ResetContext();
            var roles = test.scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            foreach (var role in new[] { "Administrador", "Odontologo", "Recepcionista" })
                Assert.True((await roles.CreateAsync(new IdentityRole(role))).Succeeded);
            return test;
        }

        public async Task<ApplicationUser> CreateUserAsync(string role, bool active = true)
        {
            var email = $"{Guid.NewGuid():N}@example.test";
            var user = new ApplicationUser
            {
                UserName = email, Email = email, Nombre = "Usuario", Apellido = "Prueba", Estado = active
            };
            Assert.True((await Users.CreateAsync(user, OriginalPassword)).Succeeded);
            Assert.True((await Users.AddToRoleAsync(user, role)).Succeeded);
            return user;
        }

        public UsuariosController Controller(string actorId = "administrador-actor")
        {
            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, actorId), new Claim(ClaimTypes.Role, "Administrador")], "test"))
            };
            return new UsuariosController(Users, Database)
            {
                ControllerContext = new ControllerContext { HttpContext = httpContext },
                TempData = new TempDataDictionary(httpContext, new MemoryTempDataProvider())
            };
        }

        public ValueTask DisposeAsync()
        {
            Users.Dispose();
            scope.Dispose();
            Factory.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ResetUserManager(IServiceProvider services) : UserManager<ApplicationUser>(
        services.GetRequiredService<IUserStore<ApplicationUser>>(),
        services.GetRequiredService<IOptions<IdentityOptions>>(),
        services.GetRequiredService<IPasswordHasher<ApplicationUser>>(),
        services.GetServices<IUserValidator<ApplicationUser>>(),
        services.GetServices<IPasswordValidator<ApplicationUser>>(),
        services.GetRequiredService<ILookupNormalizer>(),
        services.GetRequiredService<IdentityErrorDescriber>(), services,
        services.GetRequiredService<ILogger<UserManager<ApplicationUser>>>())
    {
        public Func<Task>? BeforeReset { get; set; }
        public int TokenGenerationCount { get; private set; }
        public string? LastGeneratedToken { get; private set; }

        public override async Task<string> GeneratePasswordResetTokenAsync(ApplicationUser user)
        {
            TokenGenerationCount++;
            return LastGeneratedToken = await base.GeneratePasswordResetTokenAsync(user);
        }

        public override async Task<IdentityResult> ResetPasswordAsync(ApplicationUser user, string token, string newPassword)
        {
            if (BeforeReset is not null) await BeforeReset();
            return await base.ResetPasswordAsync(user, token, newPassword);
        }
    }

    private sealed class MemoryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();
        public void SaveTempData(HttpContext context, IDictionary<string, object?> values) { }
    }
}
