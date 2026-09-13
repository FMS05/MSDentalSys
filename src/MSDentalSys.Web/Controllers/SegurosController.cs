using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MSDentalSys.Data.Context;
using MSDentalSys.Data.Models;
using MSDentalSys.Web.Models.ViewModels;

namespace MSDentalSys.Web.Controllers
{
    [Authorize(Roles = "Administrador")]
    public class SegurosController : Controller
    {
        private readonly ApplicationDbContext _context;

        public SegurosController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var seguros = await _context.Seguros
                .AsNoTracking()
                .OrderBy(seguro => seguro.Nombre)
                .ToListAsync();

            return View(seguros);
        }

        [HttpGet]
        public async Task<IActionResult> Details(int? id)
        {
            if (id is null)
            {
                return NotFound();
            }

            var seguro = await _context.Seguros
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.SeguroId == id);

            return seguro is null ? NotFound() : View(seguro);
        }

        [HttpGet]
        public IActionResult Create()
        {
            return View(new SeguroFormViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(SeguroFormViewModel model)
        {
            var nombre = model.Nombre?.Trim() ?? string.Empty;
            await AddDuplicateErrorIfNeededAsync(nombre);

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var seguro = new Seguro
            {
                Nombre = nombre,
                Estado = true,
                FechaCreacion = DateTime.Now
            };
            _context.Seguros.Add(seguro);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException exception) when (IsDuplicateNombre(exception))
            {
                _context.Entry(seguro).State = EntityState.Detached;
                ModelState.AddModelError(nameof(model.Nombre), "Ya existe un seguro con ese nombre.");
                return View(model);
            }
            TempData["SuccessMessage"] = "Seguro médico creado correctamente.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id is null)
            {
                return NotFound();
            }

            var seguro = await _context.Seguros
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.SeguroId == id);

            return seguro is null ? NotFound() : View(ToViewModel(seguro));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, SeguroFormViewModel model)
        {
            if (id != model.SeguroId)
            {
                return NotFound();
            }

            var nombre = model.Nombre?.Trim() ?? string.Empty;
            await AddDuplicateErrorIfNeededAsync(nombre, id);

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var seguro = await _context.Seguros
                .SingleOrDefaultAsync(item => item.SeguroId == id);

            if (seguro is null)
            {
                return NotFound();
            }

            seguro.Nombre = nombre;
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException exception) when (IsDuplicateNombre(exception))
            {
                _context.Entry(seguro).State = EntityState.Detached;
                ModelState.AddModelError(nameof(model.Nombre), "Ya existe un seguro con ese nombre.");
                return View(model);
            }
            TempData["SuccessMessage"] = "Seguro médico actualizado correctamente.";
            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Activate(int id)
        {
            return ChangeStatusAsync(id, true);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Deactivate(int id)
        {
            return ChangeStatusAsync(id, false);
        }

        private async Task<IActionResult> ChangeStatusAsync(int id, bool state)
        {
            var seguro = await _context.Seguros.FindAsync(id);

            if (seguro is null)
            {
                return NotFound();
            }

            seguro.Estado = state;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = state
                ? "Seguro médico activado correctamente."
                : "Seguro médico desactivado correctamente.";

            return RedirectToAction(nameof(Index));
        }

        private async Task AddDuplicateErrorIfNeededAsync(string nombre, int? ignoredId = null)
        {
            if (string.IsNullOrWhiteSpace(nombre))
            {
                ModelState.AddModelError(nameof(SeguroFormViewModel.Nombre), "El nombre del seguro es obligatorio.");
                return;
            }

            var duplicateExists = await _context.Seguros
                .AnyAsync(seguro => seguro.Nombre == nombre &&
                    (!ignoredId.HasValue || seguro.SeguroId != ignoredId.Value));

            if (duplicateExists)
            {
                ModelState.AddModelError(nameof(SeguroFormViewModel.Nombre), "Ya existe un seguro con ese nombre.");
            }
        }

        private static bool IsDuplicateNombre(DbUpdateException exception)
        {
            if (exception.InnerException is Microsoft.Data.SqlClient.SqlException sql)
                return sql.Errors.Cast<Microsoft.Data.SqlClient.SqlError>().Any(error =>
                    error.Number is 2601 or 2627 &&
                    error.Message.Contains("'IX_Seguros_Nombre'", StringComparison.Ordinal));

            return exception.InnerException is Microsoft.Data.Sqlite.SqliteException
                { SqliteErrorCode: 19, SqliteExtendedErrorCode: 2067 } sqlite &&
                sqlite.Message.Contains("UNIQUE constraint failed: Seguros.Nombre'", StringComparison.Ordinal);
        }

        private static SeguroFormViewModel ToViewModel(Seguro seguro)
        {
            return new SeguroFormViewModel
            {
                SeguroId = seguro.SeguroId,
                Nombre = seguro.Nombre
            };
        }
    }
}
