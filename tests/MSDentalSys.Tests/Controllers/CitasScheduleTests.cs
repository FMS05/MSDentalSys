using System.Data.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MSDentalSys.Data.Models;
using MSDentalSys.Web.Models.ViewModels;
using Xunit;

namespace MSDentalSys.Tests.Controllers;

public partial class CitasControllerTests
{
    private static readonly DateTime ScheduleStart = new(2030, 2, 1, 10, 0, 0);

    [Theory]
    [InlineData("Pendiente")]
    [InlineData("Confirmada")]
    [InlineData("No asistió")]
    [InlineData("Atendida")]
    public async Task Indice_EstadosNoCancelados_RechazaDuplicado(string status)
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.AddSupportDataAsync();
        database.Context.Citas.Add(database.CreateAppointment(ScheduleStart, status));
        await database.Context.SaveChangesAsync();
        await using var b = database.CreateIndependentContext();
        b.Citas.Add(database.CreateAppointment(ScheduleStart));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => b.SaveChangesAsync());
        Assert.Equal(2067, Assert.IsType<SqliteException>(error.InnerException).SqliteExtendedErrorCode);
        await using var c = database.CreateIndependentContext();
        Assert.Equal(1, await c.Citas.CountAsync());
    }

    [Theory]
    [InlineData("OtroOdontologo")]
    [InlineData("OtraFecha")]
    [InlineData("Cancelada")]
    [InlineData("VariasCanceladas")]
    public async Task Indice_PermiteReservasCompatibles(string scenario)
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.AddSupportDataAsync();
        var cancelled = scenario is "Cancelada" or "VariasCanceladas";
        database.Context.Citas.Add(database.CreateAppointment(ScheduleStart, cancelled ? "Cancelada" : "Pendiente"));
        await database.Context.SaveChangesAsync();
        var dentist = scenario == "OtroOdontologo" ? await database.AddOtherOdontologistAsync() : database.OdontologistId;
        await using var b = database.CreateIndependentContext();
        b.Citas.Add(database.CreateAppointment(scenario == "OtraFecha" ? ScheduleStart.AddDays(1) : ScheduleStart,
            scenario == "VariasCanceladas" ? "Cancelada" : "Pendiente", odontologistId: dentist));
        await b.SaveChangesAsync();
        await using var c = database.CreateIndependentContext();
        Assert.Equal(2, await c.Citas.CountAsync());
    }

    [Fact]
    public async Task Modelo_IndiceHorarioTieneClaveUnicaYFiltro()
    {
        await using var database = await TestDatabase.CreateAsync();
        var index = Assert.Single(database.Context.Model.FindEntityType(typeof(Cita))!.GetIndexes(),
            i => i.GetDatabaseName() == "UX_Citas_Odontologo_FechaHoraInicio_NoCancelada");
        Assert.Equal(new[] { "OdontologoId", "FechaHoraInicio" }, index.Properties.Select(p => p.Name));
        Assert.True(index.IsUnique);
        Assert.Equal("[EstadoCita] <> 'Cancelada'", index.GetFilter());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Horario_GanadorAntesDeTransaccion_SegundaSolicitudDevuelveFormulario(bool loserReschedules, bool winnerReschedules)
    {
        var gate = new BeforeScheduleTransaction();
        await using var database = await TestDatabase.CreateAsync(gate);
        await database.AddSupportDataAsync();
        var loser = database.CreateAppointment(ScheduleStart.AddHours(-1));
        var winner = database.CreateAppointment(ScheduleStart.AddHours(-2));
        database.Context.Citas.AddRange(loser, winner);
        await database.Context.SaveChangesAsync();
        database.Context.ChangeTracker.Clear();
        await using var a = database.CreateIndependentContext();
        Assert.False(await a.Citas.AnyAsync(x => x.OdontologoId == database.OdontologistId && x.FechaHoraInicio == ScheduleStart && x.EstadoCita != "Cancelada"));
        var winnerOriginal = await a.Citas.SingleAsync(x => x.CitaId == winner.CitaId);

        // A confirma antes de la transacción de B. La carrera real entre conexiones se prueba en SQL Server.
        var invoked = false;
        async Task Win()
        {
            invoked = true;
            if (winnerReschedules) winnerOriginal.FechaHoraInicio = ScheduleStart;
            else a.Citas.Add(database.CreateAppointment(ScheduleStart));
            await a.SaveChangesAsync();
        }
        gate.Callback = Win;
        var controller = database.CreateController("Administrador");
        IActionResult result;
        if (loserReschedules)
        {
            result = await controller.Reschedule(loser.CitaId, new ReagendarCitaViewModel { CitaId = loser.CitaId, FechaHoraInicio = ScheduleStart });
        }
        else
        {
            result = await controller.Create(database.CreateAppointmentModel(ScheduleStart));
        }
        Assert.True(invoked);
        var view = Assert.IsType<ViewResult>(result);
        Assert.Contains("se superpone", Assert.Single(controller.ModelState["FechaHoraInicio"]!.Errors).ErrorMessage);
        Assert.False(controller.TempData.ContainsKey("SuccessMessage"));
        if (!loserReschedules)
        {
            var form = Assert.IsType<CitaFormViewModel>(view.Model);
            Assert.NotEmpty(form.Odontologos);
            Assert.NotEmpty(form.Servicios);
            Assert.False(string.IsNullOrWhiteSpace(form.PacienteNombre));
            Assert.DoesNotContain(database.Context.ChangeTracker.Entries<Cita>(), e => e.State == EntityState.Added);
        }
        await using var c = database.CreateIndependentContext();
        Assert.Equal(1, await c.Citas.CountAsync(x => x.FechaHoraInicio == ScheduleStart && x.EstadoCita != "Cancelada"));
        Assert.Equal(ScheduleStart.AddHours(-1), (await c.Citas.SingleAsync(x => x.CitaId == loser.CitaId)).FechaHoraInicio);
    }

    [Theory]
    [InlineData(false, 787, "FOREIGN KEY constraint failed")]
    [InlineData(true, 787, "FOREIGN KEY constraint failed")]
    [InlineData(false, 2067, "UNIQUE constraint failed: Pacientes.Cedula")]
    [InlineData(true, 2067, "UNIQUE constraint failed: Pacientes.Cedula")]
    [InlineData(true, 5, "database is locked")]
    public async Task ErrorAjenoAlIndice_NoSeConvierteEnConflictoHorario(bool reschedule, int code, string message)
    {
        var saveGate = new ScheduleSaveGate();
        var updateGate = new ScheduleUpdateGate();
        await using var database = await TestDatabase.CreateAsync(saveGate, updateGate);
        await database.AddSupportDataAsync();
        var cita = database.CreateAppointment(ScheduleStart.AddHours(-1));
        database.Context.Citas.Add(cita);
        await database.Context.SaveChangesAsync();
        var providerError = new SqliteException($"SQLite Error {code & 255}: '{message}'.", code & 255, code);
        Exception expected = reschedule ? providerError : new DbUpdateException("Fallo de escritura", providerError);
        Task Fail() => Task.FromException(expected);
        if (reschedule) updateGate.BeforeWrite = Fail;
        else saveGate.BeforeWrite = Fail;
        var controller = database.CreateController("Administrador");
        var error = await Record.ExceptionAsync(async () =>
        {
            if (reschedule) await controller.Reschedule(cita.CitaId, new ReagendarCitaViewModel { CitaId = cita.CitaId, FechaHoraInicio = ScheduleStart });
            else await controller.Create(database.CreateAppointmentModel(ScheduleStart));
        });
        Assert.Same(expected, error);
        Assert.True(controller.ModelState.IsValid);
        Assert.False(controller.TempData.ContainsKey("SuccessMessage"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task H4_ErrorIndiceDuranteEscritura_RevierteYDevuelveFormulario(bool reschedule)
    {
        var save = new ScheduleSaveGate();
        var update = new ScheduleUpdateGate();
        await using var db = await TestDatabase.CreateAsync(save, update);
        await db.AddSupportDataAsync();
        var cita = db.CreateAppointment(ScheduleStart.AddDays(1));
        cita.DuracionProgramadaMinutos = 60;
        db.Context.Add(cita);
        await db.Context.SaveChangesAsync();
        var error = new SqliteException("SQLite Error 19: 'UNIQUE constraint failed: Citas.OdontologoId, Citas.FechaHoraInicio'.", 19, 2067);
        save.BeforeWrite = () => throw new DbUpdateException("Duplicate", error);
        update.BeforeWrite = () => throw error;
        var controller = db.CreateController();
        var result = reschedule
            ? await controller.Reschedule(cita.CitaId, new ReagendarCitaViewModel { CitaId = cita.CitaId, FechaHoraInicio = ScheduleStart })
            : await controller.Create(db.CreateAppointmentModel(ScheduleStart));
        Assert.IsType<ViewResult>(result);
        Assert.Contains(controller.ModelState.Values.SelectMany(v => v.Errors), e => e.ErrorMessage.Contains("Otra operación reservó"));
        Assert.DoesNotContain(db.Context.ChangeTracker.Entries(), e => e.State == EntityState.Added);
        db.Context.ChangeTracker.Clear();
        Assert.Equal(ScheduleStart.AddDays(1), (await db.Context.Citas.SingleAsync()).FechaHoraInicio);
    }

    private sealed class ScheduleSaveGate : SaveChangesInterceptor
    {
        public Func<Task>? BeforeWrite { get; set; }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (BeforeWrite is { } callback)
            {
                BeforeWrite = null;
                await callback();
            }
            return result;
        }
    }

    private sealed class ScheduleUpdateGate : DbCommandInterceptor
    {
        public Func<Task>? BeforeWrite { get; set; }
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) && BeforeWrite is { } callback)
            {
                BeforeWrite = null;
                await callback();
            }
            return result;
        }
    }
}
