using System.Data.Common;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MSDentalSys.Data.Context;
using MSDentalSys.Data.Models;
using MSDentalSys.Web.Controllers;
using MSDentalSys.Web.Models.ViewModels;
using Xunit;

namespace MSDentalSys.Tests.Controllers;

public class EstadoConcurrencyTests
{
    [Theory]
    [InlineData("Cancelada", "Confirmada", false)]
    [InlineData("Confirmada", "Cancelada", false)]
    [InlineData("Cancelada", "No asistió", true)]
    [InlineData("Atendida", "No asistió", true)]
    [InlineData("Confirmada", "Confirmada", false)]
    public async Task Citas_LecturasSolapadas_NoSobrescribeYMuestraConflicto(string winner, string loser, bool updateStatus)
    {
        await using var db = await Database.CreateAsync();
        await using var a = db.Context();
        var original = await a.Citas.SingleAsync();
        var gate = new BeforeUpdate(async () =>
        {
            Assert.Equal("Pendiente", original.EstadoCita);
            original.EstadoCita = winner;
            await a.SaveChangesAsync();
        });
        await using var b = db.Context(gate);
        var controller = Setup(new CitasController(b, null!));
        var result = updateStatus
            ? await controller.UpdateStatus(db.CitaId, new ActualizarEstadoCitaViewModel { CitaId = db.CitaId, EstadoCita = loser })
            : loser == "Confirmada" ? await controller.Confirm(db.CitaId) : await controller.Cancel(db.CitaId);

        Assert.IsType<RedirectToActionResult>(result);
        AssertConflict(controller, gate);
        await using var c = db.Context();
        Assert.Equal(winner, (await c.Citas.SingleAsync()).EstadoCita);
        Assert.Empty(b.ChangeTracker.Entries<Cita>());
    }

    [Theory]
    [InlineData("Cancelada")]
    [InlineData("Atendida")]
    [InlineData("Reagendada")]
    public async Task Reagendar_LecturasSolapadas_ConservaCambioGanador(string winner)
    {
        await using var db = await Database.CreateAsync();
        await using var a = db.Context();
        var original = await a.Citas.SingleAsync();
        var initialDate = original.FechaHoraInicio;
        var gate = new BeforeUpdate(async () =>
        {
            if (winner == "Reagendada") original.FechaHoraInicio = initialDate.AddDays(1);
            else original.EstadoCita = winner;
            await a.SaveChangesAsync();
        });
        await using var b = db.Context(gate);
        var controller = Setup(new CitasController(b, null!));
        await controller.Reschedule(db.CitaId, new ReagendarCitaViewModel
        {
            CitaId = db.CitaId, FechaHoraInicio = initialDate.AddDays(2)
        });

        AssertConflict(controller, gate);
        await using var c = db.Context();
        var stored = await c.Citas.SingleAsync();
        Assert.Equal(winner == "Reagendada" ? "Pendiente" : winner, stored.EstadoCita);
        Assert.Equal(winner == "Reagendada" ? initialDate.AddDays(1) : initialDate, stored.FechaHoraInicio);
    }

    [Theory]
    [InlineData("Completado", "En progreso")]
    [InlineData("En progreso", "Completado")]
    [InlineData("En progreso", "En progreso")]
    [InlineData("Completado", "Completado")]
    public async Task Tratamientos_LecturasSolapadas_NoSobrescribeYMuestraConflicto(string winner, string loser)
    {
        await using var db = await Database.CreateAsync();
        await using var a = db.Context();
        var original = await a.Tratamientos.SingleAsync();
        var gate = new BeforeUpdate(async () =>
        {
            Assert.Equal("Planificado", original.EstadoTratamiento);
            original.EstadoTratamiento = winner;
            await a.SaveChangesAsync();
        });
        await using var b = db.Context(gate);
        var controller = Setup(new TratamientosController(b));
        await controller.UpdateStatus(original.TratamientoId, loser);

        AssertConflict(controller, gate);
        await using var c = db.Context();
        Assert.Equal(winner, (await c.Tratamientos.SingleAsync()).EstadoTratamiento);
        Assert.Empty(b.ChangeTracker.Entries<Tratamiento>());
    }

