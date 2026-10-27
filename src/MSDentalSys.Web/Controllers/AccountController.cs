using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MSDentalSys.Data.Models;
using MSDentalSys.Web.Models.ViewModels;
using MSDentalSys.Web.Security;

namespace MSDentalSys.Web.Controllers
{
    public class AccountController : Controller
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;

        public AccountController(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager)
        {
            _signInManager = signInManager;
            _userManager = userManager;
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Index", "Dashboard");
            }

            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [HttpGet]
        [Authorize]
        public IActionResult AccessDenied()
        {
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _userManager.FindByEmailAsync(model.Email);

            if (user is null)
            {
                ModelState.AddModelError(string.Empty, "El correo o la contraseña no son correctos.");
                return View(model);
            }

            if (!user.Estado)
            {
                ModelState.AddModelError(string.Empty, "Tu cuenta está inactiva. Contacta a la clínica.");
                return View(model);
            }

            var result = await _signInManager.PasswordSignInAsync(
                user, model.Password, model.RememberMe, lockoutOnFailure: true);

            if (result.Succeeded)
            {
                if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    return Redirect(returnUrl);
                }

                return RedirectToAction("Index", "Dashboard");
            }

            ModelState.AddModelError(
                string.Empty,
                result.IsLockedOut
                    ? "La cuenta está temporalmente bloqueada por varios intentos fallidos. Intenta nuevamente en un minuto."
                    : "El correo o la contraseña no son correctos.");

            return View(model);
        }

        [HttpGet]
        [Authorize]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> ChangePassword()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null || !user.Estado)
            {
                await _signInManager.SignOutAsync();
                return RedirectToAction(nameof(Login));
            }

            return View(new ChangePasswordViewModel());
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting(ChangePasswordRateLimit.PolicyName)]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null || !user.Estado)
            {
                await _signInManager.SignOutAsync();
                return RedirectToAction(nameof(Login));
            }

            if (ModelState.IsValid && string.Equals(model.CurrentPassword, model.NewPassword, StringComparison.Ordinal))
                ModelState.AddModelError(nameof(model.NewPassword), "La nueva contraseña debe ser diferente de la actual.");

            if (ModelState.IsValid)
            {
                var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
                if (result.Succeeded)
                {
                    // Identity ya renovó el SecurityStamp. Conservar las propiedades de la sesión actual.
                    await _signInManager.RefreshSignInAsync(user);
                    TempData["SuccessMessage"] = "Tu contraseña se cambió correctamente.";
                    return RedirectToAction(nameof(ChangePassword));
                }

                if (result.Errors.Any(error => error.Code == "ConcurrencyFailure"))
                {
                    // Volver a leer sin la entidad rastreada que Identity intentó actualizar.
                    var currentUser = await _userManager.Users.AsNoTracking()
                        .SingleOrDefaultAsync(candidate => candidate.Id == user.Id);
                    if (currentUser is null || !currentUser.Estado)
                    {
                        await _signInManager.SignOutAsync();
                        return RedirectToAction(nameof(Login));
                    }
                }

                IdentityPasswordErrors.AddErrors(ModelState, result, _userManager.Options.Password);
            }

            IdentityPasswordErrors.ClearAttemptedValues(ModelState);
            return View(new ChangePasswordViewModel());
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction(nameof(Login));
        }
    }
}
