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
    public async Task Usuarios_Administrador_EsPermitido()
    {
        using var client = CreateClient("Administrador");
        var response = await client.GetAsync("/Usuarios");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