    // This tests the MVC zero-row branch, not cross-connection transaction isolation.
    // SQLite serializes writers; a cancellation after the transaction's read cannot
    // reproduce SQL Server's READ COMMITTED interleaving reliably.
    [Fact]
    public async Task Atencion_CeroFilas_DevuelveErrorSinInsertar()
    {
        await using var db = await Database.CreateAsync();
        var gate = new BeforeUpdate(() => Task.CompletedTask, suppress: true);
        await using var b = db.Context(gate);
        var controller = Setup(new AtencionesController(b));
        var result = await controller.Create(new AtencionOdontologicaCreateViewModel
        {
            CitaId = db.CitaId, MotivoConsulta = "Control"
        });

        Assert.IsType<ViewResult>(result);
        Assert.True(gate.Invoked);
        Assert.Contains(controller.ModelState.Values.SelectMany(v => v.Errors), e => e.ErrorMessage.Contains("Otra operación"));
        Assert.False(controller.TempData.ContainsKey("SuccessMessage"));
        await using var c = db.Context();
        Assert.Equal("Pendiente", (await c.Citas.SingleAsync()).EstadoCita);
        Assert.False(await c.AtencionesOdontologicas.AnyAsync(x => x.CitaId == db.CitaId));
    }

    [Theory]
    [InlineData("Pendiente")]
    [InlineData("Confirmada")]
    public async Task Atencion_InsercionFalla_RevierteActualizacionReal(string initialStatus)
    {
        await using var db = await Database.CreateAsync();
        await using var b = db.Context();
        await b.Citas.ExecuteUpdateAsync(s => s.SetProperty(x => x.EstadoCita, initialStatus));
        // Fail only after the real conditional UPDATE has changed the appointment.
        await b.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER FailAttention BEFORE INSERT ON AtencionesOdontologicas
            WHEN (SELECT EstadoCita FROM Citas WHERE CitaId = NEW.CitaId) = 'Atendida'
            BEGIN SELECT RAISE(ABORT, 'Forced insertion failure after appointment update'); END;
            """);
        var controller = Setup(new AtencionesController(b));
        var result = await controller.Create(new AtencionOdontologicaCreateViewModel
        {
            CitaId = db.CitaId, MotivoConsulta = "Control"
        });

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.False(controller.TempData.ContainsKey("SuccessMessage"));
        Assert.Empty(b.ChangeTracker.Entries<Cita>());
        await using var c = db.Context();
        Assert.Equal(initialStatus, (await c.Citas.SingleAsync()).EstadoCita);
        Assert.False(await c.AtencionesOdontologicas.AnyAsync(x => x.CitaId == db.CitaId));
    }

    private static void AssertConflict(Controller controller, BeforeUpdate gate)
    {
        Assert.True(gate.Invoked); // B reached its UPDATE after reading, then A committed.
        Assert.Contains("Otra operación", controller.TempData["ErrorMessage"]?.ToString());
        Assert.False(controller.TempData.ContainsKey("SuccessMessage"));
    }

    private static T Setup<T>(T controller) where T : Controller
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Administrador") }, "Test"))
        };
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        controller.TempData = new TempDataDictionary(http, new TempDataProvider());
        return controller;
    }

    private sealed class TempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class BeforeUpdate(Func<Task> winner, bool suppress = false) : DbCommandInterceptor
    {
        public bool Invoked { get; private set; }
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!Invoked && command.CommandText.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                Invoked = true;
                await winner();
                if (suppress) return InterceptionResult<int>.SuppressWithResult(0);
            }
            return result;
        }
    }

    private sealed class Database(SqliteConnection connection) : IAsyncDisposable
    {
        public int CitaId { get; private set; }
        public ApplicationDbContext Context(params IInterceptor[] interceptors) => new(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).AddInterceptors(interceptors).Options);

        public static async Task<Database> CreateAsync()
        {
            // Independent contexts, shared open connection, deterministic ordered commands.
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new Database(connection);
            await using var context = db.Context();
            await context.Database.EnsureCreatedAsync();
            var patient = new Paciente { Nombre = "Paciente", Apellido = "Prueba" };
            var dentist = new ApplicationUser { UserName = "test", Nombre = "Doctor", Apellido = "Prueba" };
            var service = new ServicioOdontologico { Nombre = "Control" };
            context.AddRange(patient, dentist, service);
            await context.SaveChangesAsync();
            var cita = new Cita
            {
                PacienteId = patient.PacienteId, OdontologoId = dentist.Id,
                ServicioOdontologicoId = service.ServicioOdontologicoId,
                FechaHoraInicio = new DateTime(2030, 1, 1, 9, 0, 0)
            };
            // Separate attention without appointment, solely to seed a valid treatment.
            var attention = new AtencionOdontologica { PacienteId = patient.PacienteId, OdontologoId = dentist.Id };
            context.AddRange(cita, attention);
            await context.SaveChangesAsync();
            context.Tratamientos.Add(new Tratamiento
            {
                AtencionOdontologicaId = attention.AtencionOdontologicaId,
                ServicioOdontologicoId = service.ServicioOdontologicoId
            });
            await context.SaveChangesAsync();
            db.CitaId = cita.CitaId;
            return db;
        }

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }
}
