using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MSDentalSys.Data.InitialData;
using MSDentalSys.Data.Models;
using MSDentalSys.Web.Models.ViewModels;
using Xunit;

namespace MSDentalSys.Tests.Controllers;

public partial class SubserviciosControllerTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(null)]
    [InlineData(99)]
    [InlineData(0)]
    public async Task Create_Clasificacion_ValidaEnServidor(int? value)
    {
        await using var db = await Database.CreateAsync();
        var model = db.Form(); model.Clasificacion = (ClasificacionSubservicio?)value;
        var controller = db.Controller();
        var result = await controller.Create(model);
        if (value is 1 or 2)
        {
            Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(model.Clasificacion, (await db.Context.SubserviciosOdontologicos.SingleAsync()).Clasificacion);
        }
        else
        {
            Assert.IsType<ViewResult>(result);
            Assert.True(controller.ModelState.ContainsKey(nameof(model.Clasificacion)));
            Assert.Empty(await db.Context.SubserviciosOdontologicos.ToListAsync());
        }
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    [InlineData(null, 1)]
    [InlineData(null, null)]
    [InlineData(null, 99)]
    [InlineData(1, null)]
    [InlineData(2, 99)]
    public async Task Edit_Clasificacion_ConservaDatosYValidaHistoricos(int? before, int? after)
    {
        await using var db = await Database.CreateAsync();
        var item = await db.AddAsync();
        item.Clasificacion = (ClasificacionSubservicio?)before;
        item.CodigoCatalogo = "CODIGO-CONSERVADO";
        await db.Context.SaveChangesAsync();
        await db.AddAppointmentAsync(item, 45);
        var model = db.Form(); model.SubservicioOdontologicoId = item.SubservicioOdontologicoId;
        model.Clasificacion = (ClasificacionSubservicio?)after;
        var result = await db.Controller().Edit(item.SubservicioOdontologicoId, model);
        if (after is 1 or 2) Assert.IsType<RedirectToActionResult>(result);
        else Assert.IsType<ViewResult>(result);
        var stored = await db.Context.SubserviciosOdontologicos.AsNoTracking().SingleAsync();
        Assert.Equal((ClasificacionSubservicio?)(after is 1 or 2 ? after : before), stored.Clasificacion);
        Assert.Equal(30, stored.DuracionEstimadaMinutos);
        Assert.Equal("CODIGO-CONSERVADO", stored.CodigoCatalogo);
        Assert.Equal(db.Parent.ServicioOdontologicoId, stored.ServicioOdontologicoId);
        Assert.Equal(45, (await db.Context.Citas.AsNoTracking().SingleAsync()).DuracionProgramadaMinutos);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(99)]
    public async Task Esquema_CheckClasificacion(int? value)
    {
        await using var db = await Database.CreateAsync();
        var item = await db.AddAsync(); item.Clasificacion = (ClasificacionSubservicio?)value;
        if (value == 99) await Assert.ThrowsAsync<DbUpdateException>(() => db.Context.SaveChangesAsync());
        else
        {
            await db.Context.SaveChangesAsync();
            Assert.Equal((ClasificacionSubservicio?)value, (await db.Context.SubserviciosOdontologicos.AsNoTracking().SingleAsync()).Clasificacion);
        }
    }

    [Fact]
    public async Task Seeder_ConservaProvisionalesSinClasificacion()
    {
        await using var db = await Database.CreateAsync();
        await db.AddCatalogParentsAsync();
        await SubservicioSeeder.SeedAsync(db.Context);
        await SubservicioSeeder.SeedAsync(db.Context);
        var items = await db.Context.SubserviciosOdontologicos.AsNoTracking().ToListAsync();
        Assert.Equal(45, items.Count);
        Assert.All(items, s => Assert.Null(s.Clasificacion));
    }

    [Fact]
    public async Task Historico_EditGetYDetails_AceptanNull()
    {
        await using var db = await Database.CreateAsync();
        var item = await db.AddAsync();
        var model = Assert.IsType<SubservicioFormViewModel>(Assert.IsType<ViewResult>(await db.Controller().Edit(item.SubservicioOdontologicoId)).Model);
        Assert.Null(model.Clasificacion);
        var detail = Assert.IsType<SubservicioOdontologico>(Assert.IsType<ViewResult>(await db.Controller().Details(item.SubservicioOdontologicoId)).Model);
        Assert.Null(detail.Clasificacion);
    }
}
