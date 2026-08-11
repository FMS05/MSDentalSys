using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MSDentalSys.Models;

namespace MSDentalSys.Controllers
{
    [Authorize(Roles = "Administrador")]
    public class UsuariosController : Controller
    {
        private const string AdministradorInicialEmail = "admin@msdentalsys.local";
        private const string AdministradorRole = "Administrador";
        private static readonly string[] RolesGestionables = ["Odontologo", "Recepcionista"];

        private readonly UserManager<ApplicationUser> _userManager;

        public UsuariosController(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? searchTerm)
        {
            var query = _userManager.Users.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim();
                query = query.Where(user =>
                    EF.Functions.Like(user.Nombre, $"%{term}%") ||
                    EF.Functions.Like(user.Apellido, $"%{term}%") ||
                    (user.Email != null && EF.Functions.Like(user.Email, $"%{term}%")));
            }

            var users = await query
                .OrderBy(user => user.Apellido)
                .ThenBy(user => user.Nombre)
                .ToListAsync();

            var userItems = new List<UsuarioListItemViewModel>(users.Count);

            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);
                userItems.Add(new UsuarioListItemViewModel
                {
                    Id = user.Id,
                    NombreCompleto = $"{user.Nombre} {user.Apellido}".Trim(),
                    Email = user.Email ?? user.UserName ?? string.Empty,
                    Telefono = user.PhoneNumber,
                    Rol = roles.FirstOrDefault() ?? "Sin rol",
                    Estado = user.Estado,
                    EsAdministradorInicial = IsAdministradorInicial(user)
                });
            }

            return View(new UsuariosIndexViewModel
            {
                SearchTerm = searchTerm,
                Usuarios = userItems
            });
        }

        [HttpGet]
        public IActionResult Create()
        {
            return View(new UsuarioCreateViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(UsuarioCreateViewModel model)
        {
            if (!RolesGestionables.Contains(model.Rol))
            {
                ModelState.AddModelError(nameof(model.Rol), "El rol seleccionado no es válido.");
            }

            var normalizedEmail = model.Email.Trim();
            if (!string.IsNullOrWhiteSpace(normalizedEmail) &&
                await _userManager.FindByEmailAsync(normalizedEmail) is not null)
            {
                ModelState.AddModelError(nameof(model.Email), "Ya existe un usuario con ese correo electrónico.");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = new ApplicationUser
            {
                UserName = normalizedEmail,
                Email = normalizedEmail,
                Nombre = model.Nombre.Trim(),
                Apellido = model.Apellido.Trim(),
                PhoneNumber = NullIfWhiteSpace(model.Telefono),
                Estado = true,
                FechaCreacion = DateTime.Now
            };

            var createResult = await _userManager.CreateAsync(user, model.Password);

            if (!createResult.Succeeded)
            {
                AddIdentityErrors(createResult, nameof(model.Password));
                return View(model);
            }

            var roleResult = await _userManager.AddToRoleAsync(user, model.Rol);

            if (!roleResult.Succeeded)
            {
                AddIdentityErrors(roleResult, nameof(model.Rol));
                return View(model);
            }

            TempData["SuccessMessage"] = "Usuario registrado correctamente.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Details(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return NotFound();
            }

            var user = await _userManager.FindByIdAsync(id);
            if (user is null)
            {
                return NotFound();
            }

            return View(await ToDetailsViewModelAsync(user));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return NotFound();
            }

            var user = await _userManager.FindByIdAsync(id);
            if (user is null)
            {
                return NotFound();
            }

            var roles = await _userManager.GetRolesAsync(user);
            return View(new UsuarioEditViewModel
            {
                Id = user.Id,
                Email = user.Email ?? user.UserName ?? string.Empty,
                Nombre = user.Nombre,
                Apellido = user.Apellido,
                Telefono = user.PhoneNumber,
                Rol = roles.FirstOrDefault() ?? string.Empty,
                EsAdministradorInicial = IsAdministradorInicial(user)
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string id, UsuarioEditViewModel model)
        {
            if (id != model.Id)
            {
                return NotFound();
            }

            var user = await _userManager.FindByIdAsync(id);
            if (user is null)
            {
                return NotFound();
            }

            var isProtectedAdmin = IsAdministradorInicial(user);
            var currentRoles = await _userManager.GetRolesAsync(user);

            if (isProtectedAdmin)
            {
                if (model.Rol != AdministradorRole)
                {
                    ModelState.AddModelError(nameof(model.Rol), "No se puede cambiar el rol del administrador inicial.");
                }
            }
            else
            {
                if (!RolesGestionables.Contains(model.Rol))
                {
                    ModelState.AddModelError(nameof(model.Rol), "Solo se permiten los roles Odontologo y Recepcionista.");
                }

                if (currentRoles.Contains(AdministradorRole))
                {
                    ModelState.AddModelError(nameof(model.Rol), "No se puede gestionar un usuario con rol Administrador desde este módulo.");
                }
            }

            if (!ModelState.IsValid)
            {
                model.Email = user.Email ?? user.UserName ?? string.Empty;
                model.EsAdministradorInicial = isProtectedAdmin;
                return View(model);
            }

            user.Nombre = model.Nombre.Trim();
            user.Apellido = model.Apellido.Trim();
            user.PhoneNumber = NullIfWhiteSpace(model.Telefono);

            var updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                AddIdentityErrors(updateResult);
                return View(model);
            }

            if (!isProtectedAdmin)
            {
                var rolesToRemove = currentRoles
                    .Where(role => RolesGestionables.Contains(role) && role != model.Rol)
                    .ToList();

                if (!currentRoles.Contains(model.Rol))
                {
                    var addResult = await _userManager.AddToRoleAsync(user, model.Rol);
                    if (!addResult.Succeeded)
                    {
                        AddIdentityErrors(addResult, nameof(model.Rol));
                        return View(model);
                    }
                }

                if (rolesToRemove.Count > 0)
                {
                    var removeResult = await _userManager.RemoveFromRolesAsync(user, rolesToRemove);
                    if (!removeResult.Succeeded)
                    {
                        AddIdentityErrors(removeResult, nameof(model.Rol));
                        return View(model);
                    }
                }
            }

            TempData["SuccessMessage"] = "Usuario actualizado correctamente.";
            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Activate(string id)
        {
            return ChangeStatusAsync(id, true);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Deactivate(string id)
        {
            return ChangeStatusAsync(id, false);
        }

        private async Task<IActionResult> ChangeStatusAsync(string id, bool state)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user is null)
            {
                return NotFound();
            }

            if (IsAdministradorInicial(user))
            {
                TempData["ErrorMessage"] = "El administrador inicial no puede ser desactivado.";
                return RedirectToAction(nameof(Details), new { id });
            }

            user.Estado = state;
            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                AddIdentityErrors(result);
                return RedirectToAction(nameof(Details), new { id });
            }

            TempData["SuccessMessage"] = state
                ? "Usuario activado correctamente."
                : "Usuario desactivado correctamente.";
            return RedirectToAction(nameof(Index));
        }

        private async Task<UsuarioDetailsViewModel> ToDetailsViewModelAsync(ApplicationUser user)
        {
            var roles = await _userManager.GetRolesAsync(user);
            return new UsuarioDetailsViewModel
            {
                Id = user.Id,
                NombreCompleto = $"{user.Nombre} {user.Apellido}".Trim(),
                Email = user.Email ?? user.UserName ?? string.Empty,
                Telefono = user.PhoneNumber,
                Rol = roles.FirstOrDefault() ?? "Sin rol",
                Estado = user.Estado,
                FechaCreacion = user.FechaCreacion,
                EsAdministradorInicial = IsAdministradorInicial(user)
            };
        }

        private static bool IsAdministradorInicial(ApplicationUser user)
        {
            return string.Equals(user.Email, AdministradorInicialEmail, StringComparison.OrdinalIgnoreCase);
        }

        private void AddIdentityErrors(IdentityResult result, string? key = null)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(key ?? string.Empty, error.Description);
            }
        }

        private static string? NullIfWhiteSpace(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
