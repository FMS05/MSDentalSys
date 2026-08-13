using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MSDentalSys.Data.Context;
using MSDentalSys.Data.Models;
using MSDentalSys.Web.Controllers;
using MSDentalSys.Web.Models.ViewModels;
using Xunit;

namespace MSDentalSys.Tests.Controllers;

public class DashboardControllerTests
{
    [Fact]
    public async Task Index_Administrador_CuentaPacientesActivos()
    {
        await using var database = await TestDatabase.CreateAsync();
        var administrator = await database.CreateUserAsync("admin.dashboard@example.test", "Administrador", "Admin", "Prueba");
        await database.AddPatientAsync(true);
        await database.AddPatientAsync(true);
        await database.AddPatientAsync(false);

        var model = await database.ExecuteDashboardAsync(administrator);

        Assert.Equal(2, model.PacientesRegistrados);
    }

    [Fact]
    public async Task Index_Administrador_CuentaSoloCitasPendientesYConfirmadas()
    {
        await using var database = await TestDatabase.CreateAsync();
        var administrator = await database.CreateUserAsync("admin.pendientes@example.test", "Administrador", "Admin", "Prueba");
        var odontologist = await database.CreateUserAsync("odontologo.pendientes@example.test", "Odontologo", "Odontologo", "Prueba");
        var patientId = await database.AddPatientAsync(true);
        var serviceId = await database.AddServiceAsync();
        var baseDate = DateTime.Today.AddDays(2);
        await database.AddAppointmentAsync(patientId, odontologist.Id, serviceId, baseDate.AddHours(8), "Pendiente");
        await database.AddAppointmentAsync(patientId, odontologist.Id, serviceId, baseDate.AddHours(9), "Confirmada");
        await database.AddAppointmentAsync(patientId, odontologist.Id, serviceId, baseDate.AddHours(10), "Atendida");
        await database.AddAppointmentAsync(patientId, odontologist.Id, serviceId, baseDate.AddHours(11), "Cancelada");
        await database.AddAppointmentAsync(patientId, odontologist.Id, serviceId, baseDate.AddHours(12), "No asistió");

        var model = await database.ExecuteDashboardAsync(administrator);

        Assert.Equal(2, model.CitasPendientes);
    }

    [Fact]
    public async Task Index_Administrador_CuentaAtencionesRealizadas()
    {
        await using var database = await TestDatabase.CreateAsync();
        var administrator = await database.CreateUserAsync("admin.atenciones@example.test", "Administrador", "Admin", "Prueba");
        var odontologist = await database.CreateUserAsync("odontologo.atenciones@example.test", "Odontologo", "Odontologo", "Prueba");
        var patientId = await database.AddPatientAsync(true);
        var serviceId = await database.AddServiceAsync();
        var baseDate = DateTime.Today.AddDays(3);
        await database.AddAppointmentAsync(patientId, odontologist.Id, serviceId, baseDate.AddHours(8), "Atendida");
        await database.AddAppointmentAsync(patientId, odontologist.Id, serviceId, baseDate.AddHours(9), "Atendida");
        await database.AddAppointmentAsync(patientId, odontologist.Id, serviceId, baseDate.AddHours(10), "Pendiente");

        var model = await database.ExecuteDashboardAsync(administrator);

        Assert.Equal(2, model.AtencionesRealizadas);
    }

    [Fact]
    public async Task Index_Administrador_CuentaCitasDelDiaSinImportarLaHora()
    {
        await using var database = await TestDatabase.CreateAsync();
        var administrator = await database.CreateUserAsync("admin.dia@example.test", "Administrador", "Admin", "Prueba");
        var odontologist = await database.CreateUserAsync("odontologo.dia@example.test", "Odontologo", "Odontologo", "Prueba");
        var patientId = await database.AddPatientAsync(true);
        var serviceId = await database.AddServiceAsync();
        var today = DateTime.Today;
        await database.AddAppointmentAsync(patientId, odontologist.Id, serviceId, today.AddMinutes(5), "Pendiente");
        await database.AddAppointmentAsync(patientId, odontologist.Id, serviceId, today.AddHours(23).AddMinutes(55), "Confirmada");
        await database.AddAppointmentAsync(patientId, odontologist.Id, serviceId, today.AddDays(-1).AddHours(12), "Pendiente");
        await database.AddAppointmentAsync(patientId, odontologist.Id, serviceId, today.AddDays(1).AddHours(12), "Pendiente");

        var model = await database.ExecuteDashboardAsync(administrator);

        Assert.Equal(2, model.CitasDelDia);
    }

