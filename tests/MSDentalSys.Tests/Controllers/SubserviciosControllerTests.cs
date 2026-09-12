using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MSDentalSys.Data.Context;
using MSDentalSys.Data.InitialData;
using MSDentalSys.Data.Models;
using MSDentalSys.Web.Controllers;
using MSDentalSys.Web.Models.ViewModels;
using Xunit;

namespace MSDentalSys.Tests.Controllers;

public class SubserviciosControllerTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(1440)]
    public async Task Create_Valido_NormalizaYPersiste(int minutes)
    {
        await using var db = await Database.CreateAsync();
        var model = db.Form(minutes);
        model.Nombre = "  Evaluación  "; model.Descripcion = "  Descripción  ";
        Assert.IsType<RedirectToActionResult>(await db.Controller().Create(model));
        var stored = await db.Context.SubserviciosOdontologicos.AsNoTracking().SingleAsync();
        Assert.Equal("Evaluación", stored.Nombre);
        Assert.Equal("Descripción", stored.Descripcion);
        Assert.Equal(minutes, stored.DuracionEstimadaMinutos);
        Assert.True(stored.Estado);
        Assert.NotEqual(default, stored.FechaCreacion);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1441)]
    public async Task Create_DuracionInvalida_NoPersiste(int? minutes)
    {
        await using var db = await Database.CreateAsync();
        var controller = db.Controller();
        Assert.IsType<ViewResult>(await controller.Create(db.Form(minutes)));
        Assert.False(controller.ModelState.IsValid);
        Assert.Empty(await db.Context.SubserviciosOdontologicos.ToListAsync());
    }

    [Theory]
    [InlineData(" ", null)]
    [InlineData("Largo", "larga")]
    public async Task Create_TextoInvalido_NoPersiste(string name, string? description)
    {
        await using var db = await Database.CreateAsync();
        var model = db.Form(); model.Nombre = name;
        model.Descripcion = description is null ? null : new string('a', 301);
        Assert.IsType<ViewResult>(await db.Controller().Create(model));
        Assert.Empty(await db.Context.SubserviciosOdontologicos.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_PadreInexistenteOInactivo_Rechaza(bool missing)
    {
        await using var db = await Database.CreateAsync();
        db.Parent.Estado = false; await db.Context.SaveChangesAsync();
        var model = db.Form(); if (missing) model.ServicioOdontologicoId = 999;
        Assert.IsType<ViewResult>(await db.Controller().Create(model));
        Assert.Empty(await db.Context.SubserviciosOdontologicos.ToListAsync());
    }

    [Fact]
    public async Task Duplicado_InactivoMismoPadreRechazado_OtroPadrePermitido()
    {
        await using var db = await Database.CreateAsync();
        var original = await db.AddAsync(); original.Estado = false; await db.Context.SaveChangesAsync();
        Assert.IsType<ViewResult>(await db.Controller().Create(db.Form()));
        var other = new ServicioOdontologico { Nombre = "Otro" };
        db.Context.Add(other); await db.Context.SaveChangesAsync();
        var model = db.Form(); model.ServicioOdontologicoId = other.ServicioOdontologicoId;
        Assert.IsType<RedirectToActionResult>(await db.Controller().Create(model));
        Assert.Equal(2, await db.Context.SubserviciosOdontologicos.CountAsync());
    }

    [Fact]
    public async Task Edit_ModificaCamposYConservaPadreEstadoFechaYSnapshot()
    {
        await using var db = await Database.CreateAsync();
        var item = await db.AddAsync(); item.Estado = false;
        var date = item.FechaCreacion;
        await db.AddAppointmentAsync(item, 45);
        var model = db.Form(90); model.SubservicioOdontologicoId = item.SubservicioOdontologicoId;
        model.Nombre = " Nombre nuevo "; model.Descripcion = " Descripción nueva ";
        Assert.IsType<RedirectToActionResult>(await db.Controller().Edit(item.SubservicioOdontologicoId, model));
        db.Context.ChangeTracker.Clear();
        var stored = await db.Context.SubserviciosOdontologicos.SingleAsync();
        Assert.Equal("Nombre nuevo", stored.Nombre); Assert.Equal("Descripción nueva", stored.Descripcion);
        Assert.Equal(90, stored.DuracionEstimadaMinutos);
        Assert.Equal(db.Parent.ServicioOdontologicoId, stored.ServicioOdontologicoId);
        Assert.False(stored.Estado); Assert.Equal(date, stored.FechaCreacion);
        Assert.Equal(45, (await db.Context.Citas.SingleAsync()).DuracionProgramadaMinutos);
    }

    [Fact]
    public async Task Edit_PadreManipulado_RechazaSinModificar()
    {
        await using var db = await Database.CreateAsync();
        var item = await db.AddAsync();
        var model = db.Form(90); model.SubservicioOdontologicoId = item.SubservicioOdontologicoId;
        model.ServicioOdontologicoId = 999;
        Assert.IsType<ViewResult>(await db.Controller().Edit(item.SubservicioOdontologicoId, model));
        var stored = await db.Context.SubserviciosOdontologicos.AsNoTracking().SingleAsync();
        Assert.Equal(db.Parent.ServicioOdontologicoId, stored.ServicioOdontologicoId);
        Assert.Equal(30, stored.DuracionEstimadaMinutos);
    }

    [Fact]
    public async Task Edit_DuplicadoExcluyePropioIdYRechazaOtro()
    {
        await using var db = await Database.CreateAsync();
        var item = await db.AddAsync();
        var model = db.Form(); model.SubservicioOdontologicoId = item.SubservicioOdontologicoId;
        Assert.IsType<RedirectToActionResult>(await db.Controller().Edit(item.SubservicioOdontologicoId, model));
        db.Context.Add(new SubservicioOdontologico { ServicioOdontologicoId = db.Parent.ServicioOdontologicoId, Nombre = "Otro", DuracionEstimadaMinutos = 30 });
        await db.Context.SaveChangesAsync(); model.Nombre = "Otro";
        Assert.IsType<ViewResult>(await db.Controller().Edit(item.SubservicioOdontologicoId, model));
        Assert.Equal("Evaluación", (await db.Context.SubserviciosOdontologicos.AsNoTracking().SingleAsync(s => s.SubservicioOdontologicoId == item.SubservicioOdontologicoId)).Nombre);
    }

    [Fact]
    public async Task Estados_NoEliminanNiModificanCitas_ActivacionRequierePadreActivo()
    {
        await using var db = await Database.CreateAsync();
        var item = await db.AddAsync(); await db.AddAppointmentAsync(item, 30);
        Assert.IsType<RedirectToActionResult>(await db.Controller().Deactivate(item.SubservicioOdontologicoId));
        Assert.False((await db.Context.SubserviciosOdontologicos.AsNoTracking().SingleAsync()).Estado);
        Assert.True(db.Parent.Estado);
        Assert.IsType<RedirectToActionResult>(await db.Controller().Activate(item.SubservicioOdontologicoId));
        Assert.True((await db.Context.SubserviciosOdontologicos.AsNoTracking().SingleAsync()).Estado);
        db.Parent.Estado = false; await db.Context.SaveChangesAsync();
        Assert.True((await db.Context.SubserviciosOdontologicos.AsNoTracking().SingleAsync()).Estado);
        await db.Controller().Deactivate(item.SubservicioOdontologicoId);
        var controller = db.Controller(); await controller.Activate(item.SubservicioOdontologicoId);
        Assert.NotNull(controller.TempData["ErrorMessage"]);
        Assert.False((await db.Context.SubserviciosOdontologicos.AsNoTracking().SingleAsync()).Estado);
        Assert.Single(await db.Context.Citas.ToListAsync());
        Assert.Equal(30, (await db.Context.Citas.SingleAsync()).DuracionProgramadaMinutos);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ColisionConcurrente_Nombre_DevuelveValidacion(bool edit)
    {
        var gate = new SaveGate(); await using var db = await Database.CreateAsync(gate);
        SubservicioOdontologico? item = edit ? await db.AddAsync() : null;
        var model = db.Form(); model.Nombre = "Reservado";
        if (item is not null) model.SubservicioOdontologicoId = item.SubservicioOdontologicoId;
        gate.Before = async () =>
        {
            await using var other = db.Fresh();
            other.Add(new SubservicioOdontologico { ServicioOdontologicoId = db.Parent.ServicioOdontologicoId, Nombre = "Reservado", DuracionEstimadaMinutos = 60 });
            await other.SaveChangesAsync();
        };
        var controller = db.Controller();
        Assert.IsType<ViewResult>(edit ? await controller.Edit(item!.SubservicioOdontologicoId, model) : await controller.Create(model));
        Assert.NotEmpty(controller.ModelState["Nombre"]!.Errors);
        await using var fresh = db.Fresh();
        Assert.Equal(1, await fresh.SubserviciosOdontologicos.CountAsync(s => s.Nombre == "Reservado"));
        if (edit) Assert.Equal("Evaluación", (await fresh.SubserviciosOdontologicos.SingleAsync(s => s.SubservicioOdontologicoId == item!.SubservicioOdontologicoId)).Nombre);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(1440)]
    public async Task Esquema_CitaNullableOConSubservicioValido(int? minutes)
    {
        await using var db = await Database.CreateAsync();
        var sub = minutes.HasValue ? await db.AddAsync() : null;
        await db.AddAppointmentAsync(sub, minutes);
        await using var fresh = db.Fresh();
        var cita = await fresh.Citas.SingleAsync();
        Assert.Equal(minutes, cita.DuracionProgramadaMinutos);
        Assert.Equal(sub?.SubservicioOdontologicoId, cita.SubservicioOdontologicoId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1441)]
    public async Task Esquema_SnapshotInvalido_RechazadoPorBD(int minutes)
    {
        await using var db = await Database.CreateAsync();
        await Assert.ThrowsAsync<DbUpdateException>(() => db.AddAppointmentAsync(null, minutes));
    }

    [Fact]
    public async Task Esquema_FKCompuesta_RechazaServicioAjeno()
    {
        await using var db = await Database.CreateAsync();
        var sub = await db.AddAsync(); await db.AddAppointmentAsync(sub, 30);
        var other = new ServicioOdontologico { Nombre = "Otro" }; db.Context.Add(other); await db.Context.SaveChangesAsync();
        // SQL directo evita que el fixup de navegaciones corrija el par incoherente antes de enviarlo.
        await Assert.ThrowsAsync<SqliteException>(() => db.Context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Citas SET ServicioOdontologicoId = {other.ServicioOdontologicoId}"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1441)]
    public async Task Esquema_DuracionSubservicioInvalida_RechazadaPorBD(int minutes)
    {
        await using var db = await Database.CreateAsync();
        db.Context.Add(new SubservicioOdontologico { ServicioOdontologicoId = db.Parent.ServicioOdontologicoId, Nombre = "Inválido", DuracionEstimadaMinutos = minutes });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task Seeder_45_Idempotente_ConservaEdicionesRenombresYManuales()
    {
        await using var db = await Database.CreateAsync();
        await db.AddCatalogParentsAsync();
        var first = SubservicioSeeder.Catalogo[0];
        var parent = await db.Context.ServiciosOdontologicos.SingleAsync(s => s.Nombre == first.Servicio);
        var existing = new SubservicioOdontologico { ServicioOdontologicoId = parent.ServicioOdontologicoId, Nombre = first.Nombre, Descripcion = "Manual", DuracionEstimadaMinutos = 77, Estado = false };
        db.Context.Add(existing); await db.Context.SaveChangesAsync();
        Assert.Equal(44, await SubservicioSeeder.SeedAsync(db.Context));
        Assert.Equal(45, await db.Context.SubserviciosOdontologicos.CountAsync());
        Assert.Equal(0, await SubservicioSeeder.SeedAsync(db.Context));
        Assert.Equal(77, existing.DuracionEstimadaMinutos); Assert.False(existing.Estado); Assert.Equal("Manual", existing.Descripcion);
        existing.Nombre = "Renombrado manualmente";
        db.Context.Add(new SubservicioOdontologico { ServicioOdontologicoId = parent.ServicioOdontologicoId, Nombre = "Externo", DuracionEstimadaMinutos = 20 });
        await db.Context.SaveChangesAsync();
        Assert.Equal(0, await SubservicioSeeder.SeedAsync(db.Context));
        Assert.Equal(46, await db.Context.SubserviciosOdontologicos.CountAsync());
        Assert.False(await db.Context.SubserviciosOdontologicos.AnyAsync(s => s.Nombre == first.Nombre));
    }

    [Fact]
    public async Task Seeder_CodigoExistenteRenombrado_ConservaPersonalizacion()
    {
        await using var db = await Database.CreateAsync(); await db.AddCatalogParentsAsync();
        var entry = SubservicioSeeder.Catalogo[0];
        var parent = await db.Context.ServiciosOdontologicos.SingleAsync(s => s.Nombre == entry.Servicio);
        db.Context.Add(new SubservicioOdontologico
        {
            ServicioOdontologicoId = parent.ServicioOdontologicoId,
            CodigoCatalogo = entry.Codigo,
            Nombre = "Nombre personalizado",
            Descripcion = "Descripción personalizada",
            DuracionEstimadaMinutos = 77,
            Estado = false
        });
        await db.Context.SaveChangesAsync();

        Assert.Equal(44, await SubservicioSeeder.SeedAsync(db.Context));
        var stored = await db.Context.SubserviciosOdontologicos.SingleAsync(s => s.CodigoCatalogo == entry.Codigo);
        Assert.Equal("Nombre personalizado", stored.Nombre);
        Assert.Equal("Descripción personalizada", stored.Descripcion);
        Assert.Equal(77, stored.DuracionEstimadaMinutos);
        Assert.False(stored.Estado);
    }

    [Fact]
    public async Task Seeder_CodigoExistenteConPadreIncorrecto_AbortaSinMoverNiDuplicar()
    {
        await using var db = await Database.CreateAsync(); await db.AddCatalogParentsAsync();
        var entry = SubservicioSeeder.Catalogo[0];
        var wrongParent = await db.Context.ServiciosOdontologicos.SingleAsync(s => s.Nombre == "Endodoncia");
        db.Context.Add(new SubservicioOdontologico
        {
            ServicioOdontologicoId = wrongParent.ServicioOdontologicoId,
            CodigoCatalogo = entry.Codigo,
            Nombre = "Registro inconsistente",
            DuracionEstimadaMinutos = 30
        });
        await db.Context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => SubservicioSeeder.SeedAsync(db.Context));
        Assert.Contains("Inconsistencia del catálogo", exception.Message);
        Assert.Equal(1, await db.Context.SubserviciosOdontologicos.CountAsync(s => s.CodigoCatalogo == entry.Codigo));
        Assert.Equal(wrongParent.ServicioOdontologicoId,
            (await db.Context.SubserviciosOdontologicos.SingleAsync(s => s.CodigoCatalogo == entry.Codigo)).ServicioOdontologicoId);
    }

    [Fact]
    public async Task Seeder_ColisionUniqueEsperada_ReintentaYCompletaCatalogo()
    {
        var gate = new CatalogRaceGate();
        await using var db = await Database.CreateAsync(gate); await db.AddCatalogParentsAsync();
        gate.Enabled = true;

        var added = await SubservicioSeeder.SeedAsync(db.Context);

        Assert.Equal(45, added);
        Assert.Equal(45, await db.Context.SubserviciosOdontologicos.CountAsync());
        Assert.Equal(2, gate.Calls);
    }

    [Fact]
    public async Task Seeder_DbUpdateExceptionNoRelacionada_NoSeOculta()
    {
        var gate = new UnexpectedFailureGate();
        await using var db = await Database.CreateAsync(gate); await db.AddCatalogParentsAsync();
        gate.Enabled = true;

        await Assert.ThrowsAsync<DbUpdateException>(() => SubservicioSeeder.SeedAsync(db.Context));
        Assert.Empty(await db.Context.SubserviciosOdontologicos.ToListAsync());
    }

    [Fact]
    public async Task Seeder_CatalogoCompleto_ConservaCadaNombreYDuracion()
    {
        await using var db = await Database.CreateAsync(); await db.AddCatalogParentsAsync();
        Assert.Equal(45, SubservicioSeeder.Catalogo.Count);
        Assert.Equal(45, await SubservicioSeeder.SeedAsync(db.Context));
        foreach (var entry in SubservicioSeeder.Catalogo)
        {
            var stored = await db.Context.SubserviciosOdontologicos.Include(s => s.ServicioOdontologico).AsNoTracking().SingleAsync(s => s.CodigoCatalogo == entry.Codigo);
            Assert.Equal(entry.Nombre, stored.Nombre); Assert.Equal(entry.Servicio, stored.ServicioOdontologico.Nombre);
            Assert.Equal(entry.Minutos, stored.DuracionEstimadaMinutos);
        }
    }

    [Theory]
    [InlineData("Ausente")]
    [InlineData("Ambiguo")]
    [InlineData("Inactivo")]
    public async Task Seeder_PadreNoDisponible_NoPersisteCargaParcial(string scenario)
    {
        await using var db = await Database.CreateAsync(); await db.AddCatalogParentsAsync();
        var last = SubservicioSeeder.Catalogo.Last().Servicio;
        var parent = await db.Context.ServiciosOdontologicos.SingleAsync(s => s.Nombre == last);
        if (scenario == "Ausente") db.Context.Remove(parent);
        else if (scenario == "Ambiguo") db.Context.Add(new ServicioOdontologico { Nombre = last });
        else parent.Estado = false;
        await db.Context.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => SubservicioSeeder.SeedAsync(db.Context));
        Assert.Contains(last, ex.Message);
        await using var fresh = db.Fresh(); Assert.Empty(await fresh.SubserviciosOdontologicos.ToListAsync());
    }

    private sealed class SaveGate : SaveChangesInterceptor
    {
        public Func<Task>? Before { get; set; }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Before is { } callback) { Before = null; await callback(); }
            return result;
        }
    }

    private sealed class CatalogRaceGate : SaveChangesInterceptor
    {
        public int Calls { get; private set; }
        public bool Enabled { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!Enabled)
                return ValueTask.FromResult(result);

            Calls++;
            if (Calls == 1)
            {
                ((ApplicationDbContext)eventData.Context!).SubserviciosOdontologicos.Add(new SubservicioOdontologico
                {
                    ServicioOdontologicoId = 2,
                    CodigoCatalogo = SubservicioSeeder.Catalogo[0].Codigo,
                    Nombre = "Carrera simulada",
                    DuracionEstimadaMinutos = 30
                });
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class UnexpectedFailureGate : SaveChangesInterceptor
    {
        public bool Enabled { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!Enabled)
                return ValueTask.FromResult(result);

            throw new DbUpdateException("Fallo no relacionado con una carrera del catálogo.", new InvalidOperationException("Fallo simulado."));
        }
    }

    private sealed class Database(SqliteConnection connection, ApplicationDbContext context, ServicioOdontologico parent) : IAsyncDisposable
    {
        public ApplicationDbContext Context => context;
        public ServicioOdontologico Parent => parent;
        public static async Task<Database> CreateAsync(params IInterceptor[] interceptors)
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).AddInterceptors(interceptors).Options);
            await context.Database.EnsureCreatedAsync();
            var parent = new ServicioOdontologico { Nombre = "Padre" }; context.Add(parent); await context.SaveChangesAsync();
            return new Database(connection, context, parent);
        }
        public ApplicationDbContext Fresh() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        public SubserviciosController Controller()
        {
            var http = new DefaultHttpContext();
            return new SubserviciosController(context) { ControllerContext = new ControllerContext { HttpContext = http }, TempData = new TempDataDictionary(http, new TempProvider()) };
        }
        public SubservicioFormViewModel Form(int? minutes = 30) => new() { ServicioOdontologicoId = parent.ServicioOdontologicoId, Nombre = "Evaluación", DuracionEstimadaMinutos = minutes };
        public async Task<SubservicioOdontologico> AddAsync()
        {
            var item = new SubservicioOdontologico { ServicioOdontologicoId = parent.ServicioOdontologicoId, Nombre = "Evaluación", DuracionEstimadaMinutos = 30 };
            context.Add(item); await context.SaveChangesAsync(); return item;
        }
        public async Task AddCatalogParentsAsync()
        {
            context.AddRange(SubservicioSeeder.Catalogo.Select(e => e.Servicio).Distinct().Select(n => new ServicioOdontologico { Nombre = n }));
            await context.SaveChangesAsync();
        }
        public async Task AddAppointmentAsync(SubservicioOdontologico? sub, int? minutes)
        {
            context.Citas.Add(new Cita { Paciente = new Paciente { Nombre = "Paciente", Apellido = "Prueba" },
                Odontologo = new ApplicationUser { UserName = "prueba@example.test", Nombre = "Doctor", Apellido = "Prueba" },
                ServicioOdontologicoId = parent.ServicioOdontologicoId, SubservicioOdontologicoId = sub?.SubservicioOdontologicoId,
                DuracionProgramadaMinutos = minutes, FechaHoraInicio = new DateTime(2030, 1, 1, 9, 0, 0) });
            await context.SaveChangesAsync();
        }
        public async ValueTask DisposeAsync() { await context.DisposeAsync(); await connection.DisposeAsync(); }
    }
    private sealed class TempProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
