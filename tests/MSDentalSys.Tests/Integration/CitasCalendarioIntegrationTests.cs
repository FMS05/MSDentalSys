using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MSDentalSys.Data.Context;
using MSDentalSys.Data.Models;
using Xunit;

namespace MSDentalSys.Tests.Integration;

public class CitasCalendarioIntegrationTests
{
    private static readonly DateTime RangeStart = new(2030, 2, 1);
    private const string DayUrl = "/Citas/Calendario?start=2030-02-01&end=2030-02-02";
    private const string DoctorId = "calendario-doctor";
    private const string OtherDoctorId = "calendario-doctor-inactivo";

    [Theory]
    [InlineData(null, HttpStatusCode.Redirect)]
    [InlineData("Paciente", HttpStatusCode.Forbidden)]
    [InlineData("Administrador", HttpStatusCode.OK)]
    [InlineData("Recepcionista", HttpStatusCode.OK)]
    [InlineData("Odontologo", HttpStatusCode.OK)]
    public async Task CalendarioHttp_AplicaAutorizacion(string? role, HttpStatusCode expected)
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = Client(factory, role);
        using var response = await client.GetAsync(DayUrl);
        Assert.Equal(expected, response.StatusCode);
        if (role is null)
            Assert.StartsWith("/Account/Login", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task CalendarioHttp_SinCitas_DevuelveArrayJsonVacio()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = Client(factory);
        using var response = await client.GetAsync(DayUrl);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
        Assert.Empty(json.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task CalendarioHttp_ContratoExplicito_NoExponeDatosPersonalesNiNavegaciones()
    {
        using var factory = new CustomWebApplicationFactory();
        var seed = await SeedAsync(factory);
        var appointment = seed.Appointment(RangeStart.AddHours(10));
        await SaveAsync(factory, appointment);
        using var client = Client(factory);
        using var json = JsonDocument.Parse(await client.GetStringAsync(DayUrl));
        var item = Assert.Single(json.RootElement.EnumerateArray());

        Assert.Equal(new[] { "duracionConocida", "end", "estado", "id", "odontologoId", "odontologoNombre", "start", "title", "url" },
            item.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.Equal(appointment.CitaId, item.GetProperty("id").GetInt32());
        Assert.Equal("Ana Calendario · Servicio calendario", item.GetProperty("title").GetString());
        Assert.Equal("2030-02-01T10:00:00", item.GetProperty("start").GetString());
        Assert.Equal("2030-02-01T10:30:00", item.GetProperty("end").GetString());
        Assert.True(item.GetProperty("duracionConocida").GetBoolean());
        Assert.Equal("Pendiente", item.GetProperty("estado").GetString());
        Assert.Equal(DoctorId, item.GetProperty("odontologoId").GetString());
        Assert.Equal("Doctor Calendario", item.GetProperty("odontologoNombre").GetString());
        Assert.Equal($"/Citas/Details/{appointment.CitaId}", item.GetProperty("url").GetString());
        Assert.DoesNotContain("dato-privado", json.RootElement.GetRawText());
        using var details = await client.GetAsync(item.GetProperty("url").GetString());
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
    }

    [Theory]
    [InlineData("Administrador")]
    [InlineData("Recepcionista")]
    public async Task CalendarioHttp_AdministracionConsultaTodosOFiltraOdontologoHistorico(string role)
    {
        using var factory = new CustomWebApplicationFactory();
        var seed = await SeedAsync(factory);
        var own = seed.Appointment(RangeStart.AddHours(10));
        var other = seed.Appointment(RangeStart.AddHours(10), dentistId: OtherDoctorId, patientId: seed.InactivePatientId);
        await SaveAsync(factory, own, other);
        using var client = Client(factory, role);
        using var all = JsonDocument.Parse(await client.GetStringAsync(DayUrl));
        Assert.Equal(new[] { own.CitaId, other.CitaId }.Order(), EventIds(all));
        using var historical = JsonDocument.Parse(await client.GetStringAsync(DayUrl + "&odontologoId=" + OtherDoctorId));
        Assert.Equal(other.CitaId, Assert.Single(EventIds(historical)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(DoctorId)]
    [InlineData(OtherDoctorId)]
    [InlineData("odontologo-inexistente")]
    public async Task CalendarioHttp_OdontologoSoloVePropiasAunqueManipuleFiltro(string? requestedDentist)
    {
        using var factory = new CustomWebApplicationFactory();
        var seed = await SeedAsync(factory);
        var own = seed.Appointment(RangeStart.AddHours(10));
        var other = seed.Appointment(RangeStart.AddHours(10), dentistId: OtherDoctorId);
        await SaveAsync(factory, own, other);
        using var client = Client(factory, "Odontologo");
        var url = requestedDentist is null ? DayUrl : DayUrl + "&odontologoId=" + requestedDentist;
        using var json = JsonDocument.Parse(await client.GetStringAsync(url));
        Assert.Equal(own.CitaId, Assert.Single(EventIds(json)));
    }

    [Fact]
    public async Task CalendarioHttp_RolMixtoOdontologoAdministrador_ConservaPrecedenciaOdontologo()
    {
        using var factory = new CustomWebApplicationFactory();
        using var mixedFactory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddTransient<IClaimsTransformation, AddAdministratorRole>()));
        var seed = await SeedAsync(mixedFactory);
        var own = seed.Appointment(RangeStart.AddHours(10));
        var other = seed.Appointment(RangeStart.AddHours(10), dentistId: OtherDoctorId);
        await SaveAsync(mixedFactory, own, other);
        using var client = Client(mixedFactory, "Odontologo");
        using var json = JsonDocument.Parse(await client.GetStringAsync(DayUrl + "&odontologoId=" + OtherDoctorId));
        Assert.Equal(own.CitaId, Assert.Single(EventIds(json)));
    }

    [Theory]
    [InlineData(0, 30, true)]
    [InlineData(600, 30, true)]
    [InlineData(-30, 60, true)] // Cruza medianoche y el cambio de enero a febrero.
    [InlineData(1410, 120, true)] // Empieza dentro, termina después del rango.
    [InlineData(-60, 60, false)] // Fin exactamente en start.
    [InlineData(1440, 30, false)] // Inicio exactamente en end.
    [InlineData(1500, 30, false)]
    [InlineData(-1440, 1440, false)]
    [InlineData(-1439, 1440, true)] // Duración máxima; comenzó casi un día antes.
    [InlineData(-30, null, false)]
    [InlineData(0, null, true)]
    [InlineData(1439, null, true)]
    [InlineData(1440, null, false)]
    public async Task CalendarioHttp_InterseccionRespetaLimitesYDuracionNula(int startMinutes, int? duration, bool included)
    {
        using var factory = new CustomWebApplicationFactory();
        var seed = await SeedAsync(factory);
        var appointment = seed.Appointment(RangeStart.AddMinutes(startMinutes), duration);
        await SaveAsync(factory, appointment);
        using var client = Client(factory);
        using var json = JsonDocument.Parse(await client.GetStringAsync(DayUrl));
        if (included)
            Assert.Equal(appointment.CitaId, Assert.Single(EventIds(json)));
        else
            Assert.Empty(EventIds(json));
    }

    [Fact]
    public async Task CalendarioHttp_UsaDuracionProgramadaAunqueCambieCatalogo()
    {
        using var factory = new CustomWebApplicationFactory();
        var seed = await SeedAsync(factory);
        var appointment = seed.Appointment(RangeStart.AddHours(10), 45);
        await SaveAsync(factory, appointment);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.ServiciosOdontologicos.SingleAsync()).DuracionEstimadaMinutos = 240;
            (await db.SubserviciosOdontologicos.SingleAsync()).DuracionEstimadaMinutos = 180;
            await db.SaveChangesAsync();
        }
        using var client = Client(factory);
        using var json = JsonDocument.Parse(await client.GetStringAsync(DayUrl));
        var item = Assert.Single(json.RootElement.EnumerateArray());
        Assert.Equal("2030-02-01T10:45:00", item.GetProperty("end").GetString());
        Assert.True(item.GetProperty("duracionConocida").GetBoolean());
    }

    [Fact]
    public async Task CalendarioHttp_DuracionNulaEsExplicitaYSoloConsultaNoLaCompleta()
    {
        using var factory = new CustomWebApplicationFactory();
        var seed = await SeedAsync(factory);
        var appointment = seed.Appointment(RangeStart.AddHours(10), null);
        await SaveAsync(factory, appointment);
        using var client = Client(factory);
        using var json = JsonDocument.Parse(await client.GetStringAsync(DayUrl));
        var item = Assert.Single(json.RootElement.EnumerateArray());
        Assert.False(item.GetProperty("duracionConocida").GetBoolean());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("end").ValueKind);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var saved = await db.Citas.SingleAsync();
        Assert.Null(saved.DuracionProgramadaMinutos);
        Assert.Equal(seed.SubserviceId, saved.SubservicioOdontologicoId);
        Assert.Equal(appointment.FechaHoraInicio, saved.FechaHoraInicio);
        Assert.Equal(appointment.EstadoCita, saved.EstadoCita);
    }

