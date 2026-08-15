using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MSDentalSys.Data.Context;
using MSDentalSys.Data.Models;
using MSDentalSys.Web.Models.ViewModels;

namespace MSDentalSys.Web.Controllers;

[Authorize(Roles = "Administrador,Odontologo")]
public class TratamientosController : Controller
{
    private static readonly string[] EstadosPermitidos = ["Planificado", "En progreso", "Completado"];

    private readonly ApplicationDbContext _context;

    public TratamientosController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> Create(int atencionId)
    {
        var atencion = await GetAtencionQuery()
            .AsNoTracking()
            .SingleOrDefaultAsync(a => a.AtencionOdontologicaId == atencionId);

        if (atencion is null)
        {
            return NotFound();
        }

        if (!CanAccessAttention(atencion))
        {
            return Forbid();
        }

        var model = new TratamientoCreateViewModel
        {
            AtencionOdontologicaId = atencionId,
            FechaInicio = DateTime.Today,
            EstadoTratamiento = "Planificado"
        };
        await LoadFormDataAsync(model, atencion);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(TratamientoCreateViewModel model)
    {
        AddExplicitValidationErrors(model);

        var atencion = await GetAtencionQuery()
            .SingleOrDefaultAsync(a => a.AtencionOdontologicaId == model.AtencionOdontologicaId);

        if (atencion is null)
        {
            return NotFound();
        }

        if (!CanAccessAttention(atencion))
        {
            return Forbid();
        }

        var servicio = await _context.ServiciosOdontologicos
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.ServicioOdontologicoId == model.ServicioOdontologicoId && s.Estado);

        if (servicio is null)
        {
            ModelState.AddModelError(nameof(model.ServicioOdontologicoId), "El servicio seleccionado no existe o está inactivo.");
        }

        if (!EstadosPermitidos.Contains(model.EstadoTratamiento))
        {
            ModelState.AddModelError(nameof(model.EstadoTratamiento), "El estado seleccionado no es válido.");
        }

        if (!ModelState.IsValid)
        {
            await LoadFormDataAsync(model, atencion);
            return View(model);
        }

        _context.Tratamientos.Add(new Tratamiento
        {
            AtencionOdontologicaId = atencion.AtencionOdontologicaId,
            ServicioOdontologicoId = servicio!.ServicioOdontologicoId,
            FechaInicio = model.FechaInicio,
            EstadoTratamiento = model.EstadoTratamiento,
            Observaciones = string.IsNullOrWhiteSpace(model.Observaciones) ? null : model.Observaciones.Trim()
        });

        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = "Tratamiento registrado correctamente.";
        return RedirectToAction("Details", "Atenciones", new { id = atencion.AtencionOdontologicaId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(int id, string estado)
    {
        if (!EstadosPermitidos.Contains(estado))
        {
            TempData["ErrorMessage"] = "El estado seleccionado no es válido.";
            return RedirectToAction("Details", "Atenciones", new { id });
        }

        var tratamiento = await _context.Tratamientos
            .Include(t => t.AtencionOdontologica)
            .SingleOrDefaultAsync(t => t.TratamientoId == id);

        if (tratamiento is null)
        {
            return NotFound();
        }

        if (!CanAccessAttention(tratamiento.AtencionOdontologica))
        {
            return Forbid();
        }

        if (tratamiento.EstadoTratamiento == "Completado")
        {
            TempData["ErrorMessage"] = "Un tratamiento completado no puede cambiar de estado.";
            return RedirectToAction("Details", "Atenciones", new { id = tratamiento.AtencionOdontologicaId });
        }

        if (!CanTransition(tratamiento.EstadoTratamiento, estado))
        {
            TempData["ErrorMessage"] = "No se permite retroceder el estado del tratamiento.";
            return RedirectToAction("Details", "Atenciones", new { id = tratamiento.AtencionOdontologicaId });
        }

        tratamiento.EstadoTratamiento = estado;
        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = "Estado del tratamiento actualizado correctamente.";
        return RedirectToAction("Details", "Atenciones", new { id = tratamiento.AtencionOdontologicaId });
    }

    private static bool CanTransition(string currentStatus, string nextStatus)
    {
        return (currentStatus, nextStatus) switch
        {
            ("Planificado", "En progreso") => true,
            ("Planificado", "Completado") => true,
            ("En progreso", "Completado") => true,
            _ => false
        };
    }

    private IQueryable<AtencionOdontologica> GetAtencionQuery()
    {
        return _context.AtencionesOdontologicas
            .Include(a => a.Paciente)
            .Include(a => a.Odontologo)
            .Include(a => a.Cita);
    }

    private bool CanAccessAttention(AtencionOdontologica atencion)
    {
        return !User.IsInRole("Odontologo") ||
            atencion.OdontologoId == User.FindFirstValue(ClaimTypes.NameIdentifier);
    }

    private async Task LoadFormDataAsync(TratamientoCreateViewModel model, AtencionOdontologica atencion)
    {
        ViewData["Atencion"] = atencion;
        var servicios = await _context.ServiciosOdontologicos
            .Where(s => s.Estado)
            .OrderBy(s => s.Nombre)
            .AsNoTracking()
            .ToListAsync();

        model.Servicios = servicios.Select(s => new SelectListItem
        {
            Value = s.ServicioOdontologicoId.ToString(),
            Text = s.Nombre,
            Selected = s.ServicioOdontologicoId == model.ServicioOdontologicoId
        });
    }

    private void AddExplicitValidationErrors(TratamientoCreateViewModel model)
    {
        if (model.FechaInicio == default)
        {
            ModelState.AddModelError(nameof(model.FechaInicio), "La fecha de inicio es obligatoria.");
        }

        if (string.IsNullOrWhiteSpace(model.EstadoTratamiento))
        {
            ModelState.AddModelError(nameof(model.EstadoTratamiento), "El estado del tratamiento es obligatorio.");
        }

        if (model.Observaciones?.Length > 400)
        {
            ModelState.AddModelError(nameof(model.Observaciones), "Las observaciones no pueden superar los 400 caracteres.");
        }
    }
}
