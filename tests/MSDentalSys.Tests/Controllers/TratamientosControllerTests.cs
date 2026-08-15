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

public class TratamientosControllerTests
{
    [Fact]
    public async Task Create_ConDatosValidos_CreaTratamientoAsociado()
    {
        await using var database = await TestDatabase.CreateAsync();
        var atencion = await database.AddAttentionAsync();
        var controller = database.CreateController("Administrador", database.AdminId);

        var result = await controller.Create(database.CreateModel(atencion, database.ActiveServiceId));

        Assert.IsType<RedirectToActionResult>(result);
        var treatment = await database.Context.Tratamientos.SingleAsync();
        Assert.Equal(atencion.AtencionOdontologicaId, treatment.AtencionOdontologicaId);
        Assert.Equal(database.ActiveServiceId, treatment.ServicioOdontologicoId);
        Assert.Equal("Planificado", treatment.EstadoTratamiento);
    }

    [Fact]
    public async Task Create_ServicioObligatorio_NoCreaTratamiento()
    {
        await using var database = await TestDatabase.CreateAsync();
        var atencion = await database.AddAttentionAsync();
        var controller = database.CreateController("Administrador", database.AdminId);
        var model = database.CreateModel(atencion, 0);

        var result = await controller.Create(model);

        Assert.IsType<ViewResult>(result);
        Assert.Empty(await database.Context.Tratamientos.ToListAsync());
    }

    [Fact]
    public async Task Create_SoloPermiteServicioActivo()
    {
        await using var database = await TestDatabase.CreateAsync();
        var atencion = await database.AddAttentionAsync();
        var controller = database.CreateController("Administrador", database.AdminId);

        var result = await controller.Create(database.CreateModel(atencion, database.InactiveServiceId));

        Assert.IsType<ViewResult>(result);
        Assert.Empty(await database.Context.Tratamientos.ToListAsync());
    }

    [Fact]
    public async Task Create_ObservacionesOpcionales_PermiteRegistro()
    {
        await using var database = await TestDatabase.CreateAsync();
        var atencion = await database.AddAttentionAsync();
        var controller = database.CreateController("Administrador", database.AdminId);
        var model = database.CreateModel(atencion, database.ActiveServiceId);
        model.Observaciones = null;

        await controller.Create(model);

        Assert.Null((await database.Context.Tratamientos.SingleAsync()).Observaciones);
    }

    [Fact]
    public async Task Create_UnaAtencionPermiteVariosTratamientos()
    {
        await using var database = await TestDatabase.CreateAsync();
        var atencion = await database.AddAttentionAsync();
        var controller = database.CreateController("Administrador", database.AdminId);

        await controller.Create(database.CreateModel(atencion, database.ActiveServiceId));
        await controller.Create(database.CreateModel(atencion, database.ActiveServiceId));

        Assert.Equal(2, await database.Context.Tratamientos.CountAsync(t => t.AtencionOdontologicaId == atencion.AtencionOdontologicaId));
    }

    [Fact]
    public async Task Create_OdontologoPuedeCrearEnSuAtencion()
    {
        await using var database = await TestDatabase.CreateAsync();
        var atencion = await database.AddAttentionAsync();
        var controller = database.CreateController("Odontologo", database.OdontologistId);

        var result = await controller.Create(database.CreateModel(atencion, database.ActiveServiceId));

        Assert.IsType<RedirectToActionResult>(result);
    }

    [Fact]
    public async Task Create_OdontologoNoPuedeCrearEnAtencionDeOtro()
    {
        await using var database = await TestDatabase.CreateAsync();
        var atencion = await database.AddAttentionAsync();
        var controller = database.CreateController("Odontologo", database.OtherOdontologistId);

        var result = await controller.Create(database.CreateModel(atencion, database.ActiveServiceId));

        Assert.IsType<ForbidResult>(result);
        Assert.Empty(await database.Context.Tratamientos.ToListAsync());
    }

