using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace MSDentalSys.Tests.Integration;

public class AuthorizationIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AuthorizationIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Dashboard_Anonimo_RedireccionaAlLogin()
    {
        using var client = CreateClient();
        var response = await client.GetAsync("/Dashboard");

        AssertLoginRedirect(response);
    }

    [Fact]
    public async Task Pacientes_Anonimo_RedireccionaAlLogin()
    {
        using var client = CreateClient();
        var response = await client.GetAsync("/Pacientes");

        AssertLoginRedirect(response);
    }

    [Fact]
    public async Task Citas_Anonimo_RedireccionaAlLogin()
    {
        using var client = CreateClient();
        var response = await client.GetAsync("/Citas");

        AssertLoginRedirect(response);
    }

    [Fact]
    public async Task Servicios_Anonimo_RedireccionaAlLogin()
    {
        using var client = CreateClient();
        var response = await client.GetAsync("/Servicios");

        AssertLoginRedirect(response);
    }

    [Fact]
    public async Task Usuarios_Recepcionista_EsRechazado()
    {
        using var client = CreateClient("Recepcionista");
        var response = await client.GetAsync("/Usuarios");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PacientesCreate_Odontologo_EsRechazado()
    {
        using var client = CreateClient("Odontologo");
        var response = await client.GetAsync("/Pacientes/Create");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PacientesCreate_Recepcionista_EsPermitido()
    {
        using var client = CreateClient("Recepcionista");
        var response = await client.GetAsync("/Pacientes/Create");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("Administrador")]
    [InlineData("Recepcionista")]
    public async Task PacientesEditGet_RolesAdministrativos_EsPermitido(string role)
    {
        using var client = CreateClient(role);
        var response = await client.GetAsync("/Pacientes/Edit/1");

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PacientesEditGet_Odontologo_EsRechazado()
    {
        using var client = CreateClient("Odontologo");
        var response = await client.GetAsync("/Pacientes/Edit/1");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PacientesEditPost_Odontologo_EsRechazado()
    {
        using var client = CreateClient("Odontologo");
        var response = await client.PostAsync("/Pacientes/Edit/1", new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PacientesDetails_Odontologo_ContinuaPermitido()
    {
        using var client = CreateClient("Odontologo");
        var response = await client.GetAsync("/Pacientes/Details/1");

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PacientesIndex_Odontologo_ContinuaPermitido()
    {
        using var client = CreateClient("Odontologo");
        var response = await client.GetAsync("/Pacientes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CitasCreate_Odontologo_EsRechazado()
    {
        using var client = CreateClient("Odontologo");
        var response = await client.GetAsync("/Citas/Create");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CitasCreate_Recepcionista_EsPermitido()
    {
        using var client = CreateClient("Recepcionista");
        var response = await client.GetAsync("/Citas/Create");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Usuarios_Odontologo_EsRechazado()
    {
        using var client = CreateClient("Odontologo");
        var response = await client.GetAsync("/Usuarios");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Servicios_Odontologo_EsPermitido()
    {
        using var client = CreateClient("Odontologo");
        var response = await client.GetAsync("/Servicios");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Servicios_Recepcionista_EsPermitido()
    {
        using var client = CreateClient("Recepcionista");
        var response = await client.GetAsync("/Servicios");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Atenciones_Recepcionista_EsRechazadoSin404()
    {
        using var client = CreateClient("Recepcionista");
        var response = await client.GetAsync("/Atenciones/Details/1");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AccessDenied_UsuarioAutenticado_MuestraPaginaAmigable()
    {
        using var client = CreateClient("Recepcionista");
        var response = await client.GetAsync("/Account/AccessDenied");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Acceso restringido", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ReturnUrl", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Usuarios_Administrador_EsPermitido()
    {
        using var client = CreateClient("Administrador");
        var response = await client.GetAsync("/Usuarios");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Seguros_Administrador_EsPermitido()
    {
        using var client = CreateClient("Administrador");
        var response = await client.GetAsync("/Seguros");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("Odontologo")]
    [InlineData("Recepcionista")]
    public async Task Seguros_RolesClinicos_NoEstanAutorizados(string role)
    {
        using var client = CreateClient(role);
        var response = await client.GetAsync("/Seguros");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/Diagnosticos/Create?atencionId=1")]
    [InlineData("/Tratamientos/Create?atencionId=1")]
    [InlineData("/EvolucionesClinicas/Create?atencionId=1")]
    public async Task ModulosClinicos_Recepcionista_AccesoDirectoRechazado(string url)
    {
        using var client = CreateClient("Recepcionista");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task PacientesCreatePost_Odontologo_EsRechazado()
    {
        using var client = CreateClient("Odontologo");
        var response = await client.PostAsync("/Pacientes/Create", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private HttpClient CreateClient(string? role = null)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        if (role is not null)
        {
            client.DefaultRequestHeaders.Add("X-Test-Role", role);
            client.DefaultRequestHeaders.Add("X-Test-UserId", $"integration-{role.ToLowerInvariant()}");
            client.DefaultRequestHeaders.Add("X-Test-UserName", $"{role} de Integracion");
        }

        return client;
    }

    private static void AssertLoginRedirect(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.StartsWith("/Account/Login", response.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
