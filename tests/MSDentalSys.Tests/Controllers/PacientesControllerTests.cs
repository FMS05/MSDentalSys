using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MSDentalSys.Data.Context;
using MSDentalSys.Data.Models;
using MSDentalSys.Web.Controllers;
using MSDentalSys.Web.Models.ViewModels;
using Xunit;

namespace MSDentalSys.Tests.Controllers;

public class PacientesControllerTests
{
    [Fact]
    public async Task Create_ConDatosValidos_CreaPacienteYAntecedenteActivo()
    {
        await using var database = await TestDatabase.CreateAsync();
        var controller = database.CreateController();

        var model = new PacienteFormViewModel
        {
            Nombre = "Test",
            Apellido = "Paciente",
            Cedula = "001-0000001-1",
            FechaNacimiento = new DateTime(1990, 1, 2),
            Sexo = "F",
            Telefono = "809-555-0101",
            Correo = "test.paciente@example.test",
            Alergias = "Ninguna",
            Observaciones = "Registro generado por prueba automatizada"
        };

        var result = await controller.Create(model);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(PacientesController.Index), redirect.ActionName);

        var paciente = await database.Context.Pacientes
            .Include(p => p.AntecedenteClinico)
            .SingleAsync();

        Assert.Equal("Test", paciente.Nombre);
        Assert.Equal("Paciente", paciente.Apellido);
        Assert.Equal("001-0000001-1", paciente.Cedula);
        Assert.Equal(new DateTime(1990, 1, 2), paciente.FechaNacimiento);
        Assert.Equal("F", paciente.Sexo);
        Assert.Equal("809-555-0101", paciente.Telefono);
        Assert.Equal("test.paciente@example.test", paciente.Correo);
        Assert.True(paciente.Estado);
        Assert.NotNull(paciente.AntecedenteClinico);
        Assert.Equal("Ninguna", paciente.AntecedenteClinico!.Alergias);
        Assert.Equal("Registro generado por prueba automatizada", paciente.AntecedenteClinico.Observaciones);
    }

    [Fact]
    public async Task Create_ConCedulaDuplicada_NoCreaSegundoPacienteYAgregaError()
    {
        await using var database = await TestDatabase.CreateAsync();
        database.Context.Pacientes.Add(new Paciente
        {
            Nombre = "Paciente Existente",
            Apellido = "Prueba",
            Cedula = "001-0000001-1",
            Estado = true
        });
        await database.Context.SaveChangesAsync();

        var controller = database.CreateController();
        var model = new PacienteFormViewModel
        {
            Nombre = "Segundo",
            Apellido = "Paciente",
            Cedula = "001-0000001-1"
        };

        var result = await controller.Create(model);

        Assert.IsType<ViewResult>(result);
        Assert.Single(controller.ModelState[nameof(PacienteFormViewModel.Cedula)]!.Errors);
        Assert.Contains("cédula", controller.ModelState[nameof(PacienteFormViewModel.Cedula)]!.Errors[0].ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await database.Context.Pacientes.CountAsync());
    }

    [Fact]
    public async Task Create_SinCedula_PermiteRegistrarDosPacientes()
    {
        await using var database = await TestDatabase.CreateAsync();
        var controller = database.CreateController();

        var firstResult = await controller.Create(new PacienteFormViewModel
        {
            Nombre = "Primero",
            Apellido = "Sin Cedula"
        });

        controller = database.CreateController();
        var secondResult = await controller.Create(new PacienteFormViewModel
        {
            Nombre = "Segundo",
            Apellido = "Sin Cedula"
        });

        Assert.IsType<RedirectToActionResult>(firstResult);
        Assert.IsType<RedirectToActionResult>(secondResult);
        Assert.Equal(2, await database.Context.Pacientes.CountAsync());
        Assert.All(await database.Context.Pacientes.ToListAsync(), paciente => Assert.Null(paciente.Cedula));
    }

    [Fact]
    public async Task Deactivate_PacienteActivo_CambiaEstadoSinEliminarlo()
    {
        await using var database = await TestDatabase.CreateAsync();
        var paciente = new Paciente
        {
            Nombre = "Activo",
            Apellido = "Para Desactivar",
            Cedula = "001-0000002-2",
            Estado = true
        };
        database.Context.Pacientes.Add(paciente);
        await database.Context.SaveChangesAsync();

        var controller = database.CreateController();
        var result = await controller.Deactivate(paciente.PacienteId);

        Assert.IsType<RedirectToActionResult>(result);
        var stored = await database.Context.Pacientes.SingleAsync(p => p.PacienteId == paciente.PacienteId);
        Assert.False(stored.Estado);
        Assert.Equal(1, await database.Context.Pacientes.CountAsync());
    }

    [Fact]
    public async Task Activate_PacienteInactivo_CambiaEstadoYConservaDatos()
    {
        await using var database = await TestDatabase.CreateAsync();
        var paciente = new Paciente
        {
            Nombre = "Inactivo",
            Apellido = "Para Activar",
            Cedula = "001-0000003-3",
            Telefono = "809-555-0103",
            Estado = false
        };
        database.Context.Pacientes.Add(paciente);
        await database.Context.SaveChangesAsync();

        var controller = database.CreateController();
        var result = await controller.Activate(paciente.PacienteId);

        Assert.IsType<RedirectToActionResult>(result);
        var stored = await database.Context.Pacientes.SingleAsync(p => p.PacienteId == paciente.PacienteId);
        Assert.True(stored.Estado);
        Assert.Equal("Inactivo", stored.Nombre);
        Assert.Equal("Para Activar", stored.Apellido);
        Assert.Equal("001-0000003-3", stored.Cedula);
        Assert.Equal("809-555-0103", stored.Telefono);
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private TestDatabase(SqliteConnection connection, ApplicationDbContext context)
        {
            _connection = connection;
            Context = context;
        }

        public ApplicationDbContext Context { get; }

        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new ApplicationDbContext(options);
            await context.Database.EnsureCreatedAsync();

            return new TestDatabase(connection, context);
        }

        public PacientesController CreateController()
        {
            var httpContext = new DefaultHttpContext();
            var controller = new PacientesController(Context)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = httpContext
                }
            };
            controller.TempData = new TempDataDictionary(httpContext, new NullTempDataProvider());
            return controller;
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();

        public void SaveTempData(HttpContext context, IDictionary<string, object?> values)
        {
        }
    }
}
