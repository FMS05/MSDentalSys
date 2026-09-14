using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MSDentalSys.Data.Context;
using MSDentalSys.Data.Models;
using Xunit;

namespace MSDentalSys.Tests.Integration;

public class PacientesAdmisionTests
{
    [Theory]
    [InlineData("Recepcionista", "Femenino")]
    [InlineData("Administrador", "Femenino")]
    [InlineData("Recepcionista", "Masculino")]
    public async Task Admision_CreateEditDetails_PersistenAntecedentesYRespetanEmbarazo(string role, string sexo)
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        int seguroId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var seguro = new Seguro { Nombre = "Seguro admision", Estado = true };
            db.Add(seguro);
            await db.SaveChangesAsync();
            seguroId = seguro.SeguroId;
        }

        var form = new Dictionary<string, string>
        {
            ["Nombre"] = "Paciente", ["Apellido"] = "Admision", ["Sexo"] = sexo,
            ["TieneSeguro"] = "true", ["SeguroId"] = seguroId.ToString(),
            ["Alergias"] = "Alergia inicial", ["EnfermedadesSistemicas"] = "Enfermedad inicial",
            ["MedicamentosActuales"] = "Medicamento inicial", ["CirugiasPrevias"] = "Cirugia inicial",
            ["HabitosRelevantes"] = "Habito inicial", ["Embarazo"] = "true",
            ["Observaciones"] = "Observacion inicial"
        };
        form["__RequestVerificationToken"] = await TokenAsync(client, "/Pacientes/Create");
        Assert.Equal(HttpStatusCode.Redirect,
            (await client.PostAsync("/Pacientes/Create", new FormUrlEncodedContent(form))).StatusCode);
        int pacienteId;
        int antecedenteId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var paciente = await db.Pacientes.Include(p => p.AntecedenteClinico).SingleAsync();
            pacienteId = paciente.PacienteId;
            antecedenteId = paciente.AntecedenteClinico!.AntecedenteClinicoId;
            Assert.Equal("Paciente", paciente.Nombre);
            Assert.Equal(seguroId, paciente.SeguroId);
            AssertAntecedentes(paciente.AntecedenteClinico, form, sexo);
            // The existing inactive insurer remains valid during clinical editing.
            (await db.Seguros.SingleAsync()).Estado = false;
            await db.SaveChangesAsync();
        }

        form["PacienteId"] = pacienteId.ToString();
        foreach (var field in new[] { "Alergias", "EnfermedadesSistemicas", "MedicamentosActuales",
            "CirugiasPrevias", "HabitosRelevantes", "Observaciones" })
            form[field] = form[field].Replace("inicial", "actualizada");
        form["__RequestVerificationToken"] = await TokenAsync(client, $"/Pacientes/Edit/{pacienteId}");
        Assert.Equal(HttpStatusCode.Redirect,
            (await client.PostAsync($"/Pacientes/Edit/{pacienteId}", new FormUrlEncodedContent(form))).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var paciente = await db.Pacientes.Include(p => p.AntecedenteClinico).SingleAsync();
            Assert.Equal(antecedenteId, paciente.AntecedenteClinico!.AntecedenteClinicoId);
            Assert.Equal(seguroId, paciente.SeguroId);
            AssertAntecedentes(paciente.AntecedenteClinico, form, sexo);
        }

        await AssertDetailsAsync(client, pacienteId, form, sexo);
        using var odontologo = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        odontologo.DefaultRequestHeaders.Add("X-Test-Role", "Odontologo");
        await AssertDetailsAsync(odontologo, pacienteId, form, sexo);
    }

    private static void AssertAntecedentes(AntecedenteClinico antecedente, Dictionary<string, string> form, string sexo)
    {
        Assert.Equal(form["Alergias"], antecedente.Alergias);
        Assert.Equal(form["EnfermedadesSistemicas"], antecedente.EnfermedadesSistemicas);
        Assert.Equal(form["MedicamentosActuales"], antecedente.MedicamentosActuales);
        Assert.Equal(form["CirugiasPrevias"], antecedente.CirugiasPrevias);
        Assert.Equal(form["HabitosRelevantes"], antecedente.HabitosRelevantes);
        Assert.Equal(form["Observaciones"], antecedente.Observaciones);
        Assert.Equal(sexo == "Femenino" ? true : (bool?)null, antecedente.Embarazo);
    }

    private static async Task<string> TokenAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success);
        return WebUtility.HtmlDecode(token.Groups[1].Value);
    }

    private static async Task AssertDetailsAsync(HttpClient client, int id, Dictionary<string, string> form, string sexo)
    {
        var response = await client.GetAsync($"/Pacientes/Details/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        foreach (var field in new[] { "Alergias", "EnfermedadesSistemicas", "MedicamentosActuales",
            "CirugiasPrevias", "HabitosRelevantes", "Observaciones" })
            Assert.Contains(form[field], html);
        if (sexo == "Femenino") Assert.Matches(@"<dt>Embarazo</dt>\s*<dd>Sí</dd>", html);
        else Assert.DoesNotContain("<dt>Embarazo</dt>", html);
    }
}
