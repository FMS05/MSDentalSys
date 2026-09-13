using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MSDentalSys.Data.Context;
using MSDentalSys.Data.InitialData;
using System.Data.Common;
using MSDentalSys.Data.Models;
using MSDentalSys.Web.Models.ViewModels;

namespace MSDentalSys.Web.Controllers;

[Authorize(Roles = "Administrador,Recepcionista,Odontologo")]
public class SubserviciosController(ApplicationDbContext context, ILogger<SubserviciosController> logger) : Controller
{
    private const string DuplicateMessage = "Ya existe un subservicio con ese nombre en el servicio seleccionado, incluso si está inactivo.";

    [HttpGet, Authorize(Roles = "Administrador,Recepcionista")]
    public async Task<IActionResult> ParaCitas(int servicioOdontologicoId)
    {
        if (!await context.ServiciosOdontologicos.AsNoTracking()
            .AnyAsync(s => s.ServicioOdontologicoId == servicioOdontologicoId && s.Estado))
            return NotFound();

        return Json(await context.SubserviciosOdontologicos.AsNoTracking()
            .Where(s => s.ServicioOdontologicoId == servicioOdontologicoId && s.Estado)
            .OrderBy(s => s.Nombre)
            .Select(s => new { s.SubservicioOdontologicoId, s.Nombre, s.DuracionEstimadaMinutos })
            .ToListAsync());
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? servicioId) => View(await context.SubserviciosOdontologicos
        .Include(s => s.ServicioOdontologico).AsNoTracking()
        .Where(s => !servicioId.HasValue || s.ServicioOdontologicoId == servicioId)
        .OrderBy(s => s.ServicioOdontologico.Nombre).ThenBy(s => s.Nombre).ToListAsync());

    [HttpGet]
    public async Task<IActionResult> Details(int? id)
    {
        var item = await context.SubserviciosOdontologicos.Include(s => s.ServicioOdontologico)
            .AsNoTracking().SingleOrDefaultAsync(s => s.SubservicioOdontologicoId == id);
        return item is null ? NotFound() : View(item);
    }

    [HttpGet, Authorize(Roles = "Administrador")]
    public async Task<IActionResult> Create(int? servicioId)
    {
        var model = new SubservicioFormViewModel { ServicioOdontologicoId = servicioId ?? 0 };
        await LoadOptionsAsync(model);
        return View(model);
    }