    [Theory]
    [InlineData("Pendiente")]
    [InlineData("Confirmada")]
    [InlineData("Atendida")]
    [InlineData("No asistió")]
    [InlineData("Cancelada")]
    public async Task CalendarioHttp_DevuelveCincoEstadosYPermiteFiltrarlos(string status)
    {
        using var factory = new CustomWebApplicationFactory();
        var seed = await SeedAsync(factory);
        var states = new[] { "Pendiente", "Confirmada", "Atendida", "No asistió", "Cancelada" };
        var appointments = states.Select((state, index) => seed.Appointment(RangeStart.AddHours(8 + index), status: state)).ToArray();
        await SaveAsync(factory, appointments);
        using var client = Client(factory);
        using var all = JsonDocument.Parse(await client.GetStringAsync(DayUrl));
        Assert.Equal(states.Order(), all.RootElement.EnumerateArray().Select(item => item.GetProperty("estado").GetString()).Order());
        using var filtered = JsonDocument.Parse(await client.GetStringAsync(DayUrl + "&estado=" + Uri.EscapeDataString(status)));
        Assert.Equal(appointments.Single(cita => cita.EstadoCita == status).CitaId, Assert.Single(EventIds(filtered)));
    }

    [Theory]
    [InlineData("paciente", 2)]
    [InlineData("estado", 3)]
    [InlineData("odontologo", 2)]
    [InlineData("paciente+estado", 1)]
    [InlineData("todos", 1)]
    [InlineData("estado-invalido", 4)]
    [InlineData("paciente-inexistente", 0)]
    [InlineData("odontologo-inexistente", 0)]
    public async Task CalendarioHttp_FiltrosSeCombinanSinAmpliarResultados(string filter, int expectedCount)
    {
        using var factory = new CustomWebApplicationFactory();
        var seed = await SeedAsync(factory);
        var target = seed.Appointment(RangeStart.AddHours(8), status: "Confirmada", dentistId: OtherDoctorId, patientId: seed.InactivePatientId);
        await SaveAsync(factory, target,
            seed.Appointment(RangeStart.AddHours(9), status: "Pendiente", patientId: seed.InactivePatientId),
            seed.Appointment(RangeStart.AddHours(10), status: "Confirmada", dentistId: OtherDoctorId),
            seed.Appointment(RangeStart.AddHours(11), status: "Confirmada"));
        var suffix = filter switch
        {
            "paciente" => $"&pacienteId={seed.InactivePatientId}",
            "estado" => "&estado=Confirmada",
            "odontologo" => "&odontologoId=" + OtherDoctorId,
            "paciente+estado" => $"&pacienteId={seed.InactivePatientId}&estado=Confirmada",
            "todos" => $"&pacienteId={seed.InactivePatientId}&estado=Confirmada&odontologoId={OtherDoctorId}",
            "estado-invalido" => "&estado=EstadoInexistente",
            "paciente-inexistente" => "&pacienteId=2147483647",
            _ => "&odontologoId=odontologo-inexistente"
        };
        using var client = Client(factory);
        using var json = JsonDocument.Parse(await client.GetStringAsync(DayUrl + suffix));
        Assert.Equal(expectedCount, json.RootElement.GetArrayLength());
        if (expectedCount > 0)
            Assert.Contains(target.CitaId, EventIds(json));
        foreach (var item in json.RootElement.EnumerateArray())
        {
            if (filter is "estado" or "paciente+estado" or "todos")
                Assert.Equal("Confirmada", item.GetProperty("estado").GetString());
            if (filter is "odontologo" or "todos")
                Assert.Equal(OtherDoctorId, item.GetProperty("odontologoId").GetString());
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("end=2030-02-02")]
    [InlineData("start=2030-02-01")]
    [InlineData("start=&end=2030-02-02")]
    [InlineData("start=2030-02-01&end=2030-02-01")]
    [InlineData("start=2030-02-02&end=2030-02-01")]
    [InlineData("start=2030-01-01&end=2030-03-05")]
    [InlineData("start=no-es-fecha&end=2030-02-02")]
    [InlineData("start=2030-02-01&end=2030-02-30")]
    [InlineData("start=01/02/2030&end=02/02/2030")]
    public async Task CalendarioHttp_IntervaloInvalido_Devuelve400(string query)
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = Client(factory);
        using var response = await client.GetAsync("/Citas/Calendario?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("2030-01-01", "2030-03-04")]
    [InlineData("0001-01-01", "0001-01-02")]
    [InlineData("9999-12-30", "9999-12-31T23:59:59")]
    public async Task CalendarioHttp_Admite62DiasYLimitesDateTimeSinDesbordar(string start, string end)
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = Client(factory);
        using var response = await client.GetAsync(RangeUrl(start, end));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("2030-02-01T10:00", "2030-02-01T11:00")]
    [InlineData("2030-02-01T10:00:00", "2030-02-01T11:00:00")]
    [InlineData("2030-02-01T10:00:00.0000000", "2030-02-01T11:00:00.0000000")]
    [InlineData("2030-02-01T10:00:00Z", "2030-02-01T11:00:00Z")]
    [InlineData("2030-02-01T10:00:00-04:00", "2030-02-01T11:00:00-04:00")]
    [InlineData("2030-02-01T10:00:00+09:00", "2030-02-01T11:00:00+09:00")]
    public async Task CalendarioHttp_FechaIsoConOSinOffset_ConservaHoraCivil(string start, string end)
    {
        using var factory = new CustomWebApplicationFactory();
        var seed = await SeedAsync(factory);
        var expected = seed.Appointment(RangeStart.AddHours(10));
        await SaveAsync(factory, expected, seed.Appointment(RangeStart.AddHours(1)), seed.Appointment(RangeStart.AddHours(14)));
        using var client = Client(factory);
        using var json = JsonDocument.Parse(await client.GetStringAsync(RangeUrl(start, end)));
        var item = Assert.Single(json.RootElement.EnumerateArray());
        Assert.Equal(expected.CitaId, item.GetProperty("id").GetInt32());
        Assert.Equal("2030-02-01T10:00:00", item.GetProperty("start").GetString());
        Assert.Equal("2030-02-01T10:30:00", item.GetProperty("end").GetString());
    }

    [Theory]
    [InlineData("texto")]
    [InlineData("1.5")]
    [InlineData("2147483648")]
    public async Task CalendarioHttp_PacienteIdMalformado_NoAmpliaConsulta(string patientId)
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = Client(factory);
        using var response = await client.GetAsync(DayUrl + "&pacienteId=" + patientId);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("2030-02-01Z", "2030-02-02Z")]
    [InlineData("2030-02-01T10:00:00+25:00", "2030-02-01T11:00:00+25:00")]
    [InlineData("2030-01-01T00:00:00", "2030-03-04T00:00:00.0000001")]
    public async Task CalendarioHttp_RechazaSufijoInvalidoYExcesoDeRangoSubsegundo(string start, string end)
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = Client(factory);
        using var response = await client.GetAsync(RangeUrl(start, end));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CalendarioHttp_ConservaPrecisionSubsegundoEnLimitesYSalida()
    {
        using var factory = new CustomWebApplicationFactory();
        var seed = await SeedAsync(factory);
        var appointment = seed.Appointment(RangeStart.AddHours(10).AddTicks(1234567));
        await SaveAsync(factory, appointment);
        using var client = Client(factory);
        using var included = JsonDocument.Parse(await client.GetStringAsync(
            RangeUrl("2030-02-01T10:30:00.1234566", "2030-02-01T11:00:00")));
        var item = Assert.Single(included.RootElement.EnumerateArray());
        Assert.Equal("2030-02-01T10:00:00.1234567", item.GetProperty("start").GetString());
        Assert.Equal("2030-02-01T10:30:00.1234567", item.GetProperty("end").GetString());
        using var excluded = JsonDocument.Parse(await client.GetStringAsync(
            RangeUrl("2030-02-01T10:30:00.1234567", "2030-02-01T11:00:00")));
        Assert.Empty(EventIds(excluded));
    }

