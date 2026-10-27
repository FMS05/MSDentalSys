using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MSDentalSys.Data.Context;
using MSDentalSys.Data.Models;
using MSDentalSys.Web.Models.ViewModels;
using MSDentalSys.Web.Security;

namespace MSDentalSys.Web.Controllers
{
    [Authorize(Roles = "Administrador,Odontologo,Recepcionista")]
    public class UsuariosController : Controller
    {
        private const string AdministradorInicialEmail = "admin@msdentalsys.local";
        private const string AdministradorRole = "Administrador";
        private static readonly string[] RolesGestionables = ["Odontologo", "Recepcionista"];

        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _context;
        private readonly SignInManager<ApplicationUser>? _signInManager;
        private readonly UserProfilePhotoStore? _photoStore;

        public UsuariosController(UserManager<ApplicationUser> userManager, ApplicationDbContext context,
            SignInManager<ApplicationUser>? signInManager = null, UserProfilePhotoStore? photoStore = null)
        {
            _userManager = userManager;
            _context = context;
            _signInManager = signInManager;
            _photoStore = photoStore;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? searchTerm)
        {
            var query = _userManager.Users.AsNoTracking();
            if (!User.IsInRole(AdministradorRole))
            {
                // Aplicar visibilidad antes de buscar o proyectar cualquier dato de la cuenta.
                var adminIds = from membership in _context.UserRoles
                               join role in _context.Roles on membership.RoleId equals role.Id
                               where role.Name == AdministradorRole select membership.UserId;
                var staffIds = from membership in _context.UserRoles
                               join role in _context.Roles on membership.RoleId equals role.Id
                               where RolesGestionables.Contains(role.Name!) select membership.UserId;
                var initialEmail = _userManager.NormalizeEmail(AdministradorInicialEmail);
                query = query.Where(user => !adminIds.Contains(user.Id) && staffIds.Contains(user.Id)
                    && user.NormalizedEmail != initialEmail);
            }

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
            var userIds = users.Select(user => user.Id).ToArray();
            var roleRows = await (from membership in _context.UserRoles.AsNoTracking()
                                  join role in _context.Roles.AsNoTracking() on membership.RoleId equals role.Id
                                  where userIds.Contains(membership.UserId)
                                  select new { membership.UserId, role.Name }).ToListAsync();
            var rolesByUser = roleRows.ToLookup(row => row.UserId, row => row.Name);

            foreach (var user in users)
            {
                var roles = rolesByUser[user.Id];
                userItems.Add(new UsuarioListItemViewModel
                {
                    Id = user.Id,
                    NombreCompleto = $"{user.Nombre} {user.Apellido}".Trim(),
                    Email = user.Email ?? user.UserName ?? string.Empty,
                    Telefono = user.PhoneNumber,
                    Rol = roles.FirstOrDefault() ?? "Sin rol",
                    Estado = user.Estado,
                    EsAdministradorInicial = IsAdministradorInicial(user),
                    PuedeEditar = User.IsInRole(AdministradorRole) || user.Id == _userManager.GetUserId(User),
                    PuedeCambiarEstado = User.IsInRole(AdministradorRole) && !IsAdministradorInicial(user)
                });
            }

            return View(new UsuariosIndexViewModel
            {
                SearchTerm = searchTerm,
                Usuarios = userItems
            });
        }

        [HttpGet]
        [Authorize(Roles = "Administrador")]
        public IActionResult Create()
        {
            return View(new UsuarioCreateViewModel());
        }

