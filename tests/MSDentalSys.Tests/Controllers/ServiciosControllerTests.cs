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

public class ServiciosControllerTests
{
    [Fact]
    public async Task Create_ConDatosValidos_CreaServicioActivo()
    {
        await using var database = await TestDatabase.CreateAsync();
        var controller = database.CreateController();

        var result = await controller.Create(new ServicioFormViewModel
        {
            Nombre = "Limpieza Dental",
            Descripcion = "Servicio ficticio de limpieza preventiva",
            DuracionEstimadaMinutos = 45
        });

        Assert.IsType<RedirectToActionResult>(result);
        var servicio = await database.Context.ServiciosOdontologicos.SingleAsync();
        Assert.Equal("Limpieza Dental", servicio.Nombre);
        Assert.Equal("Servicio ficticio de limpieza preventiva", servicio.Descripcion);
        Assert.Equal(45, servicio.DuracionEstimadaMinutos);
        Assert.True(servicio.Estado);
        Assert.NotEqual(default, servicio.FechaCreacion);
    }

    [Fact]
    public async Task Edit_ServicioExistente_ActualizaDatosYConservaIdentidadEstadoYFecha()
    {
        await using var database = await TestDatabase.CreateAsync();
        var originalDate = new DateTime(2030, 1, 1, 8, 30, 0);
        var servicio = database.CreateService(
            "Consulta Inicial",
            "Descripcion inicial",
            30,
            true,
            originalDate);
        database.Context.ServiciosOdontologicos.Add(servicio);
        await database.Context.SaveChangesAsync();
        var controller = database.CreateController();

        var result = await controller.Edit(servicio.ServicioOdontologicoId, new ServicioFormViewModel
        {
            ServicioOdontologicoId = servicio.ServicioOdontologicoId,
            Nombre = "Consulta Actualizada",
            Descripcion = "Descripcion actualizada",
            DuracionEstimadaMinutos = 60
        });

        Assert.IsType<RedirectToActionResult>(result);
        var stored = await database.Context.ServiciosOdontologicos.SingleAsync();
        Assert.Equal(servicio.ServicioOdontologicoId, stored.ServicioOdontologicoId);
        Assert.Equal("Consulta Actualizada", stored.Nombre);
        Assert.Equal("Descripcion actualizada", stored.Descripcion);
        Assert.Equal(60, stored.DuracionEstimadaMinutos);
        Assert.True(stored.Estado);
        Assert.Equal(originalDate, stored.FechaCreacion);
    }

    [Fact]
    public async Task Deactivate_ServicioActivo_CambiaEstadoSinEliminarlo()
    {
        await using var database = await TestDatabase.CreateAsync();
        var servicio = database.CreateService("Servicio Activo", "Descripcion", 30);
        database.Context.ServiciosOdontologicos.Add(servicio);
        await database.Context.SaveChangesAsync();
        var controller = database.CreateController();

        var result = await controller.Deactivate(servicio.ServicioOdontologicoId);

        Assert.IsType<RedirectToActionResult>(result);
        var stored = await database.Context.ServiciosOdontologicos.SingleAsync();
        Assert.False(stored.Estado);
        Assert.Equal(servicio.ServicioOdontologicoId, stored.ServicioOdontologicoId);
        Assert.Equal(1, await database.Context.ServiciosOdontologicos.CountAsync());
    }

    [Fact]
    public async Task Activate_ServicioInactivo_CambiaEstadoYConservaDatos()
    {
        await using var database = await TestDatabase.CreateAsync();
        var originalDate = new DateTime(2030, 2, 2, 10, 15, 0);
        var servicio = database.CreateService(
            "Servicio Inactivo",
            "Descripcion conservada",
            90,
            false,
            originalDate);
        database.Context.ServiciosOdontologicos.Add(servicio);
        await database.Context.SaveChangesAsync();
        var controller = database.CreateController();

        var result = await controller.Activate(servicio.ServicioOdontologicoId);

        Assert.IsType<RedirectToActionResult>(result);
        var stored = await database.Context.ServiciosOdontologicos.SingleAsync();
        Assert.True(stored.Estado);
        Assert.Equal("Servicio Inactivo", stored.Nombre);
        Assert.Equal("Descripcion conservada", stored.Descripcion);
        Assert.Equal(90, stored.DuracionEstimadaMinutos);
        Assert.Equal(originalDate, stored.FechaCreacion);
    }