    [Fact]
    public async Task Index_Recepcionista_VisualizaEstadisticasGeneralesDeLaClinica()
    {
        await using var database = await TestDatabase.CreateAsync();
        var receptionist = await database.CreateUserAsync("recepcionista.dashboard@example.test", "Recepcionista", "Recepcionista", "Prueba");
        var odontologistA = await database.CreateUserAsync("odontologo.a.dashboard@example.test", "Odontologo", "Odontologo A", "Prueba");
        var odontologistB = await database.CreateUserAsync("odontologo.b.dashboard@example.test", "Odontologo", "Odontologo B", "Prueba");
        var patientId = await database.AddPatientAsync(true);
        var serviceId = await database.AddServiceAsync();
        var today = DateTime.Today;
        await database.AddAppointmentAsync(patientId, odontologistA.Id, serviceId, today.AddHours(8), "Pendiente");
        await database.AddAppointmentAsync(patientId, odontologistB.Id, serviceId, today.AddHours(9), "Confirmada");
        await database.AddAppointmentAsync(patientId, odontologistA.Id, serviceId, today.AddDays(-1).AddHours(10), "Atendida");
        await database.AddAppointmentAsync(patientId, odontologistB.Id, serviceId, today.AddDays(-1).AddHours(11), "Atendida");

        var model = await database.ExecuteDashboardAsync(receptionist);

        Assert.Equal(2, model.CitasDelDia);
        Assert.Equal(2, model.CitasPendientes);
        Assert.Equal(2, model.AtencionesRealizadas);
    }

    [Fact]
    public async Task Index_Odontologo_FiltraCitasPorSuUsuario()
    {
        await using var database = await TestDatabase.CreateAsync();
        var odontologistA = await database.CreateUserAsync("odontologo.a.filtro@example.test", "Odontologo", "Odontologo A", "Prueba");
        var odontologistB = await database.CreateUserAsync("odontologo.b.filtro@example.test", "Odontologo", "Odontologo B", "Prueba");
        var patientId = await database.AddPatientAsync(true);
        var serviceId = await database.AddServiceAsync();
        var today = DateTime.Today;
        await database.AddAppointmentAsync(patientId, odontologistA.Id, serviceId, today.AddHours(8), "Pendiente");
        await database.AddAppointmentAsync(patientId, odontologistB.Id, serviceId, today.AddHours(9), "Pendiente");
        await database.AddAppointmentAsync(patientId, odontologistA.Id, serviceId, today.AddDays(-1).AddHours(10), "Atendida");
        await database.AddAppointmentAsync(patientId, odontologistB.Id, serviceId, today.AddDays(-1).AddHours(11), "Atendida");

        var model = await database.ExecuteDashboardAsync(odontologistA);

        Assert.Equal(1, model.CitasDelDia);
        Assert.Equal(1, model.CitasPendientes);
        Assert.Equal(1, model.AtencionesRealizadas);
    }

    [Fact]
    public async Task Index_Odontologo_CuentaTodosLosPacientesActivos()
    {
        await using var database = await TestDatabase.CreateAsync();
        var odontologist = await database.CreateUserAsync("odontologo.pacientes@example.test", "Odontologo", "Odontologo", "Prueba");
        await database.AddPatientAsync(true);
        await database.AddPatientAsync(true);
        await database.AddPatientAsync(true);
        await database.AddPatientAsync(false);

        var model = await database.ExecuteDashboardAsync(odontologist);

        Assert.Equal(3, model.PacientesRegistrados);
    }