        [HttpPost]
        [Authorize(Roles = "Administrador")]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(3 * 1024 * 1024)]
        public async Task<IActionResult> Create(UsuarioCreateViewModel model)
        {
            if (!RolesGestionables.Contains(model.Rol))
            {
                ModelState.AddModelError(nameof(model.Rol), "El rol seleccionado no es válido.");
            }

            model.Cedula = await ValidateCedulaAsync(model.Cedula, null, required: true);
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var normalizedEmail = model.Email.Trim();
            if (!string.IsNullOrWhiteSpace(normalizedEmail) &&
                await _userManager.FindByEmailAsync(normalizedEmail) is not null)
            {
                ModelState.AddModelError(nameof(model.Email), "Ya existe un usuario con ese correo electrónico.");
                return View(model);
            }

            var user = new ApplicationUser
            {
                UserName = normalizedEmail,
                Email = normalizedEmail,
                Nombre = model.Nombre.Trim(),
                Apellido = model.Apellido.Trim(),
                Cedula = model.Cedula,
                PhoneNumber = NullIfWhiteSpace(model.Telefono),
                Estado = true,
                FechaCreacion = DateTime.Now
            };

            await using var transaction = await _context.Database.BeginTransactionAsync();
            var committed = false;
            string? newPhoto = null;
            try
            {
                if (model.FotoArchivo is not null)
                {
                    var photo = await _photoStore!.ValidateAndSaveAsync(model.FotoArchivo, HttpContext.RequestAborted);
                    if (photo.Error is not null)
                    {
                        ModelState.AddModelError(nameof(model.FotoArchivo), photo.Error);
                        return View(model);
                    }
                    user.FotoPerfil = newPhoto = photo.FileName;
                }
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

                await transaction.CommitAsync();
                committed = true;
            }
            catch (DbUpdateException exception) when (IsCedulaUniqueViolation(exception))
            {
                ModelState.AddModelError(nameof(model.Cedula), "La cédula ya pertenece a otro usuario.");
                return View(model);
            }
            finally
            {
                if (!committed)
                {
                    try
                    {
                        await transaction.RollbackAsync();
                    }
                    finally
                    {
                        // El rollback no restaura las entidades ni las asociaciones rastreadas.
                        _context.ChangeTracker.Clear();
                        _photoStore?.DeleteOwned(newPhoto);
                    }
                }
            }

            TempData["SuccessMessage"] = "Usuario registrado correctamente.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Details([FromRoute] string? id)
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
            if (!CanView(user, roles)) return NotFound();
            return View(await ToDetailsViewModelAsync(user));
        }

        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> Photo([FromRoute] string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return NotFound();
            var user = await _userManager.FindByIdAsync(id);
            if (user is null || !CanView(user, await _userManager.GetRolesAsync(user))) return NotFound();
            var photo = await _photoStore!.TryReadAsync(user.FotoPerfil, HttpContext.RequestAborted);
            if (photo is null) return NotFound();
            Response.Headers.XContentTypeOptions = "nosniff";
            return File(photo.Value.Content, photo.Value.ContentType);
        }

        [HttpGet]
        [Authorize(Roles = "Administrador")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> ResetPassword([FromRoute] string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return NotFound();

            var user = await _userManager.FindByIdAsync(id);
            if (user is null) return NotFound();
            if (!CanResetPassword(user, await _userManager.GetRolesAsync(user))) return Forbid();

            return View(ToResetPasswordViewModel(user));
        }

        [HttpPost]
        [Authorize(Roles = "Administrador")]
        [ValidateAntiForgeryToken]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> ResetPassword([FromRoute] string? id, ResetPasswordViewModel model)
        {
            if (string.IsNullOrWhiteSpace(id)) return NotFound();

            // El objetivo y sus roles se consultan de nuevo; no proceden del formulario.
            var user = await _userManager.FindByIdAsync(id);
            if (user is null) return NotFound();
            if (!CanResetPassword(user, await _userManager.GetRolesAsync(user))) return Forbid();

            if (!ModelState.IsValid) return ResetPasswordForm(user);

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, model.NewPassword);
            if (!result.Succeeded)
            {
                IdentityPasswordErrors.AddErrors(ModelState, result, _userManager.Options.Password);
                // Identity aplica concurrencia optimista: no sobrescribe una cuenta que cambió.
                // Descartar también las modificaciones que quedaron únicamente en memoria.
                _context.ChangeTracker.Clear();
                var currentUser = await _userManager.FindByIdAsync(id);
                if (currentUser is null) return NotFound();
                if (!CanResetPassword(currentUser, await _userManager.GetRolesAsync(currentUser))) return Forbid();
                return ResetPasswordForm(currentUser);
            }

            // ResetPasswordAsync ya renueva el SecurityStamp del objetivo. La sesión del actor
            // no necesita RefreshSignInAsync ni SignOutAsync y Estado se conserva sin cambios.
            TempData["SuccessMessage"] = user.Estado
                ? "Contraseña restablecida correctamente. El usuario deberá utilizar la nueva contraseña."
                : "Contraseña restablecida correctamente. La cuenta continúa inactiva.";
            return RedirectToAction(nameof(Details), new { id = user.Id });
        }

