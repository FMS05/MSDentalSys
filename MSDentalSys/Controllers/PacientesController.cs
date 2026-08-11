using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MSDentalSys.Data;
using MSDentalSys.Models;

namespace MSDentalSys.Controllers
{
    [Authorize(Roles = "Administrador,Odontologo,Recepcionista")]
    public class PacientesController : Controller
    {
        private readonly ApplicationDbContext _context;

        public PacientesController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? searchTerm)
        {
            var query = _context.Pacientes
                .Include(p => p.AntecedenteClinico)
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim();
                query = query.Where(p =>
                    EF.Functions.Like(p.Nombre, $"%{term}%") ||
                    EF.Functions.Like(p.Apellido, $"%{term}%") ||
                    (p.Cedula != null && EF.Functions.Like(p.Cedula, $"%{term}%")) ||
                    (p.Telefono != null && EF.Functions.Like(p.Telefono, $"%{term}%")));
            }

            var pacientes = await query
                .OrderBy(p => p.Apellido)
                .ThenBy(p => p.Nombre)
                .ToListAsync();

            ViewData["SearchTerm"] = searchTerm;
            return View(pacientes);
        }

        [HttpGet]
        public async Task<IActionResult> Details(int? id)
        {
            if (id is null)
            {
                return NotFound();
            }

            var paciente = await _context.Pacientes
                .Include(p => p.AntecedenteClinico)
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.PacienteId == id);

            return paciente is null ? NotFound() : View(paciente);
        }

        [HttpGet]
        [Authorize(Roles = "Administrador,Recepcionista")]
        public IActionResult Create()
        {
            return View(new PacienteFormViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Administrador,Recepcionista")]
        public async Task<IActionResult> Create(PacienteFormViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            if (await CedulaExistsAsync(model.Cedula, null))
            {
                ModelState.AddModelError(nameof(model.Cedula), "La cédula ya pertenece a otro paciente.");
                return View(model);
            }

            var paciente = new Paciente
            {
                Nombre = model.Nombre.Trim(),
                Apellido = model.Apellido.Trim(),
                Cedula = NullIfWhiteSpace(model.Cedula),
                FechaNacimiento = model.FechaNacimiento,
                Sexo = NullIfWhiteSpace(model.Sexo),
                Telefono = NullIfWhiteSpace(model.Telefono),
                Correo = NullIfWhiteSpace(model.Correo),
                Direccion = NullIfWhiteSpace(model.Direccion),
                ContactoEmergencia = NullIfWhiteSpace(model.ContactoEmergencia),
                TelefonoEmergencia = NullIfWhiteSpace(model.TelefonoEmergencia),
                Estado = true,
                FechaRegistro = DateTime.Now,
                AntecedenteClinico = new AntecedenteClinico
                {
                    Alergias = NullIfWhiteSpace(model.Alergias),
                    EnfermedadesSistemicas = NullIfWhiteSpace(model.EnfermedadesSistemicas),
                    MedicamentosActuales = NullIfWhiteSpace(model.MedicamentosActuales),
                    CirugiasPrevias = NullIfWhiteSpace(model.CirugiasPrevias),
                    HabitosRelevantes = NullIfWhiteSpace(model.HabitosRelevantes),
                    Embarazo = model.Embarazo,
                    Observaciones = NullIfWhiteSpace(model.Observaciones),
                    FechaActualizacion = DateTime.Now
                }
            };

            _context.Pacientes.Add(paciente);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                ModelState.AddModelError(nameof(model.Cedula), "La cédula ya pertenece a otro paciente.");
                return View(model);
            }

            TempData["SuccessMessage"] = "Paciente registrado correctamente.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id is null)
            {
                return NotFound();
            }

            var paciente = await _context.Pacientes
                .Include(p => p.AntecedenteClinico)
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.PacienteId == id);

            return paciente is null ? NotFound() : View(ToFormViewModel(paciente));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, PacienteFormViewModel model)
        {
            if (id != model.PacienteId)
            {
                return NotFound();
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            if (await CedulaExistsAsync(model.Cedula, id))
            {
                ModelState.AddModelError(nameof(model.Cedula), "La cédula ya pertenece a otro paciente.");
                return View(model);
            }

            var paciente = await _context.Pacientes
                .Include(p => p.AntecedenteClinico)
                .FirstOrDefaultAsync(p => p.PacienteId == id);

            if (paciente is null)
            {
                return NotFound();
            }

            paciente.Nombre = model.Nombre.Trim();
            paciente.Apellido = model.Apellido.Trim();
            paciente.Cedula = NullIfWhiteSpace(model.Cedula);
            paciente.FechaNacimiento = model.FechaNacimiento;
            paciente.Sexo = NullIfWhiteSpace(model.Sexo);
            paciente.Telefono = NullIfWhiteSpace(model.Telefono);
            paciente.Correo = NullIfWhiteSpace(model.Correo);
            paciente.Direccion = NullIfWhiteSpace(model.Direccion);
            paciente.ContactoEmergencia = NullIfWhiteSpace(model.ContactoEmergencia);
            paciente.TelefonoEmergencia = NullIfWhiteSpace(model.TelefonoEmergencia);

            paciente.AntecedenteClinico ??= new AntecedenteClinico
            {
                PacienteId = paciente.PacienteId
            };

            paciente.AntecedenteClinico.Alergias = NullIfWhiteSpace(model.Alergias);
            paciente.AntecedenteClinico.EnfermedadesSistemicas = NullIfWhiteSpace(model.EnfermedadesSistemicas);
            paciente.AntecedenteClinico.MedicamentosActuales = NullIfWhiteSpace(model.MedicamentosActuales);
            paciente.AntecedenteClinico.CirugiasPrevias = NullIfWhiteSpace(model.CirugiasPrevias);
            paciente.AntecedenteClinico.HabitosRelevantes = NullIfWhiteSpace(model.HabitosRelevantes);
            paciente.AntecedenteClinico.Embarazo = model.Embarazo;
            paciente.AntecedenteClinico.Observaciones = NullIfWhiteSpace(model.Observaciones);
            paciente.AntecedenteClinico.FechaActualizacion = DateTime.Now;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                ModelState.AddModelError(nameof(model.Cedula), "La cédula ya pertenece a otro paciente.");
                return View(model);
            }

            TempData["SuccessMessage"] = "Paciente actualizado correctamente.";
            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [Authorize(Roles = "Administrador")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Deactivate(int id)
        {
            var paciente = await _context.Pacientes.FindAsync(id);

            if (paciente is null)
            {
                return NotFound();
            }

            paciente.Estado = false;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Paciente desactivado correctamente.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [Authorize(Roles = "Administrador")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Activate(int id)
        {
            var paciente = await _context.Pacientes.FindAsync(id);

            if (paciente is null)
            {
                return NotFound();
            }

            paciente.Estado = true;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Paciente activado correctamente.";
            return RedirectToAction(nameof(Index));
        }

        private async Task<bool> CedulaExistsAsync(string? cedula, int? pacienteId)
        {
            var normalizedCedula = NullIfWhiteSpace(cedula);

            if (normalizedCedula is null)
            {
                return false;
            }

            return await _context.Pacientes.AnyAsync(p =>
                p.Cedula == normalizedCedula &&
                (!pacienteId.HasValue || p.PacienteId != pacienteId.Value));
        }

        private static string? NullIfWhiteSpace(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static PacienteFormViewModel ToFormViewModel(Paciente paciente)
        {
            return new PacienteFormViewModel
            {
                PacienteId = paciente.PacienteId,
                Nombre = paciente.Nombre,
                Apellido = paciente.Apellido,
                Cedula = paciente.Cedula,
                FechaNacimiento = paciente.FechaNacimiento,
                Sexo = paciente.Sexo,
                Telefono = paciente.Telefono,
                Correo = paciente.Correo,
                Direccion = paciente.Direccion,
                ContactoEmergencia = paciente.ContactoEmergencia,
                TelefonoEmergencia = paciente.TelefonoEmergencia,
                Alergias = paciente.AntecedenteClinico?.Alergias,
                EnfermedadesSistemicas = paciente.AntecedenteClinico?.EnfermedadesSistemicas,
                MedicamentosActuales = paciente.AntecedenteClinico?.MedicamentosActuales,
                CirugiasPrevias = paciente.AntecedenteClinico?.CirugiasPrevias,
                HabitosRelevantes = paciente.AntecedenteClinico?.HabitosRelevantes,
                Embarazo = paciente.AntecedenteClinico?.Embarazo,
                Observaciones = paciente.AntecedenteClinico?.Observaciones
            };
        }
    }
}
