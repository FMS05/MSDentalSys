using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MSDentalSys.Data.Context;
using MSDentalSys.Data.Models;
using MSDentalSys.Web.Controllers;
using MSDentalSys.Web.Models.ViewModels;
using Xunit;

namespace MSDentalSys.Tests.Controllers;

public class AccountPasswordConcurrencyTests
{
    private const string OriginalPassword = "Original123!";
    private const string RequestedPassword = "Solicitada456!";
    private const string WinningPassword = "Concurrente789!";

    [Theory]
    [InlineData("deactivate")]
    [InlineData("delete")]
    [InlineData("password")]
    public async Task ChangePassword_ConflictoIdentity_NoSobrescribeGanador_YCierraSesionSiNoEsValida(string mutation)
    {
        await using var fixture = await Fixture.CreateAsync();
        var controller = fixture.Controller;
        fixture.Validator.BeforeValidation = async () =>
        {
            // Otra instancia Identity confirma después de leer el usuario del cambio propio,
            // pero antes de su escritura. No se modifican hashes ni stamps manualmente.
            using var concurrentScope = fixture.Services.CreateScope();
            var users = concurrentScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var concurrent = (await users.FindByIdAsync(fixture.UserId))!;
            IdentityResult result;
            if (mutation == "deactivate")
            {
                concurrent.Estado = false;
                result = await users.UpdateAsync(concurrent);
            }
            else if (mutation == "delete")
                result = await users.DeleteAsync(concurrent);
            else
                result = await users.ChangePasswordAsync(concurrent, OriginalPassword, WinningPassword);
            Assert.True(result.Succeeded);
        };

        var response = await controller.ChangePassword(Model());

        using var inspection = fixture.Services.CreateScope();
        var manager = inspection.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var current = await manager.FindByIdAsync(fixture.UserId);
        if (mutation == "password")
        {
            var model = Assert.IsType<ChangePasswordViewModel>(Assert.IsType<ViewResult>(response).Model);
            Assert.Empty(model.CurrentPassword);
            Assert.Empty(model.NewPassword);
            Assert.Empty(model.ConfirmPassword);
            Assert.Contains("cuenta cambió", Errors(controller));
            Assert.True(await manager.CheckPasswordAsync(current!, WinningPassword));
            Assert.False(await manager.CheckPasswordAsync(current!, RequestedPassword));
            Assert.Equal(0, fixture.HttpContext.Response.Headers.SetCookie.Count);
        }
        else
        {
            Assert.Equal("Login", Assert.IsType<RedirectToActionResult>(response).ActionName);
            Assert.Contains(fixture.HttpContext.Response.Headers.SetCookie, cookie =>
                cookie!.StartsWith(".AspNetCore.Identity.Application=", StringComparison.Ordinal));
            if (mutation == "delete") Assert.Null(current);
            else
            {
                Assert.False(current!.Estado);
                Assert.True(await manager.CheckPasswordAsync(current, OriginalPassword));
                Assert.False(await manager.CheckPasswordAsync(current, RequestedPassword));
            }
        }
        Assert.Empty(controller.TempData);
    }

    [Fact]
    public async Task ChangePassword_ErrorDeProveedor_NoReflejaDescriptionNiConservaValoresDelFormulario()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Validator.Failure = IdentityResult.Failed(new IdentityError
        {
            Code = "CustomValidator", Description = RequestedPassword
        });
        fixture.Controller.ModelState.SetModelValue("CurrentPassword", OriginalPassword, OriginalPassword);
        fixture.Controller.ModelState.SetModelValue("NewPassword", RequestedPassword, RequestedPassword);
        fixture.Controller.ModelState.SetModelValue("model.ConfirmPassword", RequestedPassword, RequestedPassword);
        foreach (var entry in fixture.Controller.ModelState.Values)
            entry.ValidationState = ModelValidationState.Valid;

        var response = await fixture.Controller.ChangePassword(Model());

