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

public class SegurosControllerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ColisionEnPersistencia_DevuelveNombreYDescartaCambioPendiente(bool edit)
    {
        var gate = new BeforeSave();
        await using var db = await TestDatabase.CreateAsync(gate);
        var original = db.CreateSeguro("Original");
        db.Context.Add(original);
        await db.Context.SaveChangesAsync();
        // Insert the winner after AnyAsync, before the losing SaveChanges transaction.
        gate.Callback = async () => { await db.Context.Database.ExecuteSqlRawAsync(
            "INSERT INTO Seguros (Nombre, Estado, FechaCreacion) VALUES ('Colision', 1, '2030-01-01')"); };
        var controller = db.CreateController();
        var model = new SeguroFormViewModel { SeguroId = original.SeguroId, Nombre = " Colision " };
        var result = edit ? await controller.Edit(original.SeguroId, model) : await controller.Create(model);
        Assert.True(gate.Invoked);
        Assert.Same(model, Assert.IsType<ViewResult>(result).Model);
        Assert.Equal("Ya existe un seguro con ese nombre.", Assert.Single(controller.ModelState["Nombre"]!.Errors).ErrorMessage);
        Assert.False(controller.TempData.ContainsKey("SuccessMessage"));
        Assert.DoesNotContain(db.Context.ChangeTracker.Entries(), e => e.State is EntityState.Added or EntityState.Modified);
        db.Context.ChangeTracker.Clear();
        Assert.Equal(2, await db.Context.Seguros.CountAsync());
        Assert.Equal("Original", (await db.Context.Seguros.FindAsync(original.SeguroId))!.Nombre);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ErrorAjeno_NoSeConvierteEnDuplicado(bool edit)
    {
        var gate = new BeforeSave();
        await using var db = await TestDatabase.CreateAsync(gate);
        var original = db.CreateSeguro("Original");
        db.Context.Add(original); await db.Context.SaveChangesAsync();
        var error = new DbUpdateException("Unrelated", new SqliteException("FOREIGN KEY constraint failed", 19, 787));
        gate.Callback = () => throw error;
        var controller = db.CreateController();
        var model = new SeguroFormViewModel { SeguroId = original.SeguroId, Nombre = "Nuevo" };
        var actual = await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            if (edit) await controller.Edit(original.SeguroId, model);
            else await controller.Create(model);
        });
        Assert.Same(error, actual);
        Assert.True(controller.ModelState.IsValid);
    }

    [Theory]
    [InlineData("Original", true)]
    [InlineData("Otro", false)]
    public async Task Edit_UnicidadExcluyePropioId(string name, bool success)
    {
        await using var db = await TestDatabase.CreateAsync();
        var original = db.CreateSeguro("Original");
        db.Context.AddRange(original, db.CreateSeguro("Otro")); await db.Context.SaveChangesAsync();
        var controller = db.CreateController();
        var result = await controller.Edit(original.SeguroId, new SeguroFormViewModel { SeguroId = original.SeguroId, Nombre = name });
        if (success) Assert.IsType<RedirectToActionResult>(result);
        else Assert.Contains("Ya existe", Assert.Single(controller.ModelState["Nombre"]!.Errors).ErrorMessage);
        db.Context.ChangeTracker.Clear();
        Assert.Equal("Original", (await db.Context.Seguros.FindAsync(original.SeguroId))!.Nombre);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Estado_ConPacientes_ConservaRelacionYMensajeYAdmiteRepeticion(bool active)
    {
        await using var db = await TestDatabase.CreateAsync();
        var seguro = db.CreateSeguro("Historico", !active);
        var paciente = new Paciente { Nombre = "Paciente", Apellido = "Prueba", Seguro = seguro };
        db.Context.Add(paciente); await db.Context.SaveChangesAsync();
        var controller = db.CreateController();
        for (var i = 0; i < 2; i++)
        {
            var result = active ? await controller.Activate(seguro.SeguroId) : await controller.Deactivate(seguro.SeguroId);
            Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
            Assert.Contains(active ? "activado correctamente" : "desactivado correctamente", controller.TempData["SuccessMessage"]!.ToString());
        }
        Assert.Equal(active, (await db.Context.Seguros.SingleAsync()).Estado);
        Assert.Equal(seguro.SeguroId, (await db.Context.Pacientes.SingleAsync()).SeguroId);
        Assert.IsType<NotFoundResult>(await controller.Activate(-1));
        Assert.IsType<NotFoundResult>(await controller.Deactivate(-1));
    }

    private sealed class BeforeSave : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public Func<Task>? Callback { get; set; }
        public bool Invoked { get; private set; }
        public override async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Callback is { } callback) { Callback = null; Invoked = true; await callback(); }
            return result;
        }
    }
    [Fact]
    public async Task Index_Administrador_PuedeListar()
    {
        await using var database = await TestDatabase.CreateAsync();
        database.Context.Seguros.Add(database.CreateSeguro("Seguro Uno"));
        await database.Context.SaveChangesAsync();
        var controller = database.CreateController();

        var result = await controller.Index();

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsAssignableFrom<IReadOnlyList<Seguro>>(view.Model);
        Assert.Single(model);
        Assert.Equal("Seguro Uno", model[0].Nombre);
    }

    [Fact]
    public async Task Create_Administrador_CreaSeguroActivo()
    {
        await using var database = await TestDatabase.CreateAsync();
        var controller = database.CreateController();

        var result = await controller.Create(new SeguroFormViewModel { Nombre = "Seguro Uno" });

        Assert.IsType<RedirectToActionResult>(result);
        var seguro = await database.Context.Seguros.SingleAsync();
        Assert.Equal("Seguro Uno", seguro.Nombre);
        Assert.True(seguro.Estado);
        Assert.NotEqual(default, seguro.FechaCreacion);
    }

    [Fact]
    public async Task Create_NombreObligatorio_NoCreaSeguro()
    {
        await using var database = await TestDatabase.CreateAsync();
        var controller = database.CreateController();

        var result = await controller.Create(new SeguroFormViewModel { Nombre = "   " });

        Assert.IsType<ViewResult>(result);
        Assert.Empty(await database.Context.Seguros.ToListAsync());
        Assert.False(controller.ModelState.IsValid);
    }

    [Fact]
    public async Task Create_NombreDuplicado_EsRechazado()
    {
        await using var database = await TestDatabase.CreateAsync();
        database.Context.Seguros.Add(database.CreateSeguro("Seguro Uno"));
        await database.Context.SaveChangesAsync();
        var controller = database.CreateController();

        var result = await controller.Create(new SeguroFormViewModel { Nombre = "Seguro Uno" });

        Assert.IsType<ViewResult>(result);
        Assert.Single(await database.Context.Seguros.ToListAsync());
        Assert.Contains("Ya existe", controller.ModelState[nameof(SeguroFormViewModel.Nombre)]!.Errors[0].ErrorMessage);
    }

    [Fact]
    public async Task Edit_Administrador_ActualizaNombreYConservaEstadoYFecha()
    {
        await using var database = await TestDatabase.CreateAsync();
        var originalDate = new DateTime(2030, 1, 1, 8, 30, 0);
        var seguro = database.CreateSeguro("Seguro Inicial", false, originalDate);
        database.Context.Seguros.Add(seguro);
        await database.Context.SaveChangesAsync();
        var controller = database.CreateController();

        var result = await controller.Edit(seguro.SeguroId, new SeguroFormViewModel
        {
            SeguroId = seguro.SeguroId,
            Nombre = "Seguro Actualizado"
        });

        Assert.IsType<RedirectToActionResult>(result);
        var stored = await database.Context.Seguros.SingleAsync();
        Assert.Equal("Seguro Actualizado", stored.Nombre);
        Assert.False(stored.Estado);
        Assert.Equal(originalDate, stored.FechaCreacion);
    }

    [Fact]
    public async Task Deactivate_ConservaRegistroYLoMarcaInactivo()
    {
        await using var database = await TestDatabase.CreateAsync();
        var seguro = database.CreateSeguro("Seguro Activo");
        database.Context.Seguros.Add(seguro);
        await database.Context.SaveChangesAsync();
        var controller = database.CreateController();

        var result = await controller.Deactivate(seguro.SeguroId);

        Assert.IsType<RedirectToActionResult>(result);
        var stored = await database.Context.Seguros.SingleAsync();
        Assert.False(stored.Estado);
        Assert.Equal(seguro.SeguroId, stored.SeguroId);
        Assert.Equal(1, await database.Context.Seguros.CountAsync());
    }

    [Fact]
    public async Task Activate_ConservaRegistroYLoMarcaActivo()
    {
        await using var database = await TestDatabase.CreateAsync();
        var seguro = database.CreateSeguro("Seguro Inactivo", false);
        database.Context.Seguros.Add(seguro);
        await database.Context.SaveChangesAsync();
        var controller = database.CreateController();

        var result = await controller.Activate(seguro.SeguroId);

        Assert.IsType<RedirectToActionResult>(result);
        var stored = await database.Context.Seguros.SingleAsync();
        Assert.True(stored.Estado);
        Assert.Equal("Seguro Inactivo", stored.Nombre);
        Assert.Equal(1, await database.Context.Seguros.CountAsync());
    }

    [Fact]
    public async Task Details_Administrador_PuedeConsultarSeguro()
    {
        await using var database = await TestDatabase.CreateAsync();
        var seguro = database.CreateSeguro("Seguro Uno");
        database.Context.Seguros.Add(seguro);
        await database.Context.SaveChangesAsync();
        var controller = database.CreateController();

        var result = await controller.Details(seguro.SeguroId);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal(seguro.SeguroId, Assert.IsType<Seguro>(view.Model).SeguroId);
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

        public static async Task<TestDatabase> CreateAsync(params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(interceptors)
                .Options;
            var context = new ApplicationDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new TestDatabase(connection, context);
        }

        public SegurosController CreateController()
        {
            var httpContext = new DefaultHttpContext();
            var controller = new SegurosController(Context)
            {
                ControllerContext = new ControllerContext { HttpContext = httpContext },
                TempData = new TempDataDictionary(httpContext, new NullTempDataProvider())
            };
            return controller;
        }

        public Seguro CreateSeguro(string name, bool state = true, DateTime? createdAt = null)
        {
            return new Seguro
            {
                Nombre = name,
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