    [Fact]
    public async Task CalendarioHttp_NoSeAlmacenaEnCacheYNoAceptaPost()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = Client(factory);
        using var get = await client.GetAsync(DayUrl);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.True(get.Headers.CacheControl?.NoStore);
        Assert.True(get.Headers.CacheControl?.NoCache);
        using var post = await client.PostAsync(DayUrl, new StringContent(string.Empty));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, post.StatusCode);
    }

    [Fact]
    public async Task CalendarioHttp_OdontologoSinIdentificador_NoVeCitasAjenas()
    {
        using var factory = new CustomWebApplicationFactory();
        using var missingIdFactory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddTransient<IClaimsTransformation, RemoveUserId>()));
        var seed = await SeedAsync(missingIdFactory);
        await SaveAsync(missingIdFactory, seed.Appointment(RangeStart.AddHours(10)));
        using var client = Client(missingIdFactory, "Odontologo");
        using var json = JsonDocument.Parse(await client.GetStringAsync(DayUrl));
        Assert.Empty(EventIds(json));
    }

    [Fact]
    public async Task CalendarioHttp_OdontologoNavegaAlDetallePropioYNoAlAjeno()
    {
        using var factory = new CustomWebApplicationFactory();
        var seed = await SeedAsync(factory);
        var own = seed.Appointment(RangeStart.AddHours(10));
        var other = seed.Appointment(RangeStart.AddHours(10), dentistId: OtherDoctorId);
        await SaveAsync(factory, own, other);
        using var client = Client(factory, "Odontologo");
        using var json = JsonDocument.Parse(await client.GetStringAsync(DayUrl));
        var item = Assert.Single(json.RootElement.EnumerateArray());
        using var ownDetails = await client.GetAsync(item.GetProperty("url").GetString());
        using var otherDetails = await client.GetAsync($"/Citas/Details/{other.CitaId}");
        Assert.Equal(HttpStatusCode.OK, ownDetails.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, otherDetails.StatusCode);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("calendario", true)]
    [InlineData("lista", false)]
    [InlineData("vista-desconocida", true)]
    public async Task CitasIndex_AlternaCalendarioYListaConEnlacesAccesibles(string? view, bool isCalendar)
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = Client(factory);
        var html = await client.GetStringAsync("/Citas" + (view is null ? string.Empty : "?vista=" + view));
        Assert.Equal(isCalendar, html.Contains("id=\"citas-calendario\"", StringComparison.Ordinal));
        var table = Tag(html, "div", "class", "appointment-table-card");
        Assert.Equal(isCalendar, table.Contains(" hidden", StringComparison.Ordinal));
        var selectedLink = Tag(html, "a", "data-calendar-view", isCalendar ? "calendario" : "lista");
        Assert.Contains("aria-current=\"page\"", selectedLink);
        var otherLink = Tag(html, "a", "data-calendar-view", isCalendar ? "lista" : "calendario");
        Assert.DoesNotContain("aria-current=", otherLink);
    }

    [Theory]
    [InlineData("Administrador")]
    [InlineData("Recepcionista")]
    public async Task CitasIndex_AlternanciaPreservaFiltrosYSeleccionHistorica(string role)
    {
        using var factory = new CustomWebApplicationFactory();
        var seed = await SeedAsync(factory);
        await SaveAsync(factory, seed.Appointment(RangeStart.AddHours(10), status: "No asistió",
            dentistId: OtherDoctorId, patientId: seed.InactivePatientId));
        using var client = Client(factory, role);
        var html = await client.GetStringAsync($"/Citas?vista=lista&fecha=2030-02-01&estado=No%20asisti%C3%B3&pacienteId={seed.InactivePatientId}&odontologoId={OtherDoctorId}");

        foreach (var view in new[] { "calendario", "lista" })
        {
            var link = Tag(html, "a", "data-calendar-view", view);
            var href = WebUtility.HtmlDecode(Regex.Match(link, "href=\"([^\"]+)\"").Groups[1].Value);
            var query = QueryHelpers.ParseQuery(new Uri(new Uri("https://localhost"), href).Query);
            Assert.Equal(view, query["vista"].ToString());
            Assert.Equal("2030-02-01", query["fecha"].ToString());
            Assert.Equal("No asistió", query["estado"].ToString());
            Assert.Equal(seed.InactivePatientId.ToString(), query["pacienteId"].ToString());
            Assert.Equal(OtherDoctorId, query["odontologoId"].ToString());
        }
        Assert.Contains("selected=\"selected\"", Tag(html, "option", "value", OtherDoctorId));
        Assert.Contains("value=\"Paciente Historico\"", Tag(html, "input", "id", "PacienteBusqueda"));
        Assert.Contains("Doctor Historico (inactivo)", WebUtility.HtmlDecode(html));
    }

    [Fact]
    public async Task CitasIndex_CatalogoIncluyeDoctorHistoricoSinRolYExcluyeInactivoSinCitas()
    {
        using var factory = new CustomWebApplicationFactory();
        var seed = await SeedAsync(factory);
        await SaveAsync(factory, seed.Appointment(RangeStart.AddHours(10), dentistId: OtherDoctorId));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.UserRoles.RemoveRange(await db.UserRoles.Where(role => role.UserId == OtherDoctorId).ToListAsync());
            db.Users.Add(new ApplicationUser { Id = "sin-citas", UserName = "sin-citas", Nombre = "Sin", Apellido = "Citas", Estado = false });
            await db.SaveChangesAsync();
        }
        using var client = Client(factory);
        var html = await client.GetStringAsync("/Citas");
        Assert.Contains($"value=\"{DoctorId}\"", html);
        Assert.Contains($"value=\"{OtherDoctorId}\"", html);
        Assert.DoesNotContain("value=\"sin-citas\"", html);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CitasIndex_OdontologoNoTieneSelectorNiVeCitasAjenasAunqueManipuleFiltro(bool administratorToo)
    {
        using var factory = new CustomWebApplicationFactory();
        using var selectedFactory = factory.WithWebHostBuilder(builder =>
        {
            if (administratorToo)
                builder.ConfigureTestServices(services => services.AddTransient<IClaimsTransformation, AddAdministratorRole>());
        });
        var seed = await SeedAsync(selectedFactory);
        var own = seed.Appointment(RangeStart.AddHours(10));
        var other = seed.Appointment(RangeStart.AddHours(10), dentistId: OtherDoctorId);
        await SaveAsync(selectedFactory, own, other);
        using var client = Client(selectedFactory, "Odontologo");
        var html = await client.GetStringAsync("/Citas?vista=lista&odontologoId=" + OtherDoctorId);
        Assert.DoesNotContain("id=\"odontologoId\"", html);
        Assert.Contains($"href=\"/Citas/Details/{own.CitaId}\"", html);
        Assert.DoesNotContain($"href=\"/Citas/Details/{other.CitaId}\"", html);
        Assert.DoesNotContain("odontologoId=", Tag(html, "a", "data-calendar-view", "calendario"));
    }

    [Fact]
    public async Task CitasIndex_ListaCombinaFiltrosPacienteEstadoFechaYOdontologo()
    {
        using var factory = new CustomWebApplicationFactory();
        var seed = await SeedAsync(factory);
        var target = seed.Appointment(RangeStart.AddHours(10), status: "Confirmada", dentistId: OtherDoctorId, patientId: seed.InactivePatientId);
        var excluded = new[]
        {
            seed.Appointment(RangeStart.AddHours(11), status: "Pendiente", dentistId: OtherDoctorId, patientId: seed.InactivePatientId),
            seed.Appointment(RangeStart.AddHours(12), status: "Confirmada", dentistId: DoctorId, patientId: seed.InactivePatientId),
            seed.Appointment(RangeStart.AddHours(13), status: "Confirmada", dentistId: OtherDoctorId),
            seed.Appointment(RangeStart.AddDays(1), status: "Confirmada", dentistId: OtherDoctorId, patientId: seed.InactivePatientId)
        };
        await SaveAsync(factory, [target, .. excluded]);
        using var client = Client(factory);
        var html = await client.GetStringAsync($"/Citas?vista=lista&fecha=2030-02-01&estado=Confirmada&pacienteId={seed.InactivePatientId}&odontologoId={OtherDoctorId}");
        Assert.Contains($"href=\"/Citas/Details/{target.CitaId}\"", html);
        foreach (var appointment in excluded)
            Assert.DoesNotContain($"href=\"/Citas/Details/{appointment.CitaId}\"", html);
    }

    [Fact]
    public async Task Calendario_NombresConHtmlSeSerializanYRenderizanComoTexto()
    {
        using var factory = new CustomWebApplicationFactory();
        var seed = await SeedAsync(factory);
        const string maliciousName = "<img src=x onerror=alert(1)>";
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.Pacientes.SingleAsync(patient => patient.PacienteId == seed.PatientId)).Nombre = maliciousName;
            (await db.Users.SingleAsync(user => user.Id == DoctorId)).Nombre = maliciousName;
            await db.SaveChangesAsync();
        }
        await SaveAsync(factory, seed.Appointment(RangeStart.AddHours(10)));
        using var client = Client(factory);
        var rawJson = await client.GetStringAsync(DayUrl);
        using var json = JsonDocument.Parse(rawJson);
        var item = Assert.Single(json.RootElement.EnumerateArray());
        Assert.StartsWith(maliciousName, item.GetProperty("title").GetString());
        Assert.StartsWith(maliciousName, item.GetProperty("odontologoNombre").GetString());
        Assert.DoesNotContain(maliciousName, rawJson);
        var html = await client.GetStringAsync($"/Citas?vista=lista&pacienteId={seed.PatientId}");
        Assert.DoesNotContain(maliciousName, html);
        Assert.Contains(HtmlEncoder.Default.Encode(maliciousName), html);
    }

    [Fact]
    public async Task CitasIndex_UsaFullCalendarLocalVersionadoYCargaTodosLosAssets()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = Client(factory);
        var html = await client.GetStringAsync("/Citas");
        var scripts = Regex.Matches(html, "<script[^>]+src=\"([^\"]+)\"")
            .Select(match => WebUtility.HtmlDecode(match.Groups[1].Value))
            .Where(src => src.Contains("fullcalendar", StringComparison.OrdinalIgnoreCase) || src.Contains("citas-calendario.js", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(new[]
        {
            "/lib/fullcalendar/core/index.global.min.js",
            "/lib/fullcalendar/daygrid/index.global.min.js",
            "/lib/fullcalendar/timegrid/index.global.min.js",
            "/lib/fullcalendar/core/locales/es.global.min.js",
            "/js/citas-calendario.js"
        }, scripts.Select(src => src.Split('?')[0]));
        foreach (var script in scripts)
        {
            Assert.Contains("?v=", script);
            using var response = await client.GetAsync(script);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotEmpty(await response.Content.ReadAsStringAsync());
        }
    }

    private static string Tag(string html, string element, string attribute, string value)
    {
        var tag = Regex.Match(html, $"<{element}\\b[^>]*\\b{attribute}=\"{Regex.Escape(value)}\"[^>]*>").Value;
        Assert.NotEmpty(tag);
        return tag;
    }

    private static string RangeUrl(string start, string end) =>
        "/Citas/Calendario?start=" + Uri.EscapeDataString(start) + "&end=" + Uri.EscapeDataString(end);

    private static int[] EventIds(JsonDocument document) => document.RootElement.EnumerateArray()
        .Select(item => item.GetProperty("id").GetInt32()).Order().ToArray();

    private static HttpClient Client(WebApplicationFactory<Program> factory, string? role = "Administrador")
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (role is not null)
        {
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
            client.DefaultRequestHeaders.Add("X-Test-UserId", DoctorId);
        }
        return client;
    }

    private static async Task<Seed> SeedAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var patient = new Paciente
        {
            Nombre = "Ana", Apellido = "Calendario", Cedula = "dato-privado-cedula", Telefono = "dato-privado-tel",
            Correo = "dato-privado@example.test", Direccion = "dato-privado-direccion"
        };
        var inactivePatient = new Paciente { Nombre = "Paciente", Apellido = "Historico", Estado = false };
        var service = new ServicioOdontologico { Nombre = "Servicio calendario", DuracionEstimadaMinutos = 60 };
        var subservice = new SubservicioOdontologico
        {
            Nombre = "Subservicio calendario", DuracionEstimadaMinutos = 60, ServicioOdontologico = service
        };
        var doctor = new ApplicationUser
        {
            Id = DoctorId, UserName = "doctor-calendario", Nombre = "Doctor", Apellido = "Calendario",
            Email = "dato-privado-doctor@example.test", PasswordHash = "dato-privado-hash"
        };
        var otherDoctor = new ApplicationUser
        {
            Id = OtherDoctorId, UserName = "doctor-calendario-inactivo", Nombre = "Doctor", Apellido = "Historico", Estado = false
        };
        var role = new IdentityRole("Odontologo") { NormalizedName = "ODONTOLOGO" };
        db.AddRange(patient, inactivePatient, service, subservice, doctor, otherDoctor, role);
        db.UserRoles.AddRange(
            new IdentityUserRole<string> { UserId = DoctorId, RoleId = role.Id },
            new IdentityUserRole<string> { UserId = OtherDoctorId, RoleId = role.Id });
        await db.SaveChangesAsync();
        return new Seed(patient.PacienteId, inactivePatient.PacienteId, service.ServicioOdontologicoId, subservice.SubservicioOdontologicoId);
    }

    private static async Task SaveAsync(WebApplicationFactory<Program> factory, params Cita[] appointments)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Citas.AddRange(appointments);
        await db.SaveChangesAsync();
    }

    private sealed record Seed(int PatientId, int InactivePatientId, int ServiceId, int SubserviceId)
    {
        public Cita Appointment(DateTime start, int? duration = 30, string status = "Pendiente", string? dentistId = null, int? patientId = null) => new()
        {
            PacienteId = patientId ?? PatientId, OdontologoId = dentistId ?? DoctorId,
            ServicioOdontologicoId = ServiceId, SubservicioOdontologicoId = SubserviceId,
            DuracionProgramadaMinutos = duration, FechaHoraInicio = start, EstadoCita = status,
            Observaciones = "dato-privado-observaciones"
        };
    }

    private sealed class AddAdministratorRole : IClaimsTransformation
    {
        public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            if (principal.IsInRole("Odontologo") && principal.Identity is ClaimsIdentity identity)
                identity.AddClaim(new Claim(ClaimTypes.Role, "Administrador"));
            return Task.FromResult(principal);
        }
    }

    private sealed class RemoveUserId : IClaimsTransformation
    {
        public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            if (principal.Identity is ClaimsIdentity identity)
                foreach (var claim in identity.FindAll(ClaimTypes.NameIdentifier).ToArray())
                    identity.RemoveClaim(claim);
            return Task.FromResult(principal);
        }
    }
}
