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

public class EvolucionesClinicasControllerTests
{
    [Fact]
    public async Task Create_ConDatosValidos_CreaEvolucionAsociada()
    {
        await using var database = await TestDatabase.CreateAsync();
        var attention = await database.AddAttentionAsync();
        var controller = database.CreateController("Administrador", database.AdminId);

        var result = await controller.Create(database.CreateModel(attention));

        Assert.IsType<RedirectToActionResult>(result);
        var evolution = await database.Context.EvolucionesClinicas.SingleAsync();
        Assert.Equal(attention.AtencionOdontologicaId, evolution.AtencionOdontologicaId);
        Assert.Equal("Paciente evolucionando favorablemente", evolution.Descripcion);
    }

    [Fact]
    public async Task Create_SinFecha_NoCreaEvolucion()
    {
        await using var database = await TestDatabase.CreateAsync();
        var attention = await database.AddAttentionAsync();
        var controller = database.CreateController("Administrador", database.AdminId);
        var model = database.CreateModel(attention);
        model.FechaEvolucion = default;

        var result = await controller.Create(model);

        Assert.IsType<ViewResult>(result);
        Assert.Empty(await database.Context.EvolucionesClinicas.ToListAsync());
    }

    [Fact]
    public async Task Create_SinDescripcion_NoCreaEvolucion()
    {
        await using var database = await TestDatabase.CreateAsync();
        var attention = await database.AddAttentionAsync();
        var controller = database.CreateController("Administrador", database.AdminId);
        var model = database.CreateModel(attention);
        model.Descripcion = string.Empty;

        var result = await controller.Create(model);

        Assert.IsType<ViewResult>(result);
        Assert.Empty(await database.Context.EvolucionesClinicas.ToListAsync());
    }

    [Fact]
    public async Task Create_DescripcionMayorA500_NoCreaEvolucion()
    {
        await using var database = await TestDatabase.CreateAsync();
        var attention = await database.AddAttentionAsync();
        var controller = database.CreateController("Administrador", database.AdminId);
        var model = database.CreateModel(attention);
        model.Descripcion = new string('x', 501);

        var result = await controller.Create(model);

        Assert.IsType<ViewResult>(result);
        Assert.Empty(await database.Context.EvolucionesClinicas.ToListAsync());
    }

    [Fact]
    public async Task Create_UnaAtencionPermiteMultiplesEvoluciones()
    {
        await using var database = await TestDatabase.CreateAsync();
        var attention = await database.AddAttentionAsync();
        var controller = database.CreateController("Administrador", database.AdminId);

        await controller.Create(database.CreateModel(attention, "Primera evolución"));
        await controller.Create(database.CreateModel(attention, "Segunda evolución"));

        Assert.Equal(2, await database.Context.EvolucionesClinicas.CountAsync(e => e.AtencionOdontologicaId == attention.AtencionOdontologicaId));
    }

    [Fact]
    public async Task Create_OdontologoPuedeCrearEnSuAtencion()
    {
        await using var database = await TestDatabase.CreateAsync();
        var attention = await database.AddAttentionAsync();
        var controller = database.CreateController("Odontologo", database.OdontologistId);

        var result = await controller.Create(database.CreateModel(attention));

        Assert.IsType<RedirectToActionResult>(result);
    }

    [Fact]
    public async Task Create_OdontologoNoPuedeCrearEnAtencionDeOtro()
    {
        await using var database = await TestDatabase.CreateAsync();
        var attention = await database.AddAttentionAsync();
        var controller = database.CreateController("Odontologo", database.OtherOdontologistId);

        var result = await controller.Create(database.CreateModel(attention));

        Assert.IsType<ForbidResult>(result);
        Assert.Empty(await database.Context.EvolucionesClinicas.ToListAsync());
    }

    [Fact]
    public async Task Create_NoModificaLaCita()
    {
        await using var database = await TestDatabase.CreateAsync();
        var attention = await database.AddAttentionAsync();
        var originalStatus = (await database.Context.Citas.SingleAsync()).EstadoCita;
        var controller = database.CreateController("Administrador", database.AdminId);

        await controller.Create(database.CreateModel(attention));

        Assert.Equal(originalStatus, (await database.Context.Citas.SingleAsync()).EstadoCita);
    }