    [HttpPost, Authorize(Roles = "Administrador"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SubservicioFormViewModel model)
    {
        ValidateFields(model);
        if (!await context.ServiciosOdontologicos.AnyAsync(s => s.ServicioOdontologicoId == model.ServicioOdontologicoId && s.Estado))
            ModelState.AddModelError(nameof(model.ServicioOdontologicoId), "El servicio no existe o está inactivo.");
        await ValidateDuplicateAsync(model, 0);
        if (!ModelState.IsValid) { await LoadOptionsAsync(model); return View(model); }
        var item = new SubservicioOdontologico
        {
            ServicioOdontologicoId = model.ServicioOdontologicoId,
            Nombre = model.Nombre, Descripcion = model.Descripcion,
            DuracionEstimadaMinutos = model.DuracionEstimadaMinutos!.Value,
            Clasificacion = model.Clasificacion
        };
        context.SubserviciosOdontologicos.Add(item);
        if (!await SaveAsync()) { await LoadOptionsAsync(model); return View(model); }
        TempData["SuccessMessage"] = "Subservicio creado correctamente.";
        return RedirectToAction(nameof(Details), new { id = item.SubservicioOdontologicoId });
    }

    [HttpGet, Authorize(Roles = "Administrador")]
    public async Task<IActionResult> Edit(int? id)
    {
        var item = await context.SubserviciosOdontologicos.Include(s => s.ServicioOdontologico)
            .AsNoTracking().SingleOrDefaultAsync(s => s.SubservicioOdontologicoId == id);
        return item is null ? NotFound() : View(new SubservicioFormViewModel
        {
            SubservicioOdontologicoId = item.SubservicioOdontologicoId,
            ServicioOdontologicoId = item.ServicioOdontologicoId, ServicioNombre = item.ServicioOdontologico.Nombre,
            Nombre = item.Nombre, Descripcion = item.Descripcion, DuracionEstimadaMinutos = item.DuracionEstimadaMinutos,
            Clasificacion = item.Clasificacion
        });
    }

    [HttpPost, Authorize(Roles = "Administrador"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, SubservicioFormViewModel model)
    {
        if (id != model.SubservicioOdontologicoId) return NotFound();
        var item = await context.SubserviciosOdontologicos.Include(s => s.ServicioOdontologico)
            .SingleOrDefaultAsync(s => s.SubservicioOdontologicoId == id);
        if (item is null) return NotFound();
        if (model.ServicioOdontologicoId != item.ServicioOdontologicoId)
            ModelState.AddModelError(nameof(model.ServicioOdontologicoId), "No se puede cambiar el servicio padre.");
        model.ServicioOdontologicoId = item.ServicioOdontologicoId;
        model.ServicioNombre = item.ServicioOdontologico.Nombre;
        ValidateFields(model);
        await ValidateDuplicateAsync(model, id);
        if (!ModelState.IsValid) return View(model);
        item.Nombre = model.Nombre;
        item.Descripcion = model.Descripcion;
        item.DuracionEstimadaMinutos = model.DuracionEstimadaMinutos!.Value;
        item.Clasificacion = model.Clasificacion;
        if (!await SaveAsync()) return View(model);
        TempData["SuccessMessage"] = "Subservicio actualizado correctamente.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, Authorize(Roles = "Administrador"), ValidateAntiForgeryToken]
    public Task<IActionResult> Activate(int id) => ChangeStatusAsync(id, true);

    [HttpPost, Authorize(Roles = "Administrador"), ValidateAntiForgeryToken]
    public Task<IActionResult> Deactivate(int id) => ChangeStatusAsync(id, false);

    private async Task<IActionResult> ChangeStatusAsync(int id, bool state)
    {
        var item = await context.SubserviciosOdontologicos.Include(s => s.ServicioOdontologico)
            .SingleOrDefaultAsync(s => s.SubservicioOdontologicoId == id);
        if (item is null) return NotFound();
        if (state && !item.ServicioOdontologico.Estado)
        {
            TempData["ErrorMessage"] = "Activa primero el servicio padre para activar este subservicio.";
            return RedirectToAction(nameof(Details), new { id });
        }
        item.Estado = state;
        await context.SaveChangesAsync();
        TempData["SuccessMessage"] = state ? "Subservicio activado correctamente." : "Subservicio desactivado correctamente.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, Authorize(Roles = "Administrador"), ValidateAntiForgeryToken]
    public async Task<IActionResult> PrepareDefinitiveCatalog()
    {
        try
        {
            var changed = await SubservicioCatalogoSeeder.SeedAsync(context);
            TempData["SuccessMessage"] = changed
                ? "El catálogo odontológico definitivo fue cargado correctamente."
                : "El catálogo odontológico definitivo ya se encuentra actualizado.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException or DbException)
        {
            if (ex is SubservicioCatalogoSeeder.CatalogoValidationException)
                logger.LogWarning("Carga del catálogo definitivo abortada: {Motivo}", ex.Message);
            else
                logger.LogError("Carga del catálogo definitivo abortada por {TipoError}. Se omitieron detalles del proveedor para proteger datos sensibles.", ex.GetType().Name);
            TempData["ErrorMessage"] = "No se pudo cargar el catálogo definitivo. Revisa los servicios, las identidades históricas y las colisiones de nombres o códigos. No se aplicaron cambios.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, Authorize(Roles = "Administrador"), ValidateAntiForgeryToken]
    public IActionResult LoadInitialCatalog()
    {
        TempData["ErrorMessage"] = "La carga del catálogo provisional está deshabilitada. Utiliza la carga explícita del catálogo definitivo.";
        return RedirectToAction(nameof(Index));
    }

    private void ValidateFields(SubservicioFormViewModel model)
    {
        if (model.Clasificacion is not (ClasificacionSubservicio.Principal or ClasificacionSubservicio.Complementario))
            ModelState.AddModelError(nameof(model.Clasificacion), "Selecciona una clasificación válida: Principal o Complementario.");
        model.Nombre = model.Nombre?.Trim() ?? string.Empty;
        model.Descripcion = string.IsNullOrWhiteSpace(model.Descripcion) ? null : model.Descripcion.Trim();
        if (model.Nombre.Length is 0 or > 100)
            ModelState.AddModelError(nameof(model.Nombre), "El nombre es obligatorio y no puede superar 100 caracteres.");
        if (model.Descripcion?.Length > 300)
            ModelState.AddModelError(nameof(model.Descripcion), "La descripción no puede superar 300 caracteres.");
        if (model.DuracionEstimadaMinutos is null or < 1 or > 1440)
            ModelState.AddModelError(nameof(model.DuracionEstimadaMinutos), "La duración debe estar entre 1 y 1440 minutos.");
    }

    private async Task ValidateDuplicateAsync(SubservicioFormViewModel model, int excludedId)
    {
        if (await context.SubserviciosOdontologicos.AnyAsync(s => s.ServicioOdontologicoId == model.ServicioOdontologicoId &&
            s.Nombre == model.Nombre && s.SubservicioOdontologicoId != excludedId))
            ModelState.AddModelError(nameof(model.Nombre), DuplicateMessage);
    }

    private async Task LoadOptionsAsync(SubservicioFormViewModel model) => model.Servicios = await context.ServiciosOdontologicos
        .AsNoTracking().Where(s => s.Estado).OrderBy(s => s.Nombre)
        .Select(s => new SelectListItem(s.Nombre, s.ServicioOdontologicoId.ToString())).ToListAsync();

    private async Task<bool> SaveAsync()
    {
        try { await context.SaveChangesAsync(); return true; }
        catch (DbUpdateException ex) when (IsNameCollision(ex.InnerException))
        {
            context.ChangeTracker.Clear();
            ModelState.AddModelError(nameof(SubservicioFormViewModel.Nombre), DuplicateMessage);
            return false;
        }
    }

    private static bool IsNameCollision(Exception? ex) => ex switch
    {
        SqlException sql => sql.Errors.Cast<SqlError>().Any(e => e.Number is 2601 or 2627 &&
            e.Message.Contains("'UX_Subservicios_Servicio_Nombre'", StringComparison.Ordinal)),
        SqliteException { SqliteErrorCode: 19, SqliteExtendedErrorCode: 2067 } sqlite =>
            sqlite.Message.Contains("UNIQUE constraint failed: SubserviciosOdontologicos.ServicioOdontologicoId, SubserviciosOdontologicos.Nombre'", StringComparison.Ordinal),
        _ => false
    };
}