    [Fact]
    public async Task Create_EstadoInicialPlanificado_EsValido()
    {
        await using var database = await TestDatabase.CreateAsync();
        var atencion = await database.AddAttentionAsync();
        var controller = database.CreateController("Administrador", database.AdminId);

        await controller.Create(database.CreateModel(atencion, database.ActiveServiceId));

        Assert.Equal("Planificado", (await database.Context.Tratamientos.SingleAsync()).EstadoTratamiento);
    }

    [Fact]
    public async Task UpdateStatus_PlanificadoAEnProgreso_Actualiza()
    {
        await using var database = await TestDatabase.CreateAsync();
        var treatment = await database.AddTreatmentAsync("Planificado");
        var controller = database.CreateController("Administrador", database.AdminId);

        var result = await controller.UpdateStatus(treatment.TratamientoId, "En progreso");

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("En progreso", (await database.Context.Tratamientos.SingleAsync()).EstadoTratamiento);
    }

    [Fact]
    public async Task UpdateStatus_EnProgresoACompletado_Actualiza()
    {
        await using var database = await TestDatabase.CreateAsync();
        var treatment = await database.AddTreatmentAsync("En progreso");
        var controller = database.CreateController("Administrador", database.AdminId);

        await controller.UpdateStatus(treatment.TratamientoId, "Completado");

        Assert.Equal("Completado", (await database.Context.Tratamientos.SingleAsync()).EstadoTratamiento);
    }