    [Fact]
    public async Task Create_NoModificaDiagnosticos()
    {
        await using var database = await TestDatabase.CreateAsync();
        var attention = await database.AddAttentionAsync();
        database.Context.Diagnosticos.Add(new Diagnostico { AtencionOdontologicaId = attention.AtencionOdontologicaId, Descripcion = "Diagnóstico existente" });
        await database.Context.SaveChangesAsync();
        var controller = database.CreateController("Administrador", database.AdminId);

        await controller.Create(database.CreateModel(attention));

        Assert.Equal("Diagnóstico existente", (await database.Context.Diagnosticos.SingleAsync()).Descripcion);
    }

    [Fact]
    public async Task Create_NoModificaTratamientos()
    {
        await using var database = await TestDatabase.CreateAsync();
        var attention = await database.AddAttentionAsync();
        database.Context.Tratamientos.Add(new Tratamiento
        {
            AtencionOdontologicaId = attention.AtencionOdontologicaId,
            ServicioOdontologicoId = database.ServiceId,
            FechaInicio = new DateTime(2030, 1, 15),
            EstadoTratamiento = "Planificado"
        });
        await database.Context.SaveChangesAsync();
        var controller = database.CreateController("Administrador", database.AdminId);

        await controller.Create(database.CreateModel(attention));

        Assert.Equal("Planificado", (await database.Context.Tratamientos.SingleAsync()).EstadoTratamiento);
    }

    [Fact]
    public async Task Create_AtencionInexistente_DevuelveNotFound()
    {
        await using var database = await TestDatabase.CreateAsync();
        var controller = database.CreateController("Administrador", database.AdminId);

        var result = await controller.Create(new EvolucionClinicaCreateViewModel
        {
            AtencionOdontologicaId = 999,
            FechaEvolucion = DateTime.Now,
            Descripcion = "Evolución"
        });

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public void Controller_NoTieneAccionesDeEliminacion()
    {
        var actionNames = typeof(EvolucionesClinicasController).GetMethods()
            .Where(method => method.DeclaringType == typeof(EvolucionesClinicasController))
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
        public string OdontologistId { get; private set; } = string.Empty;
        public string OtherOdontologistId { get; private set; } = string.Empty;
        public string AdminId { get; private set; } = string.Empty;
        public int PatientId { get; private set; }
        public int ServiceId { get; private set; }

        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
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
            ServiceId = service.ServicioOdontologicoId;
        }

        public async Task<AtencionOdontologica> AddAttentionAsync()
        {
            var cita = new Cita
            {
                PacienteId = PatientId,
                OdontologoId = OdontologistId,
                ServicioOdontologicoId = ServiceId,
                FechaHoraInicio = new DateTime(2030, 1, 15, 9, 0, 0),
                EstadoCita = "Atendida"
            };
            Context.Citas.Add(cita);
            await Context.SaveChangesAsync();
            var attention = new AtencionOdontologica
            {
                PacienteId = PatientId,
                CitaId = cita.CitaId,
                OdontologoId = OdontologistId,
                FechaAtencion = new DateTime(2030, 1, 15, 9, 30, 0),
                MotivoConsulta = "Consulta de prueba"
            };
            Context.AtencionesOdontologicas.Add(attention);
            await Context.SaveChangesAsync();
            return attention;
        }

        public EvolucionClinicaCreateViewModel CreateModel(AtencionOdontologica attention, string? description = null)
        {
            return new EvolucionClinicaCreateViewModel
            {
                AtencionOdontologicaId = attention.AtencionOdontologicaId,
                FechaEvolucion = new DateTime(2030, 1, 16, 10, 0, 0),
                Descripcion = description ?? "Paciente evolucionando favorablemente"
            };
        }

        public EvolucionesClinicasController CreateController(string role, string userId)
        {
            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, userId),
                    new Claim(ClaimTypes.Role, role)
                }, "Test"))
            };
            return new EvolucionesClinicasController(Context)
            {
                ControllerContext = new ControllerContext { HttpContext = httpContext },
                TempData = new TempDataDictionary(httpContext, new TestTempDataProvider())
            };
        }

        private static ApplicationUser NewUser(string email, string name)
        {
            return new ApplicationUser
            {
                Id = Guid.NewGuid().ToString(), UserName = email, NormalizedUserName = email.ToUpperInvariant(),
                Email = email, NormalizedEmail = email.ToUpperInvariant(), Nombre = name,
                SecurityStamp = Guid.NewGuid().ToString(), Estado = true
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
        public void SaveTempData(HttpContext context, IDictionary<string, object?> values) { }
    }
}
