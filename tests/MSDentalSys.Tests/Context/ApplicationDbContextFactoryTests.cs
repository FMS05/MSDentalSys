using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MSDentalSys.Data.Context;
using Xunit;

namespace MSDentalSys.Tests.Context;

public class ApplicationDbContextFactoryTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "MSDentalSysFactoryTests", Guid.NewGuid().ToString("N"));

    public ApplicationDbContextFactoryTests()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "MSDentalSys.Web.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(directory, "appsettings.json"),
            "{\"ConnectionStrings\":{\"DefaultConnection\":\"Server=base.invalid;Database=Base;Integrated Security=True\"}}");
    }

    [Fact]
    public void CreateDbContext_ArgumentoSobrescribeConfiguracion_SinAbrirConexion()
    {
        const string connection = "Server=argumento.invalid;Database=Controlada;Integrated Security=True";
        using var context = Create("--ConnectionStrings:DefaultConnection", connection);
        AssertEquivalentConnection(connection, context.Database.GetConnectionString());
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", context.Database.ProviderName);
    }

    [Fact]
    public void CreateDbContext_LeeArchivoDelEntorno()
    {
        File.WriteAllText(Path.Combine(directory, "appsettings.FactoryTests.json"),
            "{\"ConnectionStrings\":{\"DefaultConnection\":\"Server=entorno.invalid;Database=Entorno;Integrated Security=True\"}}");
        using var context = Create();
        AssertEquivalentConnection(Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Server=entorno.invalid;Database=Entorno;Integrated Security=True", context.Database.GetConnectionString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateDbContext_ConexionVacia_FallaClaramente(string value)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Create("--ConnectionStrings:DefaultConnection", value));
        Assert.Contains("DefaultConnection", error.Message);
    }

    [Fact]
    public void CreateDbContext_ContentRootInexistente_FallaClaramente()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            new ApplicationDbContextFactory().CreateDbContext(["--contentRoot", Path.Combine(directory, "ausente")]));
        Assert.Contains("MSDentalSys.Web.csproj", error.Message);
    }

    private static void AssertEquivalentConnection(string expected, string? actual)
    {
        Assert.NotNull(actual);
        var expectedBuilder = new SqlConnectionStringBuilder(expected);
        var actualBuilder = new SqlConnectionStringBuilder(actual);

        // EF Core 10 agrega Application Name solo cuando no se configuró explícitamente.
        if (!expectedBuilder.ShouldSerialize("Application Name"))
            actualBuilder.Remove("Application Name");

        Assert.True(expectedBuilder.EquivalentTo(actualBuilder),
            "La fábrica debe conservar todos los parámetros configurados de la conexión.");
    }

    private ApplicationDbContext Create(params string[] args) => new ApplicationDbContextFactory()
        .CreateDbContext(["--contentRoot", directory, "--environment", "FactoryTests", .. args]);

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
