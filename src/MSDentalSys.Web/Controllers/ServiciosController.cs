using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MSDentalSys.Data.Context;
using MSDentalSys.Data.Models;
using MSDentalSys.Web.Models.ViewModels;

namespace MSDentalSys.Web.Controllers
{
    [Authorize(Roles = "Administrador,Odontologo,Recepcionista")]
    public class ServiciosController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ServiciosController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? searchTerm)
        {
            var query = _context.ServiciosOdontologicos
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim();
                query = query.Where(servicio =>
                    EF.Functions.Like(servicio.Nombre, $"%{term}%") ||
                    (servicio.Descripcion != null &&
                     EF.Functions.Like(servicio.Descripcion, $"%{term}%")));
            }

            var servicios = await query
                .OrderBy(servicio => servicio.Nombre)
                .ToListAsync();

            return View(new ServiciosIndexViewModel
            {
                SearchTerm = searchTerm,
                Servicios = servicios
            });
        }

        [HttpGet]
        public async Task<IActionResult> Details(int? id)
        {
            if (id is null)
            {
                return NotFound();
            }

            var servicio = await _context.ServiciosOdontologicos
                .AsNoTracking()
                .FirstOrDefaultAsync(servicio => servicio.ServicioOdontologicoId == id);

            return servicio is null ? NotFound() : View(servicio);
        }

        [HttpGet]
        [Authorize(Roles = "Administrador")]
        public IActionResult Create()
        {
            return View(new ServicioFormViewModel());
        }

        [HttpPost]
        [Authorize(Roles = "Administrador")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ServicioFormViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            _context.ServiciosOdontologicos.Add(new ServicioOdontologico
            {
                Nombre = model.Nombre.Trim(),
                Descripcion = NullIfWhiteSpace(model.Descripcion),
                DuracionEstimadaMinutos = model.DuracionEstimadaMinutos,
                Estado = true,
                FechaCreacion = DateTime.Now
            });

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Servicio odontológico creado correctamente.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        [Authorize(Roles = "Administrador")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id is null)
            {
                return NotFound();
            }

            var servicio = await _context.ServiciosOdontologicos
                .AsNoTracking()
                .FirstOrDefaultAsync(servicio => servicio.ServicioOdontologicoId == id);

            return servicio is null ? NotFound() : View(ToViewModel(servicio));
        }

        [HttpPost]
        [Authorize(Roles = "Administrador")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, ServicioFormViewModel model)
        {
            if (id != model.ServicioOdontologicoId)
            {
                return NotFound();
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var servicio = await _context.ServiciosOdontologicos
                .FirstOrDefaultAsync(servicio => servicio.ServicioOdontologicoId == id);

            if (servicio is null)
            {
                return NotFound();
            }

            servicio.Nombre = model.Nombre.Trim();
            servicio.Descripcion = NullIfWhiteSpace(model.Descripcion);
            servicio.DuracionEstimadaMinutos = model.DuracionEstimadaMinutos;

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Servicio odontológico actualizado correctamente.";
            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [Authorize(Roles = "Administrador")]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Activate(int id)
        {
            return ChangeStatusAsync(id, true);
        }

        [HttpPost]
        [Authorize(Roles = "Administrador")]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Deactivate(int id)
        {
            return ChangeStatusAsync(id, false);
        }

        private async Task<IActionResult> ChangeStatusAsync(int id, bool state)
        {
            var servicio = await _context.ServiciosOdontologicos.FindAsync(id);

            if (servicio is null)
            {
                return NotFound();
            }

            servicio.Estado = state;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = state
                ? "Servicio odontológico activado correctamente."
                : "Servicio odontológico desactivado correctamente.";
            return RedirectToAction(nameof(Index));
        }

        private static ServicioFormViewModel ToViewModel(ServicioOdontologico servicio)
        {
            return new ServicioFormViewModel
            {
                ServicioOdontologicoId = servicio.ServicioOdontologicoId,
                Nombre = servicio.Nombre,
                Descripcion = servicio.Descripcion,
                DuracionEstimadaMinutos = servicio.DuracionEstimadaMinutos
            };
        }

        private static string? NullIfWhiteSpace(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
