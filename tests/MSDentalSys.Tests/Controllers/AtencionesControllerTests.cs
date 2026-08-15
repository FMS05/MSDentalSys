using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MSDentalSys.Data.Context;
using MSDentalSys.Data.Models;
using MSDentalSys.Web.Controllers;
using MSDentalSys.Web.Models.ViewModels;
using Xunit;

namespace MSDentalSys.Tests.Controllers;

public class AtencionesControllerTests
{
    [Theory]
    [InlineData("Pendiente")]
    [InlineData("Confirmada")]
    public async Task Create_DesdeCitaValida_CreaAtencionYMarcaCitaAtendida(string status)
    {
        await using var database = await TestDatabase.CreateAsync();
        var cita = await database.AddAppointmentAsync(status);
        var controller = database.CreateController("Administrador", database.AdminId);

        var result = await controller.Create(new AtencionOdontologicaCreateViewModel
        {
            CitaId = cita.CitaId,
            MotivoConsulta = "Dolor dental",
            Observaciones = "Observación inicial"
        });

        Assert.IsType<RedirectToActionResult>(result);
        var atencion = await database.Context.AtencionesOdontologicas.SingleAsync();
        var storedAppointment = await database.Context.Citas.SingleAsync();
        Assert.Equal(cita.CitaId, atencion.CitaId);
        Assert.Equal(database.PatientId, atencion.PacienteId);
        Assert.Equal(database.OdontologistId, atencion.OdontologoId);
        Assert.Equal("Dolor dental", atencion.MotivoConsulta);
        Assert.Equal("Atendida", storedAppointment.EstadoCita);
    }

    [Theory]
    [InlineData("Atendida")]
    [InlineData("Cancelada")]
    [InlineData("No asistió")]
    public async Task Create_DesdeEstadoFinal_NoCreaAtencion(string status)
    {
        await using var database = await TestDatabase.CreateAsync();
        var cita = await database.AddAppointmentAsync(status);
        var controller = database.CreateController("Administrador", database.AdminId);

        var result = await controller.Create(new AtencionOdontologicaCreateViewModel
        {
            CitaId = cita.CitaId,
            MotivoConsulta = "No debe guardarse"
        });

        Assert.IsType<ViewResult>(result);
        Assert.Empty(await database.Context.AtencionesOdontologicas.ToListAsync());
        Assert.Equal(status, (await database.Context.Citas.SingleAsync()).EstadoCita);
    }

    [Fact]
    public async Task Create_SegundaAtencionParaLaMismaCita_NoLaPermite()
    {
        await using var database = await TestDatabase.CreateAsync();
        var cita = await database.AddAppointmentAsync("Pendiente");
        var controller = database.CreateController("Administrador", database.AdminId);

        await controller.Create(new AtencionOdontologicaCreateViewModel
        {
            CitaId = cita.CitaId,
            MotivoConsulta = "Primera atención"
        });

        var secondResult = await controller.Create(new AtencionOdontologicaCreateViewModel
        {
            CitaId = cita.CitaId,
            MotivoConsulta = "Segunda atención"
        });

        Assert.IsType<ViewResult>(secondResult);
        Assert.Equal(1, await database.Context.AtencionesOdontologicas.CountAsync());
    }

    [Fact]
    public async Task Create_OdontologoDeOtraCita_DevuelveForbid()
    {
        await using var database = await TestDatabase.CreateAsync();
        var cita = await database.AddAppointmentAsync("Pendiente");
        var controller = database.CreateController("Odontologo", database.OtherOdontologistId);

        var result = await controller.Create(new AtencionOdontologicaCreateViewModel
        {
            CitaId = cita.CitaId,
            MotivoConsulta = "No autorizado"
        });

        Assert.IsType<ForbidResult>(result);
        Assert.Empty(await database.Context.AtencionesOdontologicas.ToListAsync());
    }

    [Fact]
    public async Task Create_ConservaPacienteYOdontologoDeLaCita()
    {
        await using var database = await TestDatabase.CreateAsync();
        var cita = await database.AddAppointmentAsync("Confirmada");
        var controller = database.CreateController("Odontologo", database.OdontologistId);

        await controller.Create(new AtencionOdontologicaCreateViewModel
        {
            CitaId = cita.CitaId,
            MotivoConsulta = "Consulta de control"
        });

        var atencion = await database.Context.AtencionesOdontologicas.SingleAsync();
        Assert.Equal(cita.PacienteId, atencion.PacienteId);
        Assert.Equal(cita.OdontologoId, atencion.OdontologoId);
        Assert.Equal(cita.CitaId, atencion.CitaId);
    }

    [Fact]
    public async Task Details_AdministradorPuedeConsultarAtencionDeCualquierOdontologo()
    {
        await using var database = await TestDatabase.CreateAsync();
        var atencion = await database.AddAttentionAsync(database.OtherOdontologistId);
        var controller = database.CreateController("Administrador", database.AdminId);

        var result = await controller.Details(atencion.AtencionOdontologicaId);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<AtencionOdontologica>(view.Model);
        Assert.Equal(database.OtherOdontologistId, model.OdontologoId);
    }

    [Fact]
    public async Task Details_OdontologoPropietarioPuedeConsultarSuAtencion()
    {
        await using var database = await TestDatabase.CreateAsync();
        var atencion = await database.AddAttentionAsync(database.OdontologistId, includeClinicalRecords: true);
        var controller = database.CreateController("Odontologo", database.OdontologistId);

        var result = await controller.Details(atencion.AtencionOdontologicaId);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<AtencionOdontologica>(view.Model);
        Assert.Single(model.Diagnosticos);
        Assert.Single(model.Tratamientos);
        Assert.Single(model.EvolucionesClinicas);
    }