    [Fact]
    public async Task UpdateStatus_EnProgresoAPlanificado_RechazaYConservaEstado()
    {
        await using var database = await TestDatabase.CreateAsync();
        var treatment = await database.AddTreatmentAsync("En progreso");
        var controller = database.CreateController("Administrador", database.AdminId);

        var result = await controller.UpdateStatus(treatment.TratamientoId, "Planificado");

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("En progreso", (await database.Context.Tratamientos.SingleAsync()).EstadoTratamiento);
        Assert.Contains("retroceder", controller.TempData["ErrorMessage"]?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateStatus_PlanificadoACompletado_Actualiza()
    {
        await using var database = await TestDatabase.CreateAsync();
        var treatment = await database.AddTreatmentAsync("Planificado");
        var controller = database.CreateController("Administrador", database.AdminId);

        await controller.UpdateStatus(treatment.TratamientoId, "Completado");

        Assert.Equal("Completado", (await database.Context.Tratamientos.SingleAsync()).EstadoTratamiento);
    }

    [Fact]
    public async Task UpdateStatus_CompletadoNoVuelveAEnProgreso()
    {
        await using var database = await TestDatabase.CreateAsync();
        var treatment = await database.AddTreatmentAsync("Completado");
        var controller = database.CreateController("Administrador", database.AdminId);

        await controller.UpdateStatus(treatment.TratamientoId, "En progreso");

        Assert.Equal("Completado", (await database.Context.Tratamientos.SingleAsync()).EstadoTratamiento);
    }

    [Fact]
    public async Task UpdateStatus_CompletadoNoVuelveAPlanificado()
    {
        await using var database = await TestDatabase.CreateAsync();
        var treatment = await database.AddTreatmentAsync("Completado");
        var controller = database.CreateController("Administrador", database.AdminId);

        var result = await controller.UpdateStatus(treatment.TratamientoId, "Planificado");

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Completado", (await database.Context.Tratamientos.SingleAsync()).EstadoTratamiento);
        Assert.Contains("completado", controller.TempData["ErrorMessage"]?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_RechazaEstadoNoPermitido()
    {
        await using var database = await TestDatabase.CreateAsync();
        var atencion = await database.AddAttentionAsync();
        var controller = database.CreateController("Administrador", database.AdminId);
        var model = database.CreateModel(atencion, database.ActiveServiceId);
        model.EstadoTratamiento = "Cancelado";

        var result = await controller.Create(model);

        Assert.IsType<ViewResult>(result);
        Assert.Empty(await database.Context.Tratamientos.ToListAsync());
    }

    [Fact]
    public async Task Create_NoModificaLaCita()
    {
        await using var database = await TestDatabase.CreateAsync();
        var atencion = await database.AddAttentionAsync();
        var originalStatus = (await database.Context.Citas.SingleAsync()).EstadoCita;
        var controller = database.CreateController("Administrador", database.AdminId);

        await controller.Create(database.CreateModel(atencion, database.ActiveServiceId));

        Assert.Equal(originalStatus, (await database.Context.Citas.SingleAsync()).EstadoCita);
    }

    [Fact]
    public async Task UpdateStatus_OdontologoNoPuedeModificarAtencionDeOtro()
    {
        await using var database = await TestDatabase.CreateAsync();
        var treatment = await database.AddTreatmentAsync("Planificado");
        var controller = database.CreateController("Odontologo", database.OtherOdontologistId);

        var result = await controller.UpdateStatus(treatment.TratamientoId, "En progreso");

        Assert.IsType<ForbidResult>(result);
        Assert.Equal("Planificado", (await database.Context.Tratamientos.SingleAsync()).EstadoTratamiento);
    }

    [Fact]
    public async Task UpdateStatus_RechazaEstadoNoPermitido()
    {
        await using var database = await TestDatabase.CreateAsync();
        var treatment = await database.AddTreatmentAsync("Planificado");
        var controller = database.CreateController("Administrador", database.AdminId);

        await controller.UpdateStatus(treatment.TratamientoId, "Cancelado");

        Assert.Equal("Planificado", (await database.Context.Tratamientos.SingleAsync()).EstadoTratamiento);
        Assert.Contains("no es válido", controller.TempData["ErrorMessage"]?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Controller_NoTieneAccionesDeEliminacion()
    {
        var actionNames = typeof(TratamientosController).GetMethods()
            .Where(method => method.DeclaringType == typeof(TratamientosController))
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
        public int ActiveServiceId { get; private set; }
        public int InactiveServiceId { get; private set; }

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
            var activeService = new ServicioOdontologico { Nombre = "Servicio activo", Estado = true };
            var inactiveService = new ServicioOdontologico { Nombre = "Servicio inactivo", Estado = false };
            Context.AddRange(patient, odontologist, otherOdontologist, admin, activeService, inactiveService);
            await Context.SaveChangesAsync();
            PatientId = patient.PacienteId;
            OdontologistId = odontologist.Id;
            OtherOdontologistId = otherOdontologist.Id;
            AdminId = admin.Id;
            ActiveServiceId = activeService.ServicioOdontologicoId;
            InactiveServiceId = inactiveService.ServicioOdontologicoId;
        }

        public async Task<AtencionOdontologica> AddAttentionAsync()
        {
            var cita = new Cita
            {
                PacienteId = PatientId,
                OdontologoId = OdontologistId,
                ServicioOdontologicoId = ActiveServiceId,
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

        public async Task<Tratamiento> AddTreatmentAsync(string status)
        {
            var atencion = await AddAttentionAsync();
            var treatment = new Tratamiento
            {
                AtencionOdontologicaId = atencion.AtencionOdontologicaId,
                ServicioOdontologicoId = ActiveServiceId,
                FechaInicio = new DateTime(2030, 1, 16),
                EstadoTratamiento = status
            };
            Context.Tratamientos.Add(treatment);
            await Context.SaveChangesAsync();
            return treatment;
        }

        public TratamientoCreateViewModel CreateModel(AtencionOdontologica atencion, int serviceId)
        {
            return new TratamientoCreateViewModel
            {
                AtencionOdontologicaId = atencion.AtencionOdontologicaId,
                ServicioOdontologicoId = serviceId,
                FechaInicio = new DateTime(2030, 1, 16),
                EstadoTratamiento = "Planificado",
                Observaciones = "Observación de prueba"
            };
        }

        public TratamientosController CreateController(string role, string userId)
        {
            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, userId),
                    new Claim(ClaimTypes.Role, role)
                }, "Test"))
            };
            return new TratamientosController(Context)
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
