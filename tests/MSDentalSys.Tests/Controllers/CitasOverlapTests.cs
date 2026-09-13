using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MSDentalSys.Web.Models.ViewModels;
using Xunit;

namespace MSDentalSys.Tests.Controllers;

public partial class CitasControllerTests
{
    [Fact]
    public async Task H8_Reagendar_ExcluyePropiaCitaYConservaSnapshot()
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.AddSupportDataAsync();
        var moving = db.CreateAppointment(ScheduleStart);
        moving.SubservicioOdontologicoId = db.SubserviceId;
        moving.DuracionProgramadaMinutos = 60;
        db.Context.Add(moving);
        (await db.Context.SubserviciosOdontologicos.SingleAsync()).DuracionEstimadaMinutos = 1;
        await db.Context.SaveChangesAsync();
        var result = await db.CreateController().Reschedule(moving.CitaId,
            new ReagendarCitaViewModel { CitaId = moving.CitaId, FechaHoraInicio = ScheduleStart.AddMinutes(30) });
        Assert.IsType<RedirectToActionResult>(result);
        db.Context.ChangeTracker.Clear();
        Assert.Equal(60, (await db.Context.Citas.SingleAsync()).DuracionProgramadaMinutos);
    }

    public static IEnumerable<object[]> OverlapCases()
    {
        foreach (var reschedule in new[] { false, true })
        {
            foreach (var (offset, duration, conflict) in new[] {
                (-60, 60, false), (60, 60, false), (-30, 60, true),
                (30, 60, true), (0, 60, true), (-30, 120, true), (15, 15, true),
                (-1, 1, false), (59, 1, true), (-1439, 1440, true), (-1440, 1440, false) })
                yield return new object[] { reschedule, offset, duration, 60, "Pendiente", false, conflict };
            foreach (var state in new[] { "Confirmada", "Atendida", "No asistió", "Cancelada" })
                yield return new object[] { reschedule, 30, 60, 60, state, false, state != "Cancelada" };
            yield return new object[] { reschedule, 0, 60, 60, "Pendiente", true, false };
            yield return new object[] { reschedule, 0, 60, null!, "Pendiente", false, true };
            yield return new object[] { reschedule, 30, 60, null!, "Pendiente", false, false };
            yield return new object[] { reschedule, 1439, 1, 1440, "Pendiente", false, true };
            yield return new object[] { reschedule, 1440, 1, 1440, "Pendiente", false, false };
            yield return new object[] { reschedule, 1, 1, 1, "Pendiente", false, false };
            if (reschedule)
            {
                yield return new object[] { true, 0, null!, 60, "Pendiente", false, true };
                yield return new object[] { true, 30, null!, 60, "Pendiente", false, false };
                yield return new object[] { true, 0, null!, null!, "Pendiente", false, true };
                yield return new object[] { true, 30, null!, null!, "Pendiente", false, false };
            }
        }
    }

    [Theory]
    [MemberData(nameof(OverlapCases))]
    public async Task H8_IntervalosEstadosHistoricos(bool reschedule, int offset, int? duration,
        int? existingDuration, string state, bool otherDentist, bool conflict)
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.AddSupportDataAsync();
        // 23:30 exercises both sides of midnight with the same interval matrix.
        var start = new DateTime(2030, 2, 1, 23, 30, 0);
        var existing = db.CreateAppointment(start, state, odontologistId:
            otherDentist ? await db.AddOtherOdontologistAsync() : null);
        existing.DuracionProgramadaMinutos = existingDuration;
        db.Context.Citas.Add(existing);
        var moving = db.CreateAppointment(start.AddDays(4));
        moving.SubservicioOdontologicoId = db.SubserviceId;
        moving.DuracionProgramadaMinutos = duration;
        if (reschedule) db.Context.Citas.Add(moving);
        else (await db.Context.SubserviciosOdontologicos.SingleAsync()).DuracionEstimadaMinutos = duration!.Value;
        await db.Context.SaveChangesAsync();
        var controller = db.CreateController();
        var target = start.AddMinutes(offset);
        var result = reschedule
            ? await controller.Reschedule(moving.CitaId, new ReagendarCitaViewModel { CitaId = moving.CitaId, FechaHoraInicio = target })
            : await controller.Create(db.CreateAppointmentModel(target));
        if (conflict)
        {
            var view = Assert.IsType<ViewResult>(result);
            Assert.Contains(controller.ModelState.Values.SelectMany(v => v.Errors), e => e.ErrorMessage ==
                "El horario seleccionado se superpone con otra cita del odontólogo. Selecciona una hora diferente.");
            if (!reschedule)
            {
                var form = Assert.IsType<CitaFormViewModel>(view.Model);
                Assert.NotEmpty(form.Odontologos); Assert.NotEmpty(form.Servicios); Assert.NotEmpty(form.Subservicios);
                Assert.Equal(db.SubserviceId, form.SubservicioOdontologicoId);
            }
        }
        else Assert.IsType<RedirectToActionResult>(result);
        db.Context.ChangeTracker.Clear();
        Assert.Equal(existingDuration, (await db.Context.Citas.FindAsync(existing.CitaId))!.DuracionProgramadaMinutos);
        if (reschedule)
        {
            var stored = (await db.Context.Citas.FindAsync(moving.CitaId))!;
            Assert.Equal(conflict ? start.AddDays(4) : target, stored.FechaHoraInicio);
            Assert.Equal(duration, stored.DuracionProgramadaMinutos);
            Assert.Equal(db.SubserviceId, stored.SubservicioOdontologicoId);
        }
        else Assert.Equal(conflict ? 1 : 2, await db.Context.Citas.CountAsync());
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public async Task H8_ExtremosDateTime_NoDesbordan(bool reschedule, bool maximum)
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.AddSupportDataAsync();
        var moving = db.CreateAppointment(ScheduleStart);
        moving.DuracionProgramadaMinutos = 60;
        if (reschedule) { db.Context.Add(moving); await db.Context.SaveChangesAsync(); }
        var controller = db.CreateController();
        var target = maximum ? DateTime.MaxValue : DateTime.MinValue;
        var result = reschedule
            ? await controller.Reschedule(moving.CitaId, new ReagendarCitaViewModel { CitaId = moving.CitaId, FechaHoraInicio = target })
            : await controller.Create(db.CreateAppointmentModel(target));
        if (maximum) Assert.IsType<ViewResult>(result);
        else Assert.IsType<RedirectToActionResult>(result);
    }
}