        private ViewResult ResetPasswordForm(ApplicationUser user)
        {
            // Conservar errores de validación sin repoblar secretos en el modelo ni ModelState.
            IdentityPasswordErrors.ClearAttemptedValues(ModelState);
            return View(nameof(ResetPassword), ToResetPasswordViewModel(user));
        }

        private static ResetPasswordViewModel ToResetPasswordViewModel(ApplicationUser user) => new()
        {
            Id = user.Id,
            NombreCompleto = $"{user.Nombre} {user.Apellido}".Trim(),
            Email = user.Email ?? user.UserName ?? string.Empty,
            Estado = user.Estado
        };

        private bool CanResetPassword(ApplicationUser user, IEnumerable<string> roles) =>
            User.IsInRole(AdministradorRole) &&
            user.Id != _userManager.GetUserId(User) &&
            !roles.Contains(AdministradorRole) && roles.Any(role => RolesGestionables.Contains(role));

        [HttpGet]
        public async Task<IActionResult> Edit([FromRoute] string? id)
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
            if (!CanView(user, roles)) return NotFound();
            if (!CanEdit(user)) return Forbid();
            return View(new UsuarioEditViewModel
            {
                Id = user.Id,
                Email = user.Email ?? user.UserName ?? string.Empty,
                Nombre = user.Nombre,
                Apellido = user.Apellido,
                Telefono = user.PhoneNumber,
                Cedula = user.Cedula,
                FotoPerfil = user.FotoPerfil,
                Rol = roles.Contains(AdministradorRole) ? AdministradorRole : roles.FirstOrDefault() ?? string.Empty,
                EsAdministradorInicial = IsAdministradorInicial(user),
                PuedeEditarDatosAdministrativos = User.IsInRole(AdministradorRole),
                PuedeEditarRol = User.IsInRole(AdministradorRole) && !roles.Contains(AdministradorRole)
                    && !IsAdministradorInicial(user)
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(3 * 1024 * 1024)]
        public async Task<IActionResult> Edit([FromRoute] string id, UsuarioEditViewModel model)
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
            if (!CanView(user, currentRoles)) return NotFound();
            if (!CanEdit(user)) return Forbid();

            // Los permisos y valores no editables provienen de Identity, nunca del POST.
            model.EsAdministradorInicial = isProtectedAdmin;
            model.PuedeEditarDatosAdministrativos = User.IsInRole(AdministradorRole);
            model.PuedeEditarRol = model.PuedeEditarDatosAdministrativos && !isProtectedAdmin
                && !currentRoles.Contains(AdministradorRole);
            model.FotoPerfil = user.FotoPerfil;
            ModelState.Remove(nameof(model.EsAdministradorInicial));
            ModelState.Remove(nameof(model.PuedeEditarDatosAdministrativos));
            ModelState.Remove(nameof(model.PuedeEditarRol));
            ModelState.Remove(nameof(model.FotoPerfil));

            if (!model.PuedeEditarDatosAdministrativos)
            {
                model.Nombre = user.Nombre;
                model.Apellido = user.Apellido;
                model.Cedula = user.Cedula;
                model.Rol = currentRoles.FirstOrDefault() ?? string.Empty;
                foreach (var field in new[] { nameof(model.Nombre), nameof(model.Apellido), nameof(model.Cedula), nameof(model.Rol) })
                    ModelState.Remove(field);
            }

            if (isProtectedAdmin)
            {
                var attemptedEmail = model.Email;
                model.Email = user.Email ?? user.UserName ?? string.Empty;
                ModelState.Remove(nameof(model.Email));
                if (!string.Equals(attemptedEmail, model.Email, StringComparison.Ordinal))
                {
                    ModelState.AddModelError(nameof(model.Email), "El correo del Administrador inicial no se puede modificar.");
                }
            }

            if (model.PuedeEditarDatosAdministrativos)
            {
                if (!model.PuedeEditarRol && model.Rol != AdministradorRole)
                {
                    ModelState.AddModelError(nameof(model.Rol), isProtectedAdmin
                        ? "No se puede cambiar el rol del administrador inicial."
                        : "No se puede cambiar el rol Administrador desde este módulo.");
                }
                else if (model.PuedeEditarRol && !RolesGestionables.Contains(model.Rol))
                {
                    ModelState.AddModelError(nameof(model.Rol), "Solo se permiten los roles Odontologo y Recepcionista.");
                }
                model.Cedula = await ValidateCedulaAsync(model.Cedula, user.Id, required: false);
            }

            var email = model.Email?.Trim() ?? string.Empty;
            if (!isProtectedAdmin && ModelState.IsValid)
            {
                var emailOwner = await _userManager.FindByEmailAsync(email);
                var nameOwner = await _userManager.FindByNameAsync(email);
                if ((emailOwner is not null && emailOwner.Id != user.Id) ||
                    (nameOwner is not null && nameOwner.Id != user.Id))
                    ModelState.AddModelError(nameof(model.Email), "Ya existe un usuario con ese correo electrónico.");
            }
            if (model.EliminarFoto && model.FotoArchivo is not null)
                ModelState.AddModelError(nameof(model.FotoArchivo), "Elige una foto nueva o elimina la actual, no ambas acciones a la vez.");
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var oldPhoto = user.FotoPerfil;
            string? newPhoto = null;
            var emailChanged = !isProtectedAdmin && !string.Equals(user.Email, email, StringComparison.Ordinal);
            await using var transaction = await _context.Database.BeginTransactionAsync();
            var committed = false;
            try
            {
                if (model.FotoArchivo is not null)
                {
                    var photo = await _photoStore!.ValidateAndSaveAsync(model.FotoArchivo, HttpContext.RequestAborted);
                    if (photo.Error is not null)
                    {
                        ModelState.AddModelError(nameof(model.FotoArchivo), photo.Error);
                        return View(model);
                    }
                    user.FotoPerfil = newPhoto = photo.FileName;
                }
                else if (model.EliminarFoto) user.FotoPerfil = null;

                if (model.PuedeEditarDatosAdministrativos)
                {
                    user.Nombre = model.Nombre.Trim();
                    user.Apellido = model.Apellido.Trim();
                    user.Cedula = model.Cedula;
                }
                var phone = NullIfWhiteSpace(model.Telefono);
                if (user.PhoneNumber != phone) user.PhoneNumberConfirmed = false;
                user.PhoneNumber = phone;

                // Las dos APIs validan y normalizan mediante Identity. La transacción también
                // revierte el primer cambio si falla el segundo o cualquier operación posterior.
                if (emailChanged)
                {
                    var emailResult = await _userManager.SetEmailAsync(user, email);
                    if (!emailResult.Succeeded)
                    {
                        AddIdentityErrors(emailResult, nameof(model.Email));
                        return View(model);
                    }
                }
                if (!isProtectedAdmin && !string.Equals(user.UserName, email, StringComparison.Ordinal))
                {
                    var nameResult = await _userManager.SetUserNameAsync(user, email);
                    if (!nameResult.Succeeded)
                    {
                        AddIdentityErrors(nameResult, nameof(model.Email));
                        return View(model);
                    }
                }
                var updateResult = await _userManager.UpdateAsync(user);
                if (!updateResult.Succeeded)
                {
                    AddIdentityErrors(updateResult);
                    return View(model);
                }

                if (model.PuedeEditarRol)
                {
                    var rolesToRemove = currentRoles
                        .Where(role => RolesGestionables.Contains(role) && role != model.Rol)
                        .ToList();
                    var roleChanged = !currentRoles.Contains(model.Rol) || rolesToRemove.Count > 0;

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

                    if (roleChanged)
                    {
                        var stampResult = await _userManager.UpdateSecurityStampAsync(user);
                        if (!stampResult.Succeeded)
                        {
                            AddIdentityErrors(stampResult);
                            return View(model);
                        }
                    }
                }

                await transaction.CommitAsync();
                committed = true;
            }
            catch (DbUpdateException exception) when (IsCedulaUniqueViolation(exception))
            {
                ModelState.AddModelError(nameof(model.Cedula), "La cédula ya pertenece a otro usuario.");
                return View(model);
            }
            catch (DbUpdateException exception) when (IsUserNameUniqueViolation(exception))
            {
                ModelState.AddModelError(nameof(model.Email), "Ya existe un usuario con ese correo electrónico.");
                return View(model);
            }
            finally
            {
                if (!committed)
                {
                    try
                    {
                        await transaction.RollbackAsync();
                    }
                    finally
                    {
                        // El rollback no restaura las entidades ni las asociaciones rastreadas.
                        _context.ChangeTracker.Clear();
                        _photoStore?.DeleteOwned(newPhoto);
                    }
                }
            }

            if (oldPhoto != user.FotoPerfil) _photoStore?.DeleteOwned(oldPhoto);
            if (user.Id == _userManager.GetUserId(User) && _signInManager is not null)
                await _signInManager.RefreshSignInAsync(user);
            TempData["SuccessMessage"] = "Usuario actualizado correctamente.";
            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [Authorize(Roles = "Administrador")]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Activate(string id)
        {
            return ChangeStatusAsync(id, true);
        }

        [HttpPost]
        [Authorize(Roles = "Administrador")]
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

            var isDeactivation = user.Estado && !state;
            user.Estado = state;
            var result = isDeactivation
                ? await _userManager.UpdateSecurityStampAsync(user)
                : await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                TempData["ErrorMessage"] = "No se pudo actualizar el estado del usuario.";
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
                Cedula = user.Cedula,
                FotoPerfil = user.FotoPerfil,
                Rol = roles.FirstOrDefault() ?? "Sin rol",
                Estado = user.Estado,
                FechaCreacion = user.FechaCreacion,
                EsAdministradorInicial = IsAdministradorInicial(user),
                PuedeRestablecerPassword = CanResetPassword(user, roles),
                PuedeEditar = CanEdit(user),
                EsPerfilPropio = user.Id == _userManager.GetUserId(User),
                PuedeCambiarEstado = User.IsInRole(AdministradorRole) && !IsAdministradorInicial(user)
            };
        }

        private bool CanView(ApplicationUser user, IEnumerable<string> roles) =>
            User.IsInRole(AdministradorRole) || (!IsAdministradorInicial(user)
                && !roles.Contains(AdministradorRole) && roles.Any(role => RolesGestionables.Contains(role)));

        private bool CanEdit(ApplicationUser user) =>
            User.IsInRole(AdministradorRole) || user.Id == _userManager.GetUserId(User);

        private async Task<string?> ValidateCedulaAsync(string? value, string? userId, bool required)
        {
            var cedula = NullIfWhiteSpace(value);
            if (cedula is null)
            {
                if (required) ModelState.AddModelError("Cedula", "La cédula es obligatoria para registrar un usuario.");
                return null;
            }
            // Misma regla de formato que Pacientes; no se valida dígito verificador.
            if (!System.Text.RegularExpressions.Regex.IsMatch(cedula, @"\A(?:[0-9]{11}|[0-9]{3}-[0-9]{7}-[0-9])\z"))
            {
                ModelState.AddModelError("Cedula", "Ingresa una cédula completa de 11 dígitos con formato XXX-XXXXXXX-X.");
                return value;
            }
            var digits = cedula.Replace("-", "");
            var normalized = $"{digits[..3]}-{digits[3..10]}-{digits[10]}";
            // Incluye inactivos y formatos históricos. La tabla Pacientes es independiente.
            if (await _context.Users.AnyAsync(user => user.Id != userId
                && (user.Cedula == normalized || user.Cedula == digits)))
                ModelState.AddModelError("Cedula", "La cédula ya pertenece a otro usuario.");
            return normalized;
        }

        private static bool IsCedulaUniqueViolation(DbUpdateException exception) =>
            IsUniqueViolation(exception, "IX_AspNetUsers_Cedula", "AspNetUsers.Cedula");

        private static bool IsUserNameUniqueViolation(DbUpdateException exception) =>
            IsUniqueViolation(exception, "UserNameIndex", "AspNetUsers.NormalizedUserName");

        private static bool IsUniqueViolation(DbUpdateException exception, string indexName, string sqliteColumn)
        {
            if (exception.InnerException is Microsoft.Data.SqlClient.SqlException sql)
                return sql.Errors.Cast<Microsoft.Data.SqlClient.SqlError>().Any(error =>
                    error.Number is 2601 or 2627 && error.Message.Contains(indexName, StringComparison.Ordinal));
            return exception.InnerException is Microsoft.Data.Sqlite.SqliteException
                { SqliteErrorCode: 19, SqliteExtendedErrorCode: 2067 } sqlite &&
                sqlite.Message.Contains($"UNIQUE constraint failed: {sqliteColumn}'", StringComparison.Ordinal);
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