    [Fact]
    public async Task Index_ConBusquedaPorNombre_DevuelveSoloServicioCoincidente()
    {
        await using var database = await TestDatabase.CreateAsync();
        database.Context.ServiciosOdontologicos.AddRange(
            database.CreateService("Limpieza Dental", "Prevencion", 45),
            database.CreateService("Extraccion Simple", "Procedimiento ficticio", 30),
            database.CreateService("Blanqueamiento", "Estetica dental", 60));
        await database.Context.SaveChangesAsync();
        var controller = database.CreateController();

        var result = await controller.Index("Extraccion");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ServiciosIndexViewModel>(view.Model);
        var servicio = Assert.Single(model.Servicios);
        Assert.Equal("Extraccion Simple", servicio.Nombre);
    }

    [Fact]
    public async Task Index_ConBusquedaPorDescripcion_DevuelveServicioCorrecto()
    {
        await using var database = await TestDatabase.CreateAsync();
        database.Context.ServiciosOdontologicos.AddRange(
            database.CreateService("Servicio Uno", "Prevencion y control", 30),
            database.CreateService("Servicio Dos", "Tratamiento restaurativo", 60),
            database.CreateService("Servicio Tres", "Evaluacion general", 45));
        await database.Context.SaveChangesAsync();
        var controller = database.CreateController();

        var result = await controller.Index("restaurativo");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ServiciosIndexViewModel>(view.Model);
        var servicio = Assert.Single(model.Servicios);
        Assert.Equal("Servicio Dos", servicio.Nombre);
    }

    [Fact]
    public async Task Index_SinBusqueda_DevuelveTodosLosServiciosSinModificarDatos()
    {
        await using var database = await TestDatabase.CreateAsync();
        database.Context.ServiciosOdontologicos.AddRange(
            database.CreateService("Servicio A", "Descripcion A", 20),
            database.CreateService("Servicio B", "Descripcion B", 40),
            database.CreateService("Servicio C", "Descripcion C", 80));
        await database.Context.SaveChangesAsync();
        var before = await database.Context.ServiciosOdontologicos
            .AsNoTracking()
            .Select(servicio => new { servicio.ServicioOdontologicoId, servicio.Nombre, servicio.Estado })
            .ToListAsync();
        var controller = database.CreateController();

        var result = await controller.Index(null);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ServiciosIndexViewModel>(view.Model);
        Assert.Equal(3, model.Servicios.Count);
        Assert.Equal(
            before.OrderBy(servicio => servicio.ServicioOdontologicoId).Select(servicio => servicio.Nombre),
            model.Servicios.OrderBy(servicio => servicio.ServicioOdontologicoId).Select(servicio => servicio.Nombre));
        var after = await database.Context.ServiciosOdontologicos
            .AsNoTracking()
            .Select(servicio => new { servicio.ServicioOdontologicoId, servicio.Nombre, servicio.Estado })
            .ToListAsync();
        Assert.Equal(before, after);
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

        public ServiciosController CreateController()
        {
            var httpContext = new DefaultHttpContext();
            var controller = new ServiciosController(Context)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = httpContext
                }
            };
            controller.TempData = new TempDataDictionary(
                httpContext,
                new NullTempDataProvider());
            return controller;
        }

        public ServicioOdontologico CreateService(
            string name,
            string description,
            int duration,
            bool state = true,
            DateTime? createdAt = null)
        {
            return new ServicioOdontologico
            {
                Nombre = name,
                Descripcion = description,
                DuracionEstimadaMinutos = duration,
                Estado = state,
                FechaCreacion = createdAt ?? new DateTime(2030, 1, 1, 8, 0, 0)
            };
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
