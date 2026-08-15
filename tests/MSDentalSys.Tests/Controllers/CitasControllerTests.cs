using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using MSDentalSys.Data.Context;
using MSDentalSys.Data.Models;
using MSDentalSys.Web.Controllers;
using MSDentalSys.Web.Models.ViewModels;
using Xunit;

namespace MSDentalSys.Tests.Controllers;

public class CitasControllerTests
{
    [Fact]
    public async Task Create_ConDatosValidos_CreaCitaPendiente()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.AddSupportDataAsync();
        var start = new DateTime(2030, 1, 15, 9, 0, 0);
        var controller = database.CreateController();

        var result = await controller.Create(new CitaFormViewModel
        {
            PacienteId = database.PatientId,
            OdontologoId = database.OdontologistId,
            ServicioOdontologicoId = database.ServiceId,
            FechaHoraInicio = start,
            Observaciones = "Cita ficticia generada por prueba automatizada"
        });

        Assert.IsType<RedirectToActionResult>(result);
        var cita = await database.Context.Citas.SingleAsync();
        Assert.Equal(database.PatientId, cita.PacienteId);
        Assert.Equal(database.OdontologistId, cita.OdontologoId);
        Assert.Equal(database.ServiceId, cita.ServicioOdontologicoId);
        Assert.Equal(start, cita.FechaHoraInicio);
        Assert.Equal("Pendiente", cita.EstadoCita);
        Assert.Equal("Cita ficticia generada por prueba automatizada", cita.Observaciones);
    }

    [Fact]
    public async Task Create_ConConflictoDeHorario_NoCreaSegundaCitaYAgregaError()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.AddSupportDataAsync();
        var start = new DateTime(2030, 1, 15, 10, 0, 0);
        database.Context.Citas.Add(database.CreateAppointment(start));
        await database.Context.SaveChangesAsync();

        var controller = database.CreateController();
        var result = await controller.Create(database.CreateAppointmentModel(start));

        Assert.IsType<ViewResult>(result);
        Assert.Single(controller.ModelState[nameof(CitaFormViewModel.FechaHoraInicio)]!.Errors);
        Assert.Contains("otra cita", controller.ModelState[nameof(CitaFormViewModel.FechaHoraInicio)]!.Errors[0].ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await database.Context.Citas.CountAsync());
    }

    [Fact]
    public async Task Create_ConCitaCanceladaEnElHorario_PermiteNuevaCita()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.AddSupportDataAsync();
        var start = new DateTime(2030, 1, 15, 11, 0, 0);
        database.Context.Citas.Add(database.CreateAppointment(start, "Cancelada"));
        await database.Context.SaveChangesAsync();

        var controller = database.CreateController();
        var result = await controller.Create(database.CreateAppointmentModel(start));

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(2, await database.Context.Citas.CountAsync());
        Assert.Equal(1, await database.Context.Citas.CountAsync(c => c.EstadoCita == "Pendiente"));
    }

    [Fact]
    public async Task Reschedule_CitaNoFinal_CambiaHorarioYConservaDatos()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.AddSupportDataAsync();
        var originalStart = new DateTime(2030, 1, 15, 12, 0, 0);
        var newStart = new DateTime(2030, 1, 16, 13, 0, 0);
        var cita = database.CreateAppointment(originalStart, "Confirmada", "Observación original");
        database.Context.Citas.Add(cita);
        await database.Context.SaveChangesAsync();

        var controller = database.CreateController();
        var result = await controller.Reschedule(cita.CitaId, new ReagendarCitaViewModel
        {
            CitaId = cita.CitaId,
            FechaHoraInicio = newStart
        });

        Assert.IsType<RedirectToActionResult>(result);
        var stored = await database.Context.Citas.SingleAsync();
        Assert.Equal(newStart, stored.FechaHoraInicio);
        Assert.Equal(database.PatientId, stored.PacienteId);
        Assert.Equal(database.OdontologistId, stored.OdontologoId);
        Assert.Equal(database.ServiceId, stored.ServicioOdontologicoId);
        Assert.Equal("Confirmada", stored.EstadoCita);
        Assert.Equal("Observación original", stored.Observaciones);
    }

    [Fact]
    public async Task Reschedule_ConConflicto_ConservaHorarioOriginalYAgregaError()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.AddSupportDataAsync();
        var originalStart = new DateTime(2030, 1, 15, 14, 0, 0);
        var occupiedStart = new DateTime(2030, 1, 15, 15, 0, 0);
        var first = database.CreateAppointment(originalStart);
        var second = database.CreateAppointment(occupiedStart, "Confirmada");
        database.Context.Citas.AddRange(first, second);
        await database.Context.SaveChangesAsync();

        var controller = database.CreateController();
        var result = await controller.Reschedule(first.CitaId, new ReagendarCitaViewModel
        {
            CitaId = first.CitaId,
            FechaHoraInicio = occupiedStart
        });

        Assert.IsType<ViewResult>(result);
        Assert.Single(controller.ModelState[nameof(ReagendarCitaViewModel.FechaHoraInicio)]!.Errors);
        Assert.Contains("otra cita", controller.ModelState[nameof(ReagendarCitaViewModel.FechaHoraInicio)]!.Errors[0].ErrorMessage, StringComparison.OrdinalIgnoreCase);
        var stored = await database.Context.Citas.SingleAsync(c => c.CitaId == first.CitaId);
        Assert.Equal(originalStart, stored.FechaHoraInicio);
    }

    [Fact]
    public async Task UpdateStatus_CitaCancelada_EsEstadoFinalYNoSeModifica()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.AddSupportDataAsync();
        var cita = database.CreateAppointment(new DateTime(2030, 1, 16, 9, 0, 0), "Cancelada");
        database.Context.Citas.Add(cita);
        await database.Context.SaveChangesAsync();

        var controller = database.CreateController();
        var result = await controller.UpdateStatus(cita.CitaId, new ActualizarEstadoCitaViewModel
        {
            CitaId = cita.CitaId,
            EstadoCita = "Atendida"
        });

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Cancelada", (await database.Context.Citas.SingleAsync()).EstadoCita);
        Assert.Contains("cancelada", controller.TempData["ErrorMessage"]?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateStatus_CitaAtendida_EsEstadoFinalYNoSeModifica()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.AddSupportDataAsync();
        var cita = database.CreateAppointment(new DateTime(2030, 1, 16, 10, 0, 0), "Atendida");
        database.Context.Citas.Add(cita);
        await database.Context.SaveChangesAsync();

        var controller = database.CreateController();
        var result = await controller.UpdateStatus(cita.CitaId, new ActualizarEstadoCitaViewModel
        {
            CitaId = cita.CitaId,
            EstadoCita = "Cancelada"
        });

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Atendida", (await database.Context.Citas.SingleAsync()).EstadoCita);
        Assert.Contains("atendida", controller.TempData["ErrorMessage"]?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Pendiente")]
    [InlineData("Confirmada")]
    public async Task UpdateStatus_AtendidaManual_RechazaYConservaEstado(string originalStatus)
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.AddSupportDataAsync();
        var cita = database.CreateAppointment(new DateTime(2030, 1, 16, 11, 0, 0), originalStatus);
        database.Context.Citas.Add(cita);
        await database.Context.SaveChangesAsync();

        var controller = database.CreateController();
        var result = await controller.UpdateStatus(cita.CitaId, new ActualizarEstadoCitaViewModel
        {
            CitaId = cita.CitaId,
            EstadoCita = "Atendida"
        });

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(originalStatus, (await database.Context.Citas.SingleAsync()).EstadoCita);
        Assert.Contains("registrar su atención odontológica", controller.TempData["ErrorMessage"]?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Administrador")]
    [InlineData("Recepcionista")]
    [InlineData("Odontologo")]
    public async Task UpdateStatus_CualquierRol_RechazaAtendidaManual(string role)
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.AddSupportDataAsync();
        var cita = database.CreateAppointment(new DateTime(2030, 1, 16, 11, 30, 0), "Pendiente");
        database.Context.Citas.Add(cita);
        await database.Context.SaveChangesAsync();

        var controller = database.CreateController(role);
        var result = await controller.UpdateStatus(cita.CitaId, new ActualizarEstadoCitaViewModel
        {
            CitaId = cita.CitaId,
            EstadoCita = "Atendida"
        });

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Pendiente", (await database.Context.Citas.SingleAsync()).EstadoCita);
        Assert.Contains("registrar su atención odontológica", controller.TempData["ErrorMessage"]?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateStatus_CitaConfirmada_AceptaEstadoNoAsistio()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.AddSupportDataAsync();
        var cita = database.CreateAppointment(new DateTime(2030, 1, 16, 12, 0, 0), "Confirmada");
        database.Context.Citas.Add(cita);
        await database.Context.SaveChangesAsync();

        var controller = database.CreateController();
        var result = await controller.UpdateStatus(cita.CitaId, new ActualizarEstadoCitaViewModel
        {
            CitaId = cita.CitaId,
            EstadoCita = "No asistió"
        });

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("No asistió", (await database.Context.Citas.SingleAsync()).EstadoCita);
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _services;

        private TestDatabase(SqliteConnection connection, ApplicationDbContext context, ServiceProvider services)
        {
            _connection = connection;
            Context = context;
            _services = services;
        }

        public ApplicationDbContext Context { get; }
        public int PatientId { get; private set; }
        public string OdontologistId { get; private set; } = string.Empty;
        public int ServiceId { get; private set; }

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

            return new TestDatabase(connection, context, services);
        }

        public async Task AddSupportDataAsync()
        {
            var patient = new Paciente
            {
                Nombre = "Paciente",
                Apellido = "Ficticio",
                Cedula = "001-0000009-9",
                Estado = true
            };
            var service = new ServicioOdontologico
            {
                Nombre = "Servicio de Prueba",
                Descripcion = "Servicio ficticio para pruebas",
                Estado = true
            };
            var odontologist = new ApplicationUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "odontologo.prueba@example.test",
                NormalizedUserName = "ODONTOLOGO.PRUEBA@EXAMPLE.TEST",
                Email = "odontologo.prueba@example.test",
                NormalizedEmail = "ODONTOLOGO.PRUEBA@EXAMPLE.TEST",
                Nombre = "Odontólogo",
                Apellido = "Ficticio",
                Estado = true,
                SecurityStamp = Guid.NewGuid().ToString()
            };
            var role = new IdentityRole
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Odontologo",
                NormalizedName = "ODONTOLOGO"
            };

            Context.Users.Add(odontologist);
            Context.Roles.Add(role);
            Context.UserRoles.Add(new IdentityUserRole<string>
            {
                UserId = odontologist.Id,
                RoleId = role.Id
            });
            Context.Pacientes.Add(patient);
            Context.ServiciosOdontologicos.Add(service);
            await Context.SaveChangesAsync();

            PatientId = patient.PacienteId;
            OdontologistId = odontologist.Id;
            ServiceId = service.ServicioOdontologicoId;
        }

        public CitasController CreateController(string? role = null)
        {
            var httpContext = new DefaultHttpContext
            {
                User = role is null
                    ? new ClaimsPrincipal(new ClaimsIdentity())
                    : new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, OdontologistId),
                        new Claim(ClaimTypes.Role, role)
                    }, "Test"))
            };
            var controller = new CitasController(
                Context,
                _services.GetRequiredService<UserManager<ApplicationUser>>())
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = httpContext
                }
            };
            var tempData = new TempDataDictionary(httpContext, new RecordingTempDataProvider(new Dictionary<string, object?>()));
            controller.TempData = tempData;
            return controller;
        }

        public Cita CreateAppointment(DateTime start, string status = "Pendiente", string? observations = null)
        {
            return new Cita
            {
                PacienteId = PatientId,
                OdontologoId = OdontologistId,
                ServicioOdontologicoId = ServiceId,
                FechaHoraInicio = start,
                EstadoCita = status,
                Observaciones = observations
            };
        }

        public CitaFormViewModel CreateAppointmentModel(DateTime start)
        {
            return new CitaFormViewModel
            {
                PacienteId = PatientId,
                OdontologoId = OdontologistId,
                ServicioOdontologicoId = ServiceId,
                FechaHoraInicio = start
            };
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
            _services.Dispose();
        }
    }

    private sealed class RecordingTempDataProvider : ITempDataProvider
    {
        private readonly IDictionary<string, object?> _values;

        public RecordingTempDataProvider(IDictionary<string, object?> values)
        {
            _values = values;
        }

        public IDictionary<string, object?> LoadTempData(HttpContext context) => _values;

        public void SaveTempData(HttpContext context, IDictionary<string, object?> values)
        {
            foreach (var pair in values)
            {
                _values[pair.Key] = pair.Value;
            }
        }
    }
}
