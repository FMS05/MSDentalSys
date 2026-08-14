using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MSDentalSys.Data.Context;
using MSDentalSys.Data.Models;
using MSDentalSys.Web.Models.ViewModels;

namespace MSDentalSys.Web.Controllers;

[Authorize(Roles = "Administrador,Odontologo")]
public class DiagnosticosController : Controller
{
    private readonly ApplicationDbContext _context;

    public DiagnosticosController(ApplicationDbContext context)
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

        SetAttentionViewData(atencion);
        return View(new DiagnosticoCreateViewModel
        {
            AtencionOdontologicaId = atencionId
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DiagnosticoCreateViewModel model)
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
            SetAttentionViewData(atencion);
            return View(model);
        }

        _context.Diagnosticos.Add(new Diagnostico
        {
            AtencionOdontologicaId = atencion.AtencionOdontologicaId,
            Descripcion = model.Descripcion.Trim(),
            Observaciones = string.IsNullOrWhiteSpace(model.Observaciones)
                ? null
                : model.Observaciones.Trim()
        });

        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = "Diagnóstico registrado correctamente.";
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

    private void SetAttentionViewData(AtencionOdontologica atencion)
    {
        ViewData["Atencion"] = atencion;
    }

    private void AddExplicitValidationErrors(DiagnosticoCreateViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Descripcion))
        {
            ModelState.AddModelError(nameof(model.Descripcion), "La descripción del diagnóstico es obligatoria.");
        }
        else if (model.Descripcion.Length > 300)
        {
            ModelState.AddModelError(nameof(model.Descripcion), "La descripción no puede superar los 300 caracteres.");
        }

        if (model.Observaciones?.Length > 300)
        {
            ModelState.AddModelError(nameof(model.Observaciones), "Las observaciones no pueden superar los 300 caracteres.");
        }
    }
}
