using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MSDentalSys.Data.Context;
using MSDentalSys.Data.Models;
using Xunit;

namespace MSDentalSys.Tests.Integration;

public class CitaFormTests
{
    [Fact]
    public async Task CreateGet_ConservaContratoYDistribuyeCamposSinIncluirAccionesEnLaRejilla()
    {
        using var factory = new CustomWebApplicationFactory();
        await PrepareAsync(factory);
        using var client = CreateClient(factory);
        var html = await GetHtmlAsync(client, "/Citas/Create");
        var form = AssertForm(html, "/Citas/Create");
        AssertCompactCardAndHeading(html, false);

        var grid = Element(form, "div", tag => Classes(tag).Contains("row"));
        Assert.Contains("g-3", Classes(OpeningTag(grid)));
        var columns = Elements(grid, "div", tag => Classes(tag).Contains("col-12")).ToArray();
        Assert.Equal(6, columns.Length);
        string[][] fields =
        [
            ["PacienteId", "PacienteNombre"], ["OdontologoId"],
            ["ServicioOdontologicoId"], ["SubservicioOdontologicoId"],
            ["FechaHoraInicio"], ["Observaciones"]
        ];
        for (var index = 0; index < columns.Length; index++)
        {
            Assert.Equal(index < 4, Classes(OpeningTag(columns[index])).Contains("col-md-6"));
            Assert.Equal(fields[index], OpeningTags(columns[index], "(?:input|select|textarea)")
                .Select(tag => Attribute(tag, "name")).Where(name => name != "__Invariant").ToArray());
            var field = fields[index][0];
            var control = Control(columns[index], field);
            Assert.Equal(field, Attribute(control, "name"));
            AssertValidationMessage(columns[index], field);
            var label = Element(columns[index], "label", tag => Attribute(tag, "for") ==
                (field == "PacienteId" ? "PacienteBusqueda" : field));
            Assert.NotEmpty(TextContent(label));
        }

        Assert.Equal("hidden", Attribute(Control(form, "PacienteId"), "type"));
        var search = Control(form, "PacienteBusqueda");
        Assert.Equal("PacienteNombre", Attribute(search, "name"));
        Assert.Equal("patient-search-results", Attribute(search, "aria-controls"));
        Assert.Equal("list", Attribute(search, "aria-autocomplete"));
        Assert.DoesNotContain(OpeningTags(form, "label"), tag => Attribute(tag, "for") == "PacienteId");
        Assert.Equal("Paciente", TextContent(Element(form, "label", tag => Attribute(tag, "for") == "PacienteBusqueda")));
        var results = OpeningTag(Element(form, "div", tag => Attribute(tag, "id") == "patient-search-results"));
        Assert.Equal("listbox", Attribute(results, "role"));
        Assert.True(HasAttribute(results, "hidden"));

        var subservice = Control(form, "SubservicioOdontologicoId");
        Assert.True(HasAttribute(subservice, "disabled"));
        Assert.Equal("/Subservicios/ParaCitas", Attribute(subservice, "data-endpoint"));
        Assert.Equal("subservicios-status", Attribute(subservice, "aria-describedby"));
        Assert.Equal("status", Attribute(OpeningTag(Element(form, "small", tag => Attribute(tag, "id") == "subservicios-status")), "role"));
        foreach (var field in fields.Take(5).Select(item => item[0]))
            Assert.NotEmpty(Attribute(Control(form, field), "data-val-required"));
        Assert.Equal("datetime-local", Attribute(Control(form, "FechaHoraInicio"), "type"));
        Assert.Equal("3", Attribute(Control(form, "Observaciones"), "rows"));
        Assert.Equal("300", Attribute(Control(form, "Observaciones"), "data-val-length-max"));
        Assert.DoesNotContain(OpeningTags(form, "(?:input|select|textarea)"), tag => Attribute(tag, "name") == "DuracionProgramadaMinutos");
        Assert.DoesNotContain(OpeningTags(form, "[a-z][a-z0-9]*"), tag => Attribute(tag, "role") == "tab");

        var actions = AssertActions(form, "/Citas", "Registrar cita");
        Assert.DoesNotContain(actions, grid);
        Assert.True(form.IndexOf(actions, StringComparison.Ordinal) > form.IndexOf(grid, StringComparison.Ordinal) + grid.Length);
        Assert.Contains("/js/citas-subservicios.js", html);
        Assert.Contains("/lib/jquery-validation-unobtrusive/dist/jquery.validate.unobtrusive.min.js", html);
    }

