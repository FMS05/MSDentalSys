using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MSDentalSys.Data.Models;
using MSDentalSys.Web.Models.ViewModels;
using Xunit;

namespace MSDentalSys.Tests.Controllers;

public partial class CitasControllerTests
{
    [Fact]
    public void Subservicio_RequeridoYSinDuracionEditable()
    {
        var model = new CitaFormViewModel();
        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), errors, true);
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(model.SubservicioOdontologicoId)));
        Assert.Null(typeof(CitaFormViewModel).GetProperty("DuracionProgramadaMinutos"));
    }

    [Theory]
    [InlineData("ausente")]
    [InlineData("inexistente")]
    [InlineData("inactivo")]
    [InlineData("otroServicio")]
    [InlineData("servicioInactivo")]
    [InlineData("servicioInexistente")]
    [InlineData("duracionCero")]
    [InlineData("duracionExcesiva")]
    public async Task Create_RechazaCatalogoInvalido(string caso)
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.AddSupportDataAsync();
        var controller = db.CreateController();
        await controller.Create(); // El catálogo puede cambiar después del GET.
        var model = db.CreateAppointmentModel(new DateTime(2030, 3, 1, 9, 0, 0));
        var sub = await db.Context.SubserviciosOdontologicos.SingleAsync();
        switch (caso)
        {
            case "ausente": model.SubservicioOdontologicoId = null; break;
            case "inexistente": model.SubservicioOdontologicoId = int.MaxValue; break;
            case "inactivo": sub.Estado = false; break;
            case "otroServicio":
                var other = new ServicioOdontologico { Nombre = "Otro servicio" };
                db.Context.Add(other); await db.Context.SaveChangesAsync();
                model.ServicioOdontologicoId = other.ServicioOdontologicoId; break;
            case "servicioInactivo": (await db.Context.ServiciosOdontologicos.SingleAsync()).Estado = false; break;
            case "servicioInexistente": model.ServicioOdontologicoId = int.MaxValue; break;
            case "duracionCero":
            case "duracionExcesiva":
                // Simula datos legados/corruptos fuera del CHECK de SQLite.
                await db.Context.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints = ON");
                sub.DuracionEstimadaMinutos = caso == "duracionCero" ? 0 : 1441; break;
        }
        await db.Context.SaveChangesAsync();
        Assert.IsType<ViewResult>(await controller.Create(model));
        Assert.False(controller.ModelState.IsValid);
        Assert.Empty(await db.Context.Citas.ToListAsync());
        if (caso is "inactivo" or "inexistente" or "otroServicio" or "servicioInactivo" or "servicioInexistente")
        {
            Assert.Null(model.SubservicioOdontologicoId);
            Assert.DoesNotContain(model.Subservicios, s => s.Selected);
        }
    }

    [Fact]
    public async Task Snapshot_UsaBD_ConservaHistoricoYReagendar_NuevaCitaUsaNuevoValor()
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.AddSupportDataAsync();
        var start = new DateTime(2030, 3, 1, 9, 0, 0);
        Assert.IsType<RedirectToActionResult>(await db.CreateController().Create(db.CreateAppointmentModel(start)));
        var cita = await db.Context.Citas.AsNoTracking().SingleAsync();
        Assert.Equal(db.SubserviceId, cita.SubservicioOdontologicoId);
        Assert.Equal(60, cita.DuracionProgramadaMinutos);
        (await db.Context.SubserviciosOdontologicos.SingleAsync()).DuracionEstimadaMinutos = 90;
        await db.Context.SaveChangesAsync();
        Assert.IsType<RedirectToActionResult>(await db.CreateController().Reschedule(cita.CitaId,
            new ReagendarCitaViewModel { CitaId = cita.CitaId, FechaHoraInicio = start.AddDays(1) }));
        var preserved = await db.Context.Citas.AsNoTracking().SingleAsync();
        Assert.Equal(db.ServiceId, preserved.ServicioOdontologicoId);
        Assert.Equal(db.SubserviceId, preserved.SubservicioOdontologicoId);
        Assert.Equal(60, preserved.DuracionProgramadaMinutos);
        Assert.IsType<RedirectToActionResult>(await db.CreateController().Create(db.CreateAppointmentModel(start)));
        Assert.Equal(90, (await db.Context.Citas.AsNoTracking().SingleAsync(c => c.CitaId != cita.CitaId)).DuracionProgramadaMinutos);
    }

    [Fact]
    public async Task PostInvalido_ReconstruyeSeleccion_YGetNoCargaSubservicios()
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.AddSupportDataAsync();
        var controller = db.CreateController();
        var initial = Assert.IsType<CitaFormViewModel>(Assert.IsType<ViewResult>(await controller.Create()).Model);
        Assert.Empty(initial.Subservicios);
        var model = db.CreateAppointmentModel(new DateTime(2030, 3, 1, 9, 0, 0));
        controller.ModelState.AddModelError("Observaciones", "Error de otro campo");
        Assert.IsType<ViewResult>(await controller.Create(model));
        Assert.Equal("Paciente Ficticio", model.PacienteNombre);
        Assert.Contains(model.Odontologos, s => s.Selected);
        Assert.Contains(model.Servicios, s => s.Selected);
        Assert.Equal(db.SubserviceId.ToString(), Assert.Single(model.Subservicios, s => s.Selected).Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Details_CargaSubservicio_OAceptaHistorico(bool historico)
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.AddSupportDataAsync();
        var cita = db.CreateAppointment(new DateTime(2030, 3, 1, 9, 0, 0));
        if (!historico) { cita.SubservicioOdontologicoId = db.SubserviceId; cita.DuracionProgramadaMinutos = 60; }
        db.Context.Add(cita); await db.Context.SaveChangesAsync(); db.Context.ChangeTracker.Clear();
        var result = Assert.IsType<Cita>(Assert.IsType<ViewResult>(await db.CreateController().Details(cita.CitaId)).Model);
        if (historico) { Assert.Null(result.SubservicioOdontologico); Assert.Null(result.DuracionProgramadaMinutos); }
        else { Assert.Equal("Procedimiento de prueba", result.SubservicioOdontologico!.Nombre); Assert.Equal(60, result.DuracionProgramadaMinutos); }
    }
}