    [Fact]
    public async Task Details_OdontologoAjeno_NoPuedeConsultarLaAtencion()
    {
        await using var database = await TestDatabase.CreateAsync();
        var atencion = await database.AddAttentionAsync(database.OtherOdontologistId);
        var controller = database.CreateController("Odontologo", database.OdontologistId);

        var result = await controller.Details(atencion.AtencionOdontologicaId);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Details_AtencionInexistente_DevuelveNotFound()
    {
        await using var database = await TestDatabase.CreateAsync();
        var controller = database.CreateController("Administrador", database.AdminId);

        var result = await controller.Details(999999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public void Controller_NoTieneAccionesDeEliminacion()
    {
        var actionNames = typeof(AtencionesController).GetMethods()
            .Where(method => method.DeclaringType == typeof(AtencionesController))
            .Select(method => method.Name)
            .ToHashSet();

        Assert.DoesNotContain("Delete", actionNames);
        Assert.DoesNotContain("Remove", actionNames);
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private TestDatabase(SqliteConnection connection, ApplicationDbContext context)
        {
            _connection = connection;
            Context = context;
        }

        public ApplicationDbContext Context { get; }
        public int PatientId { get; private set; }
        public string OdontologistId { get; private set; } = string.Empty;
        public string OtherOdontologistId { get; private set; } = string.Empty;
        public string AdminId { get; private set; } = string.Empty;
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
            var database = new TestDatabase(connection, context);
            await database.AddSupportDataAsync();
            return database;
        }

        private async Task AddSupportDataAsync()
        {
            var patient = new Paciente { Nombre = "Paciente", Apellido = "Prueba", Estado = true };
            var service = new ServicioOdontologico { Nombre = "Servicio de prueba", Estado = true };
            var odontologist = NewUser("odontologo-1@test.local", "Odontólogo Principal");
            var otherOdontologist = NewUser("odontologo-2@test.local", "Odontólogo Secundario");
            var admin = NewUser("admin@test.local", "Administrador");
            Context.AddRange(patient, service, odontologist, otherOdontologist, admin);
            await Context.SaveChangesAsync();
            PatientId = patient.PacienteId;
            OdontologistId = odontologist.Id;
            OtherOdontologistId = otherOdontologist.Id;
            AdminId = admin.Id;
            ServiceId = service.ServicioOdontologicoId;
        }

        public async Task<Cita> AddAppointmentAsync(string status, string? odontologistId = null)
        {
            var appointment = new Cita
            {
                PacienteId = PatientId,
                OdontologoId = odontologistId ?? OdontologistId,
                ServicioOdontologicoId = await Context.ServiciosOdontologicos.Select(s => s.ServicioOdontologicoId).SingleAsync(),
                FechaHoraInicio = new DateTime(2030, 1, 15, 9, 0, 0),
                EstadoCita = status
            };
            Context.Citas.Add(appointment);
            await Context.SaveChangesAsync();
            return appointment;
        }

        public async Task<AtencionOdontologica> AddAttentionAsync(string odontologistId, bool includeClinicalRecords = false)
        {
            var cita = await AddAppointmentAsync("Atendida", odontologistId);
            var atencion = new AtencionOdontologica
            {
                PacienteId = PatientId,
                CitaId = cita.CitaId,
                OdontologoId = odontologistId,
                FechaAtencion = new DateTime(2030, 1, 15, 9, 30, 0),
                MotivoConsulta = "Consulta de prueba"
            };
            Context.AtencionesOdontologicas.Add(atencion);
            await Context.SaveChangesAsync();

            if (includeClinicalRecords)
            {
                Context.Diagnosticos.Add(new Diagnostico
                {
                    AtencionOdontologicaId = atencion.AtencionOdontologicaId,
                    Descripcion = "Diagnóstico de prueba"
                });
                Context.Tratamientos.Add(new Tratamiento
                {
                    AtencionOdontologicaId = atencion.AtencionOdontologicaId,
                    ServicioOdontologicoId = ServiceId,
                    EstadoTratamiento = "Planificado"
                });
                Context.EvolucionesClinicas.Add(new EvolucionClinica
                {
                    AtencionOdontologicaId = atencion.AtencionOdontologicaId,
                    FechaEvolucion = new DateTime(2030, 1, 16),
                    Descripcion = "Evolución de prueba"
                });
                await Context.SaveChangesAsync();
            }

            return atencion;
        }

        public AtencionesController CreateController(string role, string userId)
        {
            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, userId),
                    new Claim(ClaimTypes.Role, role)
                }, "Test"))
            };
            var controller = new AtencionesController(Context)
            {
                ControllerContext = new ControllerContext { HttpContext = httpContext },
                TempData = new TempDataDictionary(httpContext, new TestTempDataProvider())
            };
            return controller;
        }

        private static ApplicationUser NewUser(string email, string name)
        {
            return new ApplicationUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = email,
                NormalizedUserName = email.ToUpperInvariant(),
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                Nombre = name,
                SecurityStamp = Guid.NewGuid().ToString(),
                Estado = true
            };
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();

        public void SaveTempData(HttpContext context, IDictionary<string, object?> values)
        {
        }
    }
}
