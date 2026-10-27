using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MSDentalSys.Data.Context;
using MSDentalSys.Data.Models;
using Xunit;

namespace MSDentalSys.Tests.Context;

public sealed class UserCedulaSchemaTests
{
    [Fact]
    public void Cedula_EsNullableConLongitudVeinteEIndiceUnicoFiltrado()
    {
        using var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=MetadataOnly;Database=NotUsed;").Options);
        var entity = context.Model.FindEntityType(typeof(ApplicationUser))!;
        var property = entity.FindProperty(nameof(ApplicationUser.Cedula))!;
        Assert.True(property.IsNullable);
        Assert.Equal(20, property.GetMaxLength());
        Assert.Equal("nvarchar(20)", property.GetColumnType());
        var index = Assert.Single(entity.GetIndexes(), candidate =>
            candidate.Properties.Count == 1 && candidate.Properties[0].Name == nameof(ApplicationUser.Cedula));
        Assert.True(index.IsUnique);
        Assert.Equal("[Cedula] IS NOT NULL", index.GetFilter());
    }

    [Fact]
    public async Task Cedula_AdmiteHistoricosNullYCoincidenciaPaciente_RechazaDuplicadoEntreUsuarios()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        context.Users.AddRange(new ApplicationUser(), new ApplicationUser(),
            new ApplicationUser { Cedula = "001-1234567-8" });
        context.Pacientes.Add(new Paciente { Nombre = "Paciente", Apellido = "Prueba", Cedula = "001-1234567-8" });
        await context.SaveChangesAsync();
        Assert.Equal(2, await context.Users.CountAsync(user => user.Cedula == null));
        Assert.Equal(3, await context.Users.CountAsync());
        context.Users.Add(new ApplicationUser { Cedula = "001-1234567-8" });
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
}