    [Fact]
    public async Task Index_UsuarioAutenticado_DevuelveNombreCompletoYRol()
    {
        await using var database = await TestDatabase.CreateAsync();
        var odontologist = await database.CreateUserAsync(
            "odontologo.identidad@example.test",
            "Odontologo",
            "Nombre Dashboard",
            "Apellido Prueba");

        var model = await database.ExecuteDashboardAsync(odontologist);

        Assert.Equal("Nombre Dashboard Apellido Prueba", model.NombreCompleto);
        Assert.Equal("Odontologo", model.Rol);
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private const string Password = "Test1234!";
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _services;

        private TestDatabase(SqliteConnection connection, ApplicationDbContext context, ServiceProvider services)
        {
            _connection = connection;
            Context = context;
            _services = services;
        }

        public ApplicationDbContext Context { get; }
        private UserManager<ApplicationUser> UserManager => _services.GetRequiredService<UserManager<ApplicationUser>>();
        private RoleManager<IdentityRole> RoleManager => _services.GetRequiredService<RoleManager<IdentityRole>>();

        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new ApplicationDbContext(options);
            await context.Database.EnsureCreatedAsync();

            var services = new ServiceCollection()
                .AddSingleton(context)
                .AddLogging()
                .AddIdentityCore<ApplicationUser>()
                .AddRoles<IdentityRole>()
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .Services
                .BuildServiceProvider();

            var database = new TestDatabase(connection, context, services);
            await database.CreateRolesAsync();
            return database;
        }

        public async Task<ApplicationUser> CreateUserAsync(
            string email,
            string role,
            string firstName,
            string lastName)
        {
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                Nombre = firstName,
                Apellido = lastName,
                Estado = true,
                FechaCreacion = DateTime.Now
            };
            var createResult = await UserManager.CreateAsync(user, Password);
            if (!createResult.Succeeded)
            {
                throw new InvalidOperationException(string.Join("; ", createResult.Errors.Select(error => error.Description)));
            }

            var roleResult = await UserManager.AddToRoleAsync(user, role);
            if (!roleResult.Succeeded)
            {
                throw new InvalidOperationException(string.Join("; ", roleResult.Errors.Select(error => error.Description)));
            }

            return user;
        }

        public async Task<int> AddPatientAsync(bool state)
        {
            var patient = new Paciente
            {
                Nombre = $"Paciente {Guid.NewGuid():N}",
                Apellido = "Dashboard",
                Estado = state,
                FechaRegistro = DateTime.Now
            };
            Context.Pacientes.Add(patient);
            await Context.SaveChangesAsync();
            return patient.PacienteId;
        }

        public async Task<int> AddServiceAsync()
        {
            var service = new ServicioOdontologico
            {
                Nombre = $"Servicio {Guid.NewGuid():N}",
                Estado = true,
                FechaCreacion = DateTime.Now
            };
            Context.ServiciosOdontologicos.Add(service);
            await Context.SaveChangesAsync();
            return service.ServicioOdontologicoId;
        }

        public async Task AddAppointmentAsync(
            int patientId,
            string odontologistId,
            int serviceId,
            DateTime start,
            string status)
        {
            Context.Citas.Add(new Cita
            {
                PacienteId = patientId,
                OdontologoId = odontologistId,
                ServicioOdontologicoId = serviceId,
                FechaHoraInicio = start,
                EstadoCita = status,
                FechaCreacion = DateTime.Now
            });
            await Context.SaveChangesAsync();
        }

        public async Task<DashboardViewModel> ExecuteDashboardAsync(ApplicationUser user)
        {
            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.NameIdentifier, user.Id) },
                    authenticationType: "TestAuthentication"))
            };
            var controller = new DashboardController(UserManager, Context)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = httpContext
                }
            };
            controller.TempData = new TempDataDictionary(httpContext, new NullTempDataProvider());

            var result = await controller.Index();
            var view = Assert.IsType<ViewResult>(result);
            return Assert.IsType<DashboardViewModel>(view.Model);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
            _services.Dispose();
        }

        private async Task CreateRolesAsync()
        {
            foreach (var roleName in new[] { "Administrador", "Odontologo", "Recepcionista" })
            {
                var result = await RoleManager.CreateAsync(new IdentityRole(roleName));
                if (!result.Succeeded)
                {
                    throw new InvalidOperationException(string.Join("; ", result.Errors.Select(error => error.Description)));
                }
            }
        }
    }

    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();

        public void SaveTempData(HttpContext context, IDictionary<string, object?> values)
        {
        }
    }
}
