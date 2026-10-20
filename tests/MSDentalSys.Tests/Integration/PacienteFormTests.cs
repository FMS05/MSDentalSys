using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MSDentalSys.Data.Context;
using MSDentalSys.Data.Models;
using Xunit;

namespace MSDentalSys.Tests.Integration;

public class PacienteFormTests
{
    private static readonly (string Key, string Label, string[] Fields)[] Tabs =
    [
        ("datos", "Datos personales", ["Nombre", "Apellido", "Cedula", "FechaNacimiento", "Sexo", "Telefono", "Correo", "Direccion"]),
        ("seguro", "Seguro y emergencia", ["TieneSeguro", "SeguroId", "ContactoEmergencia", "TelefonoEmergencia"]),
        ("antecedentes", "Antecedentes clínicos", ["Alergias", "EnfermedadesSistemicas", "MedicamentosActuales", "CirugiasPrevias", "HabitosRelevantes", "Embarazo", "Observaciones"])
    ];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Formulario_ComparteTresPestanasDentroDeUnSoloFormulario(bool edit)
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);
        var (url, pacienteId, _) = await PrepareAsync(factory, edit);
        var html = await GetHtmlAsync(client, url);
        var form = Element(html, "form", "id", "paciente-form");

        Assert.Single(OpeningTags(form, "form"));
        Assert.Equal("post", Attribute(OpeningTag(form), "method"));
        Assert.Equal(url, Attribute(OpeningTag(form), "action"));
        Assert.DoesNotContain("/Account/Logout", form);
        Assert.Single(OpeningTags(html, "form"), tag => Attribute(tag, "action").StartsWith("/Pacientes/", StringComparison.Ordinal));
        var token = Assert.Single(OpeningTags(form, "input"), tag => Attribute(tag, "name") == "__RequestVerificationToken");
        Assert.Equal("hidden", Attribute(token, "type"));
        Assert.NotEmpty(Attribute(token, "value"));
        if (edit)
        {
            var id = OpeningTag(Element(form, "input", "id", "PacienteId"));
            Assert.Equal("hidden", Attribute(id, "type"));
            Assert.Equal(pacienteId.ToString(), Attribute(id, "value"));
        }

        Assert.Equal(3, OpeningTags(form, "button").Count(tag => Attribute(tag, "role") == "tab"));
        Assert.Equal(3, OpeningTags(form, "div").Count(tag => Attribute(tag, "role") == "tabpanel"));
        AssertActiveTab(form, "datos");
        foreach (var tab in Tabs)
        {
            var button = Element(form, "button", "id", $"paciente-{tab.Key}-tab");
            var buttonTag = OpeningTag(button);
            Assert.Equal("button", Attribute(buttonTag, "type"));
            Assert.Equal("tab", Attribute(buttonTag, "role"));
            Assert.Equal("tab", Attribute(buttonTag, "data-bs-toggle"));
            Assert.Equal($"#paciente-{tab.Key}-panel", Attribute(buttonTag, "data-bs-target"));
            Assert.Equal($"paciente-{tab.Key}-panel", Attribute(buttonTag, "aria-controls"));
            Assert.Contains(tab.Label, WebUtility.HtmlDecode(button));

            var panel = Element(form, "div", "id", $"paciente-{tab.Key}-panel");
            Assert.Equal("tabpanel", Attribute(OpeningTag(panel), "role"));
            Assert.Equal($"paciente-{tab.Key}-tab", Attribute(OpeningTag(panel), "aria-labelledby"));
            var fields = OpeningTags(panel, "(?:input|select|textarea)")
                .Where(tag => Attribute(tag, "type") != "hidden")
                .Select(tag => Attribute(tag, "name")).Where(name => name.Length > 0).Distinct().ToArray();
            Assert.Equal(tab.Fields, fields);
            foreach (var field in tab.Fields.Where(field => field != "TieneSeguro"))
            {
                var control = Assert.Single(OpeningTags(panel, "(?:input|select|textarea)"), tag => Attribute(tag, "name") == field);
                Assert.Equal(field, Attribute(control, "id"));
                Assert.Single(OpeningTags(panel, "span"), tag => Attribute(tag, "data-valmsg-for") == field);
            }
            Assert.DoesNotContain(OpeningTags(panel, "button"), tag => Attribute(tag, "type") == "submit");
            Assert.DoesNotContain("data-valmsg-summary", panel);
            Assert.DoesNotContain(OpeningTags(panel, "a"), tag => Attribute(tag, "href") == "/Pacientes");
        }
        var ids = OpeningTags(form, "[a-z][a-z0-9]*").Select(tag => Attribute(tag, "id")).Where(id => id.Length > 0).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.Single(OpeningTags(form, "button"), tag => Attribute(tag, "type") == "submit");
        Assert.Contains(edit ? "Guardar cambios" : "Registrar paciente", form);
        Assert.Contains(OpeningTags(form, "a"), tag => Attribute(tag, "href") == "/Pacientes");
        // ModelOnly no genera el resumen cliente de errores de propiedades.
        Assert.DoesNotContain(OpeningTags(form, "div"), tag => Attribute(tag, "data-valmsg-summary") == "true");
        Assert.Contains("/js/pacientes-form.js", html);
        var invariant = Assert.Single(OpeningTags(form, "input"), tag => Attribute(tag, "name") == "__Invariant");
        Assert.Equal("hidden", Attribute(invariant, "type"));
        Assert.Equal("FechaNacimiento", Attribute(invariant, "value"));

        var cedula = OpeningTag(Element(form, "input", "id", "Cedula"));
        Assert.Equal("numeric", Attribute(cedula, "inputmode"));
        Assert.Equal("text", Attribute(cedula, "type"));
        Assert.Equal("13", Attribute(cedula, "maxlength"));
        Assert.Equal("false", Attribute(cedula, "aria-required"));
        Assert.False(HasAttribute(cedula, "required"));
        Assert.False(HasAttribute(cedula, "data-val-required"));
        Assert.Contains("id=\"cedula-required\"", form);
        Assert.Contains("id=\"cedula-help\"", form);
        Assert.NotEmpty(Attribute(OpeningTag(Element(form, "input", "id", "Nombre")), "data-val-required"));
        Assert.Equal("300", Attribute(OpeningTag(Element(form, "textarea", "id", "Alergias")), "data-val-length-max"));
        Assert.Equal("500", Attribute(OpeningTag(Element(form, "textarea", "id", "Observaciones")), "data-val-length-max"));
    }

    [Theory]
    [InlineData(false, "Nombre", "datos")]
    [InlineData(true, "Nombre", "datos")]
    [InlineData(false, "SeguroId", "seguro")]
    [InlineData(true, "SeguroId", "seguro")]
    [InlineData(false, "Alergias", "antecedentes")]
    [InlineData(true, "Alergias", "antecedentes")]
    public async Task PostInvalido_ActivaPestanaDelErrorYConservaValores(bool edit, string invalidField, string activeTab)
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);
        var (url, pacienteId, seguroId) = await PrepareAsync(factory, edit);
        var values = ValidValues(pacienteId, seguroId);
        values[invalidField] = invalidField == "Alergias" ? new string('A', 301) : "";

        var html = await PostAsync(client, url, values, HttpStatusCode.OK);
        var form = Element(html, "form", "id", "paciente-form");
        AssertActiveTab(form, activeTab);
        AssertFieldError(form, invalidField);
        AssertValues(form, values);
        // ModelOnly omite el resumen cuando únicamente existen errores de propiedad.
        Assert.DoesNotContain(OpeningTags(form, "div"), tag => Attribute(tag, "data-valmsg-summary") == "true");
        Assert.DoesNotContain("validation-summary-errors", form);
        await AssertNoSaveAsync(factory, edit, pacienteId);
    }

    [Theory]
    [InlineData(false, true, "datos")]
    [InlineData(true, true, "datos")]
    [InlineData(false, false, "seguro")]
    [InlineData(true, false, "seguro")]
    public async Task PostConVariosErrores_PriorizaOrdenVisualDePestanas(bool edit, bool personalError, string activeTab)
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);
        var (url, pacienteId, seguroId) = await PrepareAsync(factory, edit);
        var values = ValidValues(pacienteId, seguroId);
        // La anotación de Alergias se valida antes de que el controlador agregue el error de SeguroId.
        values["Alergias"] = new string('A', 301);
        values["SeguroId"] = "";
        if (personalError) values["Nombre"] = "";

        var form = Element(await PostAsync(client, url, values, HttpStatusCode.OK), "form", "id", "paciente-form");
        AssertActiveTab(form, activeTab);
        AssertFieldError(form, "SeguroId");
        AssertFieldError(form, "Alergias");
        if (personalError) AssertFieldError(form, "Nombre");
        AssertValues(form, values);
        await AssertNoSaveAsync(factory, edit, pacienteId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CedulaDuplicada_ReenviaErrorEnDatosYConservaTodoElFormulario(bool edit)
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);
        var (url, pacienteId, seguroId) = await PrepareAsync(factory, edit);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Pacientes.Add(new Paciente { Nombre = "Existente", Apellido = "Duplicado", Cedula = "001-1234567-8" });
            await db.SaveChangesAsync();
        }
        var values = ValidValues(pacienteId, seguroId);
        values["Cedula"] = "00112345678";

        var html = await PostAsync(client, url, values, HttpStatusCode.OK);
        var form = Element(html, "form", "id", "paciente-form");
        AssertActiveTab(form, "datos");
        Assert.Contains("otro paciente", AssertFieldError(form, "Cedula"));
        Assert.Contains("input-validation-error", OpeningTag(Element(form, "input", "id", "Cedula")));
        AssertValues(form, values);
        using var verification = factory.Services.CreateScope();
        var context = verification.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(edit ? 2 : 1, await context.Pacientes.CountAsync());
        if (edit) Assert.Equal("Original", (await context.Pacientes.FindAsync(pacienteId))!.Nombre);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostValido_GuardaCamposDeLasTresPestanas(bool edit)
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);
        var (url, pacienteId, seguroId) = await PrepareAsync(factory, edit);
        var values = ValidValues(pacienteId, seguroId);
        await PostAsync(client, url, values, HttpStatusCode.Redirect);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var paciente = await db.Pacientes.Include(item => item.AntecedenteClinico).SingleAsync();
        if (edit) Assert.Equal(pacienteId, paciente.PacienteId);
        Assert.Equal(values["Nombre"], paciente.Nombre);
        Assert.Equal(values["Apellido"], paciente.Apellido);
        Assert.Equal(values["Cedula"], paciente.Cedula);
        Assert.Equal(new DateTime(2010, 7, 12), paciente.FechaNacimiento);
        Assert.Equal(values["Sexo"], paciente.Sexo);
        Assert.Equal(values["Telefono"], paciente.Telefono);
        Assert.Equal(values["Correo"], paciente.Correo);
        Assert.Equal(values["Direccion"], paciente.Direccion);
        Assert.Equal(seguroId, paciente.SeguroId);
        Assert.Equal(values["ContactoEmergencia"], paciente.ContactoEmergencia);
        Assert.Equal(values["TelefonoEmergencia"], paciente.TelefonoEmergencia);
        var antecedentes = Assert.IsType<AntecedenteClinico>(paciente.AntecedenteClinico);
        Assert.Equal(values["Alergias"], antecedentes.Alergias);
        Assert.Equal(values["EnfermedadesSistemicas"], antecedentes.EnfermedadesSistemicas);
        Assert.Equal(values["MedicamentosActuales"], antecedentes.MedicamentosActuales);
        Assert.Equal(values["CirugiasPrevias"], antecedentes.CirugiasPrevias);
        Assert.Equal(values["HabitosRelevantes"], antecedentes.HabitosRelevantes);
        Assert.True(antecedentes.Embarazo);
        Assert.Equal(values["Observaciones"], antecedentes.Observaciones);
        Assert.Single(await db.AntecedentesClinicos.ToListAsync());
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("")]
    public async Task Edit_ConservaValoresDeTodasLasPestanasYSuSeguroActualInactivo(string embarazo)
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);
        var (_, _, seguroId) = await PrepareAsync(factory, false);
        var values = ValidValues(0, seguroId);
        values["Embarazo"] = embarazo;
        await PostAsync(client, "/Pacientes/Create", values, HttpStatusCode.Redirect);

        int pacienteId;
        int antecedenteId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var paciente = await db.Pacientes.Include(item => item.AntecedenteClinico).SingleAsync();
            pacienteId = paciente.PacienteId;
            antecedenteId = paciente.AntecedenteClinico!.AntecedenteClinicoId;
            (await db.Seguros.FindAsync(seguroId))!.Estado = false;
            await db.SaveChangesAsync();
        }

        values["PacienteId"] = pacienteId.ToString();
        var url = $"/Pacientes/Edit/{pacienteId}";
        var form = Element(await GetHtmlAsync(client, url), "form", "id", "paciente-form");
        AssertActiveTab(form, "datos");
        AssertValues(form, values);
        Assert.False(HasAttribute(OpeningTag(Element(form, "div", "id", "seguro-selector")), "hidden"));
        var seguro = Assert.Single(OpeningTags(Element(form, "select", "id", "SeguroId"), "option"),
            tag => HasAttribute(tag, "selected"));
        Assert.Equal(seguroId.ToString(), Attribute(seguro, "value"));
        Assert.DoesNotContain("field-validation-error", form);

        // Guardar los valores existentes admite su propia cédula y su seguro actual inactivo.
        await PostAsync(client, url, values, HttpStatusCode.Redirect);
        AssertValues(Element(await GetHtmlAsync(client, url), "form", "id", "paciente-form"), values);
        using var verification = factory.Services.CreateScope();
        var context = verification.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var saved = await context.Pacientes.Include(item => item.AntecedenteClinico).SingleAsync();
        Assert.Equal(pacienteId, saved.PacienteId);
        Assert.Equal(seguroId, saved.SeguroId);
        Assert.Equal(antecedenteId, saved.AntecedenteClinico!.AntecedenteClinicoId);
        Assert.Equal(embarazo == "" ? (bool?)null : bool.Parse(embarazo), saved.AntecedenteClinico.Embarazo);
    }

    [Theory]
    [InlineData(false, "Masculino")]
    [InlineData(true, "Masculino")]
    [InlineData(false, "Otro")]
    [InlineData(true, "Otro")]
    [InlineData(false, "")]
    [InlineData(true, "")]
    public async Task SinSeguroYSexoNoFemenino_ConservaNormalizacionCondicional(bool edit, string sexo)
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = CreateClient(factory);
        var (url, pacienteId, seguroId) = await PrepareAsync(factory, edit);
        var values = ValidValues(pacienteId, seguroId);
        values["TieneSeguro"] = "false";
        values["SeguroId"] = int.MaxValue.ToString();
        values["Sexo"] = sexo;
        values["Embarazo"] = "true";

        await PostAsync(client, url, values, HttpStatusCode.Redirect);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var saved = await db.Pacientes.Include(item => item.AntecedenteClinico).SingleAsync();
            if (edit) Assert.Equal(pacienteId, saved.PacienteId);
            pacienteId = saved.PacienteId;
            Assert.Null(saved.SeguroId);
            Assert.Null(saved.AntecedenteClinico!.Embarazo);
        }

        values["SeguroId"] = "";
        values["Embarazo"] = "";
        var form = Element(await GetHtmlAsync(client, $"/Pacientes/Edit/{pacienteId}"), "form", "id", "paciente-form");
        AssertValues(form, values);
        Assert.True(HasAttribute(OpeningTag(Element(form, "div", "id", "seguro-selector")), "hidden"));
        Assert.DoesNotContain("field-validation-error", form);
    }

    private static HttpClient CreateClient(CustomWebApplicationFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Test-Role", "Administrador");
        return client;
    }

    private static async Task<(string Url, int PacienteId, int SeguroId)> PrepareAsync(CustomWebApplicationFactory factory, bool edit)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var seguro = new Seguro { Nombre = "Seguro de prueba", Estado = true };
        db.Seguros.Add(seguro);
        var paciente = new Paciente
        {
            Nombre = "Original", Apellido = "Sin cambios", Sexo = "Masculino",
            AntecedenteClinico = new AntecedenteClinico { Observaciones = "Original" }
        };
        if (edit) db.Pacientes.Add(paciente);
        await db.SaveChangesAsync();
        return (edit ? $"/Pacientes/Edit/{paciente.PacienteId}" : "/Pacientes/Create", paciente.PacienteId, seguro.SeguroId);
    }

    private static Dictionary<string, string> ValidValues(int pacienteId, int seguroId) => new()
    {
        ["PacienteId"] = pacienteId.ToString(), ["Nombre"] = "Ana & Elena", ["Apellido"] = "Pérez",
        ["Cedula"] = "001-1234567-8", ["FechaNacimiento"] = "2010-07-12", ["Sexo"] = "Femenino",
        ["Telefono"] = "8095550101", ["Correo"] = "ana@example.test", ["Direccion"] = "Calle <Principal> 12",
        ["TieneSeguro"] = "true", ["SeguroId"] = seguroId.ToString(),
        ["ContactoEmergencia"] = "María Pérez", ["TelefonoEmergencia"] = "8095550102",
        ["Alergias"] = "Alergia de prueba", ["EnfermedadesSistemicas"] = "Enfermedad de prueba",
        ["MedicamentosActuales"] = "Medicamento de prueba", ["CirugiasPrevias"] = "Cirugía de prueba",
        ["HabitosRelevantes"] = "Hábito de prueba", ["Embarazo"] = "true", ["Observaciones"] = "Observación <clínica> & adicional"
    };

    private static async Task<string> GetHtmlAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<string> PostAsync(HttpClient client, string url, Dictionary<string, string> values, HttpStatusCode expectedStatus)
    {
        var form = Element(await GetHtmlAsync(client, url), "form", "id", "paciente-form");
        var token = Assert.Single(OpeningTags(form, "input"), tag => Attribute(tag, "name") == "__RequestVerificationToken");
        var posted = new Dictionary<string, string>(values) { ["__RequestVerificationToken"] = Attribute(token, "value") };
        var response = await client.PostAsync(url, new FormUrlEncodedContent(posted));
        Assert.Equal(expectedStatus, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task AssertNoSaveAsync(CustomWebApplicationFactory factory, bool edit, int pacienteId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(edit ? 1 : 0, await db.Pacientes.CountAsync());
        if (edit)
        {
            var paciente = await db.Pacientes.Include(item => item.AntecedenteClinico).SingleAsync();
            Assert.Equal(pacienteId, paciente.PacienteId);
            Assert.Equal("Original", paciente.Nombre);
            Assert.Equal("Original", paciente.AntecedenteClinico!.Observaciones);
        }
    }

    private static void AssertActiveTab(string form, string activeTab)
    {
        foreach (var tab in Tabs)
        {
            var active = tab.Key == activeTab;
            var button = OpeningTag(Element(form, "button", "id", $"paciente-{tab.Key}-tab"));
            var panel = OpeningTag(Element(form, "div", "id", $"paciente-{tab.Key}-panel"));
            Assert.Equal(active ? "true" : "false", Attribute(button, "aria-selected"));
            Assert.Equal(active, Attribute(button, "class").Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("active"));
            Assert.Equal(active, Attribute(panel, "class").Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("active"));
        }
    }

    private static string AssertFieldError(string form, string field)
    {
        var span = Element(form, "span", "data-valmsg-for", field);
        Assert.Contains("field-validation-error", OpeningTag(span));
        var text = WebUtility.HtmlDecode(Regex.Replace(span, "<[^>]*>", "")).Trim();
        Assert.NotEmpty(text);
        return text;
    }

    private static void AssertValues(string form, Dictionary<string, string> values)
    {
        foreach (var field in Tabs.SelectMany(tab => tab.Fields))
        {
            var controls = OpeningTags(form, "(?:input|select|textarea)").Where(tag => Attribute(tag, "name") == field).ToArray();
            string actual;
            if (field == "TieneSeguro") actual = Attribute(Assert.Single(controls, tag => HasAttribute(tag, "checked")), "value");
            else
            {
                var control = Assert.Single(controls);
                if (control.StartsWith("<textarea", StringComparison.OrdinalIgnoreCase))
                {
                    var element = Element(form, "textarea", "name", field);
                    actual = WebUtility.HtmlDecode(element[(element.IndexOf('>') + 1)..element.LastIndexOf('<')]).TrimStart('\r', '\n');
                }
                else if (control.StartsWith("<select", StringComparison.OrdinalIgnoreCase))
                {
                    var options = OpeningTags(Element(form, "select", "name", field), "option").ToArray();
                    actual = Attribute(options.FirstOrDefault(tag => HasAttribute(tag, "selected")) ?? options[0], "value");
                }
                else actual = Attribute(control, "value");
            }
            Assert.Equal(values[field], actual);
        }
    }

    private static IEnumerable<string> OpeningTags(string html, string tagPattern) =>
        Regex.Matches(html, $"<{tagPattern}\\b[^>]*>", RegexOptions.IgnoreCase).Select(match => match.Value);

    private static string OpeningTag(string element) => element[..(element.IndexOf('>') + 1)];

    private static string Attribute(string tag, string name) => WebUtility.HtmlDecode(
        Regex.Match(tag, $"\\s{Regex.Escape(name)}=\"([^\"]*)\"", RegexOptions.IgnoreCase).Groups[1].Value);

    private static bool HasAttribute(string tag, string name) =>
        Regex.IsMatch(tag, $"\\s{Regex.Escape(name)}(?:\\s|=|/?>)", RegexOptions.IgnoreCase);

    // Locate balanced elements so nested Bootstrap rows do not truncate a panel.
    private static string Element(string html, string tagName, string attribute, string value)
    {
        var opening = Assert.Single(Regex.Matches(html, $"<{tagName}\\b[^>]*>", RegexOptions.IgnoreCase),
            match => Attribute(match.Value, attribute) == value);
        if (tagName == "input") return opening.Value;
        var depth = 0;
        foreach (Match tag in Regex.Matches(html[opening.Index..], $"<(/?){tagName}\\b[^>]*>", RegexOptions.IgnoreCase))
        {
            depth += tag.Groups[1].Value.Length == 0 ? 1 : -1;
            if (depth == 0) return html.Substring(opening.Index, tag.Index + tag.Length);
        }
        throw new InvalidOperationException($"Elemento {tagName} sin cierre: {attribute}={value}.");
    }
}
