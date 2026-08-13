using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MSDentalSys.Data.Context;
using MSDentalSys.Data.Models;
using MSDentalSys.Web.Models.ViewModels;

namespace MSDentalSys.Web.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _context;

        public DashboardController(
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext context)
        {
            _userManager = userManager;
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);

            if (user is null)
            {
                return Challenge();
            }

            var roles = await _userManager.GetRolesAsync(user);
            var nombreCompleto = $"{user.Nombre} {user.Apellido}".Trim();
            var esOdontologo = roles.Contains("Odontologo");
            var hoy = DateTime.Today;
            var manana = hoy.AddDays(1);
            var citasQuery = _context.Citas.AsNoTracking().AsQueryable();

            if (esOdontologo)
            {
                citasQuery = citasQuery.Where(c => c.OdontologoId == user.Id);
            }

            var pacientesRegistrados = await _context.Pacientes
                .CountAsync(p => p.Estado);
            var citasDelDia = await citasQuery
                .CountAsync(c => c.FechaHoraInicio >= hoy && c.FechaHoraInicio < manana);
            var citasPendientes = await citasQuery
                .CountAsync(c => c.EstadoCita == "Pendiente" || c.EstadoCita == "Confirmada");
            var atencionesRealizadas = await citasQuery
                .CountAsync(c => c.EstadoCita == "Atendida");

            return View(new DashboardViewModel
            {
                NombreCompleto = string.IsNullOrWhiteSpace(nombreCompleto)
                    ? user.Email ?? user.UserName ?? "Usuario"
                    : nombreCompleto,
                Rol = roles.FirstOrDefault() ?? "Usuario",
                PacientesRegistrados = pacientesRegistrados,
                CitasDelDia = citasDelDia,
                CitasPendientes = citasPendientes,
                AtencionesRealizadas = atencionesRealizadas
            });
        }
    }
}
