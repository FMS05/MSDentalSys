using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MSDentalSys.Data.Context;
using MSDentalSys.Data.Models;
using MSDentalSys.Web.Models.ViewModels;

namespace MSDentalSys.Web.Controllers;

[Authorize(Roles = "Administrador,Odontologo")]
public class AtencionesController : Controller
{
    private readonly ApplicationDbContext _context;

    public AtencionesController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> Create(int citaId)
    {
        var cita = await GetCitaQuery().AsNoTracking().SingleOrDefaultAsync(c => c.CitaId == citaId);
        if (cita is null)
        {
            return NotFound();
        }

        var validationError = await ValidateCanStartAsync(cita);
        if (validationError is not null)
        {
            if (validationError == "FORBIDDEN")
            {
                return Forbid();
            }

            TempData["ErrorMessage"] = validationError;
            return RedirectToAction("Details", "Citas", new { id = citaId });
        }

        SetAppointmentViewData(cita);
        return View(new AtencionOdontologicaCreateViewModel { CitaId = citaId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AtencionOdontologicaCreateViewModel model)
    {
        if (!ModelState.IsValid)
        {
            await ReturnCreateViewWithAppointmentAsync(model.CitaId);
            return View(model);
        }

        await using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            var cita = await GetCitaQuery().SingleOrDefaultAsync(c => c.CitaId == model.CitaId);
            if (cita is null)
            {
                await transaction.RollbackAsync();
                return NotFound();
            }

            var validationError = await ValidateCanStartAsync(cita);
            if (validationError is not null)
            {
                await transaction.RollbackAsync();
                if (validationError == "FORBIDDEN")
                {
                    return Forbid();
                }

                ModelState.AddModelError(string.Empty, validationError);
                SetAppointmentViewData(cita);
                return View(model);
            }

            var atencion = new AtencionOdontologica
            {
                PacienteId = cita.PacienteId,
                CitaId = cita.CitaId,
                OdontologoId = cita.OdontologoId,
                FechaAtencion = DateTime.Now,
                MotivoConsulta = model.MotivoConsulta.Trim(),
                Observaciones = string.IsNullOrWhiteSpace(model.Observaciones)
                    ? null
                    : model.Observaciones.Trim()
            };

            _context.AtencionesOdontologicas.Add(atencion);
            await _context.SaveChangesAsync();

            cita.EstadoCita = "Atendida";
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["SuccessMessage"] = "La atención odontológica fue registrada correctamente.";
            return RedirectToAction(nameof(Details), new { id = atencion.AtencionOdontologicaId });
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync();
            ModelState.AddModelError(string.Empty, "No fue posible registrar la atención. Verifica que la cita no haya sido atendida previamente.");
            await ReturnCreateViewWithAppointmentAsync(model.CitaId);
            return View(model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var atencion = await _context.AtencionesOdontologicas
            .Include(a => a.Paciente)
            .Include(a => a.Cita)
            .Include(a => a.Odontologo)
            .Include(a => a.Diagnosticos)
            .Include(a => a.Tratamientos)
                .ThenInclude(t => t.ServicioOdontologico)
            .Include(a => a.EvolucionesClinicas)
            .AsNoTracking()
            .SingleOrDefaultAsync(a => a.AtencionOdontologicaId == id);

        if (atencion is null)
        {
            return NotFound();
        }

        if (User.IsInRole("Odontologo") &&
            atencion.OdontologoId != User.FindFirstValue(ClaimTypes.NameIdentifier))
        {
            return Forbid();
        }

        return View(atencion);
    }

    private IQueryable<Cita> GetCitaQuery()
    {
        return _context.Citas
            .Include(c => c.Paciente)
            .Include(c => c.Odontologo)
            .Include(c => c.ServicioOdontologico);
    }

    private async Task<string?> ValidateCanStartAsync(Cita cita)
    {
        if (User.IsInRole("Odontologo") && cita.OdontologoId != User.FindFirstValue(ClaimTypes.NameIdentifier))
        {
            return "FORBIDDEN";
        }

        if (cita.EstadoCita != "Pendiente" && cita.EstadoCita != "Confirmada")
        {
            return "Solo se puede iniciar atención para una cita Pendiente o Confirmada.";
        }

        if (await _context.AtencionesOdontologicas.AnyAsync(a => a.CitaId == cita.CitaId))
        {
            return "Esta cita ya tiene una atención odontológica registrada.";
        }

        if (cita.Paciente is null)
        {
            return "El paciente asignado a la cita no existe.";
        }

        if (cita.Odontologo is null)
        {
            return "El odontólogo asignado a la cita no existe.";
        }

        return null;
    }

    private void SetAppointmentViewData(Cita cita)
    {
        ViewData["Cita"] = cita;
    }

    private async Task ReturnCreateViewWithAppointmentAsync(int citaId)
    {
        var cita = await GetCitaQuery().AsNoTracking().SingleOrDefaultAsync(c => c.CitaId == citaId);
        if (cita is not null)
        {
            SetAppointmentViewData(cita);
        }
    }
}
