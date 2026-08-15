using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MSDentalSys.Data.Context;
using MSDentalSys.Data.Models;
using MSDentalSys.Web.Models.ViewModels;

namespace MSDentalSys.Web.Controllers;

[Authorize(Roles = "Administrador,Odontologo")]
public class EvolucionesClinicasController : Controller
{
    private readonly ApplicationDbContext _context;

    public EvolucionesClinicasController(ApplicationDbContext context)
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

        ViewData["Atencion"] = atencion;
        return View(new EvolucionClinicaCreateViewModel
        {
            AtencionOdontologicaId = atencionId,
            FechaEvolucion = DateTime.Now
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(EvolucionClinicaCreateViewModel model)
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

        if (!ModelState.IsValid)
        {
            ViewData["Atencion"] = atencion;
            return View(model);
        }

        _context.EvolucionesClinicas.Add(new EvolucionClinica
        {
            AtencionOdontologicaId = atencion.AtencionOdontologicaId,
            FechaEvolucion = model.FechaEvolucion,
            Descripcion = model.Descripcion.Trim()
        });

        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = "Evolución clínica registrada correctamente.";
        return RedirectToAction("Details", "Atenciones", new { id = atencion.AtencionOdontologicaId });
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

    private void AddExplicitValidationErrors(EvolucionClinicaCreateViewModel model)
    {
        if (model.FechaEvolucion == default)
        {
            ModelState.AddModelError(nameof(model.FechaEvolucion), "La fecha de evolución es obligatoria.");
        }

        if (string.IsNullOrWhiteSpace(model.Descripcion))
        {
            ModelState.AddModelError(nameof(model.Descripcion), "La descripción es obligatoria.");
        }
        else if (model.Descripcion.Length > 500)
        {
            ModelState.AddModelError(nameof(model.Descripcion), "La descripción no puede superar los 500 caracteres.");
        }
    }
}
