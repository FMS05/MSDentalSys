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

public class DiagnosticosControllerTests
{
    [Fact]
    public async Task Create_ConDatosValidos_CreaDiagnosticoAsociado()
    {
        await using var database = await TestDatabase.CreateAsync();
        var atencion = await database.AddAttentionAsync();
        var controller = database.CreateController("Administrador", database.AdminId);

        var result = await controller.Create(new DiagnosticoCreateViewModel
        {
            AtencionOdontologicaId = atencion.AtencionOdontologicaId,
            Descripcion = "Caries dental",
            Observaciones = "Lesión inicial"
        });

        Assert.IsType<RedirectToActionResult>(result);
        var diagnostico = await database.Context.Diagnosticos.SingleAsync();
        Assert.Equal(atencion.AtencionOdontologicaId, diagnostico.AtencionOdontologicaId);
        Assert.Equal("Caries dental", diagnostico.Descripcion);
        Assert.Equal("Lesión inicial", diagnostico.Observaciones);
    }

    [Fact]
    public async Task Create_SinDescripcion_NoCreaDiagnostico()
    {
        await using var database = await TestDatabase.CreateAsync();
        var atencion = await database.AddAttentionAsync();
        var controller = database.CreateController("Administrador", database.AdminId);

        var result = await controller.Create(new DiagnosticoCreateViewModel
        {
            AtencionOdontologicaId = atencion.AtencionOdontologicaId
        });

        Assert.IsType<ViewResult>(result);
        Assert.Empty(await database.Context.Diagnosticos.ToListAsync());
    }

    [Fact]
    public async Task Create_SinObservaciones_PermiteRegistro()
    {
        await using var database = await TestDatabase.CreateAsync();
        var atencion = await database.AddAttentionAsync();
        var controller = database.CreateController("Administrador", database.AdminId);

        await controller.Create(new DiagnosticoCreateViewModel
        {
            AtencionOdontologicaId = atencion.AtencionOdontologicaId,
            Descripcion = "Gingivitis"
        });

        var diagnostico = await database.Context.Diagnosticos.SingleAsync();
        Assert.Null(diagnostico.Observaciones);
    }

    [Fact]
    public async Task Create_UnaAtencionPermiteVariosDiagnosticos()
    {
        await using var database = await TestDatabase.CreateAsync();
        var atencion = await database.AddAttentionAsync();
        var controller = database.CreateController("Administrador", database.AdminId);

        await controller.Create(new DiagnosticoCreateViewModel { AtencionOdontologicaId = atencion.AtencionOdontologicaId, Descripcion = "Caries" });
        await controller.Create(new DiagnosticoCreateViewModel { AtencionOdontologicaId = atencion.AtencionOdontologicaId, Descripcion = "Gingivitis" });

        Assert.Equal(2, await database.Context.Diagnosticos.CountAsync(d => d.AtencionOdontologicaId == atencion.AtencionOdontologicaId));
    }

    [Fact]
    public async Task Create_OdontologoPuedeRegistrarEnSuAtencion()
    {
        await using var database = await TestDatabase.CreateAsync();
        var atencion = await database.AddAttentionAsync();
        var controller = database.CreateController("Odontologo", database.OdontologistId);

        var result = await controller.Create(new DiagnosticoCreateViewModel
        {
            AtencionOdontologicaId = atencion.AtencionOdontologicaId,
            Descripcion = "Maloclusión"
        });

        Assert.IsType<RedirectToActionResult>(result);
    }

    [Fact]
    public async Task Create_OdontologoNoPuedeRegistrarEnAtencionDeOtro()
    {
        await using var database = await TestDatabase.CreateAsync();
        var atencion = await database.AddAttentionAsync();
        var controller = database.CreateController("Odontologo", database.OtherOdontologistId);

        var result = await controller.Create(new DiagnosticoCreateViewModel
        {
            AtencionOdontologicaId = atencion.AtencionOdontologicaId,
            Descripcion = "No autorizado"
        });

        Assert.IsType<ForbidResult>(result);
        Assert.Empty(await database.Context.Diagnosticos.ToListAsync());
    }

    [Fact]
    public async Task Create_NoModificaElEstadoDeLaCita()
    {
        await using var database = await TestDatabase.CreateAsync();
        var atencion = await database.AddAttentionAsync();
        var originalStatus = (await database.Context.Citas.SingleAsync()).EstadoCita;
        var controller = database.CreateController("Administrador", database.AdminId);

        await controller.Create(new DiagnosticoCreateViewModel
        {
            AtencionOdontologicaId = atencion.AtencionOdontologicaId,
            Descripcion = "Diagnóstico de control"
        });

        Assert.Equal(originalStatus, (await database.Context.Citas.SingleAsync()).EstadoCita);
    }

    [Fact]
    public async Task Create_AtencionInexistente_DevuelveNotFound()
    {
        await using var database = await TestDatabase.CreateAsync();
        var controller = database.CreateController("Administrador", database.AdminId);

        var result = await controller.Create(new DiagnosticoCreateViewModel
        {
            AtencionOdontologicaId = 999,
            Descripcion = "Diagnóstico"
        });

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public void Controller_NoTieneAccionesDeEliminacion()
    {
        var actionNames = typeof(DiagnosticosController).GetMethods()
            .Where(method => method.DeclaringType == typeof(DiagnosticosController))
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
            var odontologist = NewUser("odontologo-1@test.local", "Odontólogo Principal");
            var otherOdontologist = NewUser("odontologo-2@test.local", "Odontólogo Secundario");
            var admin = NewUser("admin@test.local", "Administrador");
            var service = new ServicioOdontologico { Nombre = "Servicio de prueba", Estado = true };
            Context.AddRange(patient, odontologist, otherOdontologist, admin, service);
            await Context.SaveChangesAsync();
            PatientId = patient.PacienteId;
            OdontologistId = odontologist.Id;
            OtherOdontologistId = otherOdontologist.Id;
            AdminId = admin.Id;
        }

        public async Task<AtencionOdontologica> AddAttentionAsync()
        {
            var serviceId = await Context.ServiciosOdontologicos.Select(s => s.ServicioOdontologicoId).SingleAsync();
            var cita = new Cita
            {
                PacienteId = PatientId,
                OdontologoId = OdontologistId,
                ServicioOdontologicoId = serviceId,
                FechaHoraInicio = new DateTime(2030, 1, 15, 9, 0, 0),
                EstadoCita = "Atendida"
            };
            Context.Citas.Add(cita);
            await Context.SaveChangesAsync();
            var atencion = new AtencionOdontologica
            {
                PacienteId = PatientId,
                CitaId = cita.CitaId,
                OdontologoId = OdontologistId,
                FechaAtencion = new DateTime(2030, 1, 15, 9, 30, 0),
                MotivoConsulta = "Consulta de prueba"
            };
            Context.AtencionesOdontologicas.Add(atencion);
            await Context.SaveChangesAsync();
            return atencion;
        }

        public DiagnosticosController CreateController(string role, string userId)
        {
            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, userId),
                    new Claim(ClaimTypes.Role, role)
                }, "Test"))
            };
            return new DiagnosticosController(Context)
            {
                ControllerContext = new ControllerContext { HttpContext = httpContext },
                TempData = new TempDataDictionary(httpContext, new TestTempDataProvider())
            };
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