    [Fact]
    public async Task CreatePostInvalido_ConservaSeleccionValoresYErrorJuntoAlCampo()
    {
        using var factory = new CustomWebApplicationFactory();
        var seed = await PrepareAsync(factory);
        using var client = CreateClient(factory);
        var observations = "Notas & <pendientes> " + new string('x', 301);
        var values = new Dictionary<string, string>
        {
            ["PacienteId"] = seed.PacienteId.ToString(), ["PacienteNombre"] = seed.PacienteNombre,
            ["OdontologoId"] = seed.OdontologoId, ["ServicioOdontologicoId"] = seed.ServicioId.ToString(),
            ["SubservicioOdontologicoId"] = seed.SubservicioId.ToString(),
            ["FechaHoraInicio"] = "2030-03-15T14:30", ["Observaciones"] = observations
        };

        var form = AssertForm(await PostInvalidAsync(client, "/Citas/Create", values), "/Citas/Create");
        Assert.Equal(values["PacienteId"], Attribute(Control(form, "PacienteId"), "value"));
        Assert.Equal(seed.PacienteNombre, Attribute(Control(form, "PacienteBusqueda"), "value"));
        foreach (var field in new[] { "OdontologoId", "ServicioOdontologicoId", "SubservicioOdontologicoId" })
        {
            var select = Element(form, "select", tag => Attribute(tag, "id") == field);
            var selected = Assert.Single(OpeningTags(select, "option"), tag => HasAttribute(tag, "selected"));
            Assert.Equal(values[field], Attribute(selected, "value"));
        }
        Assert.False(HasAttribute(Control(form, "SubservicioOdontologicoId"), "disabled"));
        Assert.Contains("Procedimiento de prueba — 30 min", WebUtility.HtmlDecode(form));
        Assert.Equal(values["FechaHoraInicio"], Attribute(Control(form, "FechaHoraInicio"), "value"));
        Assert.Equal(observations, TextContent(Element(form, "textarea", tag => Attribute(tag, "id") == "Observaciones")));
        Assert.Contains("300", AssertFieldError(form, "Observaciones"));
        Assert.DoesNotContain("validation-summary-errors", form);
        AssertActions(form, "/Citas", "Registrar cita");

        using var scope = factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Citas.ToListAsync());
    }

    [Fact]
    public async Task RescheduleGet_ConservaFormularioConUnSoloCampoEditableYAcciones()
    {
        using var factory = new CustomWebApplicationFactory();
        var seed = await PrepareAsync(factory, appointment: true);
        using var client = CreateClient(factory);
        var url = $"/Citas/Reschedule/{seed.CitaId}";
        var html = await GetHtmlAsync(client, url);
        var form = AssertForm(html, url);
        AssertCompactCardAndHeading(html, true);

        var id = Control(form, "CitaId");
        Assert.Equal("CitaId", Attribute(id, "name"));
        Assert.Equal("hidden", Attribute(id, "type"));
        Assert.Equal(seed.CitaId.ToString(), Attribute(id, "value"));
        var editable = Assert.Single(OpeningTags(form, "(?:input|select|textarea)"), tag => Attribute(tag, "type") != "hidden");
        Assert.Equal("FechaHoraInicio", Attribute(editable, "id"));
        Assert.Equal("FechaHoraInicio", Attribute(editable, "name"));
        Assert.Equal("datetime-local", Attribute(editable, "type"));
        Assert.Contains("form-control", Classes(editable));
        Assert.NotEmpty(Attribute(editable, "data-val-required"));
        Assert.Equal(seed.FechaHoraInicio, DateTime.Parse(Attribute(editable, "value"), CultureInfo.InvariantCulture));
        Assert.Single(OpeningTags(form, "label"), tag => Attribute(tag, "for") == "FechaHoraInicio");
        AssertValidationMessage(form, "FechaHoraInicio");
        Assert.DoesNotContain(OpeningTags(form, "div"), tag => Classes(tag).Contains("row"));
        AssertActions(form, $"/Citas/Details/{seed.CitaId}", "Guardar nueva fecha");
        Assert.Contains("/lib/jquery-validation-unobtrusive/dist/jquery.validate.unobtrusive.min.js", html);
    }

    [Fact]
    public async Task ReschedulePostInvalido_ConservaIdentificadorValorIntentadoYErrorSinGuardar()
    {
        using var factory = new CustomWebApplicationFactory();
        var seed = await PrepareAsync(factory, appointment: true);
        using var client = CreateClient(factory);
        var url = $"/Citas/Reschedule/{seed.CitaId}";
        var values = new Dictionary<string, string>
        {
            ["CitaId"] = seed.CitaId.ToString(), ["FechaHoraInicio"] = "fecha-invalida"
        };

        var form = AssertForm(await PostInvalidAsync(client, url, values), url);
        Assert.Equal(values["CitaId"], Attribute(Control(form, "CitaId"), "value"));
        Assert.Equal(values["FechaHoraInicio"], Attribute(Control(form, "FechaHoraInicio"), "value"));
        AssertFieldError(form, "FechaHoraInicio");
        Assert.DoesNotContain("validation-summary-errors", form);
        AssertActions(form, $"/Citas/Details/{seed.CitaId}", "Guardar nueva fecha");

        using var scope = factory.Services.CreateScope();
        var saved = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Citas.SingleAsync();
        Assert.Equal(seed.CitaId, saved.CitaId);
        Assert.Equal(seed.FechaHoraInicio, saved.FechaHoraInicio);
        Assert.Equal(seed.PacienteId, saved.PacienteId);
        Assert.Equal(seed.OdontologoId, saved.OdontologoId);
        Assert.Equal(seed.ServicioId, saved.ServicioOdontologicoId);
        Assert.Equal(seed.SubservicioId, saved.SubservicioOdontologicoId);
        Assert.Equal(30, saved.DuracionProgramadaMinutos);
        Assert.Equal("Pendiente", saved.EstadoCita);
        Assert.Equal("Observaciones originales", saved.Observaciones);
    }

    private static HttpClient CreateClient(CustomWebApplicationFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Test-Role", "Administrador");
        return client;
    }

    private static async Task<SeedData> PrepareAsync(CustomWebApplicationFactory factory, bool appointment = false)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var patient = new Paciente { Nombre = "Ana & Elena", Apellido = "Pérez", Estado = true };
        var dentist = new ApplicationUser { Id = "dentist-form", UserName = "dentist-form", Nombre = "Doctor", Apellido = "Formulario", Estado = true };
        var role = new IdentityRole("Odontologo") { NormalizedName = "ODONTOLOGO" };
        var service = new ServicioOdontologico { Nombre = "Servicio de prueba", Estado = true };
        var subservice = new SubservicioOdontologico
        {
            Nombre = "Procedimiento de prueba", ServicioOdontologico = service,
            DuracionEstimadaMinutos = 30, Estado = true
        };
        db.AddRange(patient, dentist, role, subservice);
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = dentist.Id, RoleId = role.Id });
        await db.SaveChangesAsync();
        var date = new DateTime(2030, 3, 1, 9, 0, 0);
        var cita = new Cita
        {
            PacienteId = patient.PacienteId, OdontologoId = dentist.Id,
            ServicioOdontologicoId = service.ServicioOdontologicoId,
            SubservicioOdontologicoId = subservice.SubservicioOdontologicoId,
            DuracionProgramadaMinutos = 30, FechaHoraInicio = date,
            EstadoCita = "Pendiente", Observaciones = "Observaciones originales"
        };
        if (appointment)
        {
            db.Citas.Add(cita);
            await db.SaveChangesAsync();
        }
        return new SeedData(patient.PacienteId, patient.Nombre + " " + patient.Apellido, dentist.Id,
            service.ServicioOdontologicoId, subservice.SubservicioOdontologicoId, cita.CitaId, date);
    }

    private sealed record SeedData(int PacienteId, string PacienteNombre, string OdontologoId,
        int ServicioId, int SubservicioId, int CitaId, DateTime FechaHoraInicio);

    private static async Task<string> GetHtmlAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<string> PostInvalidAsync(HttpClient client, string url, Dictionary<string, string> values)
    {
        var form = AssertForm(await GetHtmlAsync(client, url), url);
        values["__RequestVerificationToken"] = Attribute(Assert.Single(OpeningTags(form, "input"),
            tag => Attribute(tag, "name") == "__RequestVerificationToken"), "value");
        var response = await client.PostAsync(url, new FormUrlEncodedContent(values));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static string AssertForm(string html, string action)
    {
        var form = Element(html, "form", tag => Attribute(tag, "action") == action);
        Assert.Single(OpeningTags(html, "form"), tag => Attribute(tag, "action").StartsWith("/Citas/", StringComparison.Ordinal));
        Assert.Single(OpeningTags(form, "form"));
        Assert.Equal("post", Attribute(OpeningTag(form), "method"));
        var token = Assert.Single(OpeningTags(form, "input"), tag => Attribute(tag, "name") == "__RequestVerificationToken");
        Assert.Equal("hidden", Attribute(token, "type"));
        Assert.NotEmpty(Attribute(token, "value"));
        // ModelOnly must not become an All summary that duplicates property errors.
        Assert.DoesNotContain(OpeningTags(form, "div"), tag => Attribute(tag, "data-valmsg-summary") == "true");
        return form;
    }

    private static void AssertCompactCardAndHeading(string html, bool reschedule)
    {
        var card = Assert.Single(OpeningTags(html, "div"), tag => Classes(tag).Contains("appointment-form-card"));
        Assert.Contains("p-3", Classes(card));
        Assert.Contains("p-md-4", Classes(card));
        Assert.Equal(reschedule, Classes(card).Contains("appointment-reschedule-card"));
        var heading = Assert.Single(OpeningTags(html, "div"), tag => Classes(tag).Contains("page-heading"));
        Assert.Contains("mb-3", Classes(heading));
    }

    private static string AssertActions(string form, string cancelUrl, string submitText)
    {
        var actions = Element(form, "div", tag => Classes(tag).Contains("justify-content-end"));
        Assert.Contains("flex-wrap", Classes(OpeningTag(actions)));
        Assert.Contains("mt-3", Classes(OpeningTag(actions)));
        Assert.Equal("Cancelar", TextContent(Element(actions, "a", tag => Attribute(tag, "href") == cancelUrl)));
        Assert.Equal(submitText, TextContent(Element(actions, "button", tag => Attribute(tag, "type") == "submit")));
        Assert.Single(OpeningTags(form, "button"), tag => Attribute(tag, "type") == "submit");
        return actions;
    }

    private static string AssertValidationMessage(string form, string field)
    {
        var span = Element(form, "span", tag => Attribute(tag, "data-valmsg-for") == field);
        Assert.Equal("true", Attribute(OpeningTag(span), "data-valmsg-replace"));
        return span;
    }

    private static string AssertFieldError(string form, string field)
    {
        var span = AssertValidationMessage(form, field);
        Assert.Contains("field-validation-error", Classes(OpeningTag(span)));
        Assert.Contains("input-validation-error", Classes(Control(form, field)));
        var text = TextContent(span);
        Assert.NotEmpty(text);
        return text;
    }

    private static string Control(string html, string id) =>
        Assert.Single(OpeningTags(html, "(?:input|select|textarea)"), tag => Attribute(tag, "id") == id);

    private static IEnumerable<string> OpeningTags(string html, string tagPattern) =>
        Regex.Matches(html, $"<{tagPattern}\\b[^>]*>", RegexOptions.IgnoreCase).Select(match => match.Value);

    private static string OpeningTag(string element) => element[..(element.IndexOf('>') + 1)];

    private static string Attribute(string tag, string name) => WebUtility.HtmlDecode(
        Regex.Match(tag, $"\\s{Regex.Escape(name)}=\"([^\"]*)\"", RegexOptions.IgnoreCase).Groups[1].Value);

    private static bool HasAttribute(string tag, string name) =>
        Regex.IsMatch(tag, $"\\s{Regex.Escape(name)}(?:\\s|=|/?>)", RegexOptions.IgnoreCase);

    private static string[] Classes(string tag) => Attribute(tag, "class").Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static string TextContent(string element) => WebUtility.HtmlDecode(Regex.Replace(element, "<[^>]*>", "")).Trim();

    private static string Element(string html, string tagName, Func<string, bool> predicate) =>
        Assert.Single(Elements(html, tagName, predicate));

    // Balanced extraction keeps nested field wrappers and action placement observable.
    private static IEnumerable<string> Elements(string html, string tagName, Func<string, bool> predicate)
    {
        foreach (Match opening in Regex.Matches(html, $"<{tagName}\\b[^>]*>", RegexOptions.IgnoreCase))
        {
            if (!predicate(opening.Value)) continue;
            var depth = 0;
            var length = 0;
            foreach (Match tag in Regex.Matches(html[opening.Index..], $"<(/?){tagName}\\b[^>]*>", RegexOptions.IgnoreCase))
            {
                depth += tag.Groups[1].Value.Length == 0 ? 1 : -1;
                if (depth != 0) continue;
                length = tag.Index + tag.Length;
                break;
            }
            if (length == 0) throw new InvalidOperationException($"Elemento {tagName} sin cierre.");
            yield return html.Substring(opening.Index, length);
        }
    }
}