        var model = Assert.IsType<ChangePasswordViewModel>(Assert.IsType<ViewResult>(response).Model);
        Assert.Empty(model.CurrentPassword);
        Assert.Empty(model.NewPassword);
        Assert.Empty(model.ConfirmPassword);
        Assert.Contains("No se pudo actualizar", Errors(fixture.Controller));
        Assert.DoesNotContain(RequestedPassword, Errors(fixture.Controller));
        Assert.All(fixture.Controller.ModelState.Values, entry =>
        {
            Assert.Null(entry.RawValue);
            Assert.Null(entry.AttemptedValue);
        });
        using var inspection = fixture.Services.CreateScope();
        var manager = inspection.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True(await manager.CheckPasswordAsync((await manager.FindByIdAsync(fixture.UserId))!, OriginalPassword));
        Assert.Empty(fixture.Controller.TempData);
    }

    private static ChangePasswordViewModel Model() => new()
    {
        CurrentPassword = OriginalPassword, NewPassword = RequestedPassword, ConfirmPassword = RequestedPassword
    };

    private static string Errors(Controller controller) => string.Join(" ", controller.ModelState.Values
        .SelectMany(value => value.Errors).Select(error => error.ErrorMessage));

    private sealed class HookValidator : IPasswordValidator<ApplicationUser>
    {
        public Func<Task>? BeforeValidation { get; set; }
        public IdentityResult? Failure { get; set; }
        public async Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user, string? password)
        {
            var hook = BeforeValidation;
            BeforeValidation = null;
            if (hook is not null) await hook();
            return Failure ?? IdentityResult.Success;
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly IServiceScope scope;
        public ServiceProvider Services { get; }
        public HookValidator Validator { get; }
        public string UserId { get; }
        public AccountController Controller { get; }
        public DefaultHttpContext HttpContext { get; }

        private Fixture(SqliteConnection connection, ServiceProvider services, IServiceScope scope,
            HookValidator validator, string userId, AccountController controller, DefaultHttpContext httpContext)
        {
            this.connection = connection;
            this.scope = scope;
            Services = services;
            Validator = validator;
            UserId = userId;
            Controller = controller;
            HttpContext = httpContext;
        }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var validator = new HookValidator();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddHttpContextAccessor();
            services.AddMvcCore();
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));
            services.AddAuthentication(IdentityConstants.ApplicationScheme)
                .AddCookie(IdentityConstants.ApplicationScheme);
            services.AddIdentityCore<ApplicationUser>(options => options.Password.RequiredLength = 8)
                .AddEntityFrameworkStores<ApplicationDbContext>().AddSignInManager();
            services.AddSingleton<IPasswordValidator<ApplicationUser>>(validator);
            var provider = services.BuildServiceProvider();
            using (var setup = provider.CreateScope())
            {
                await setup.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync();
            }
            string id;
            using (var seed = provider.CreateScope())
            {
                var users = seed.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var user = new ApplicationUser
                {
                    Email = "race@example.test", UserName = "race@example.test",
                    Nombre = "Prueba", Apellido = "Concurrencia", Estado = true
                };
                Assert.True((await users.CreateAsync(user, OriginalPassword)).Succeeded);
                id = user.Id;
            }
            var scope = provider.CreateScope();
            var httpContext = new DefaultHttpContext
            {
                RequestServices = scope.ServiceProvider,
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id)], "Test"))
            };
            provider.GetRequiredService<IHttpContextAccessor>().HttpContext = httpContext;
            var signInManager = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
            signInManager.Context = httpContext;
            var controller = new AccountController(signInManager,
                scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>())
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = httpContext, RouteData = new RouteData(),
                    ActionDescriptor = new ControllerActionDescriptor()
                },
                TempData = new TempDataDictionary(httpContext, new NullTempDataProvider())
            };
            return new Fixture(connection, provider, scope, validator, id, controller, httpContext);
        }

        public async ValueTask DisposeAsync()
        {
            scope.Dispose();
            await Services.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();
        public void SaveTempData(HttpContext context, IDictionary<string, object?> values) { }
    }
}
