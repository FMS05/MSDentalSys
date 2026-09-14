using System.Data;
using System.Data.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MSDentalSys.Data.Context;
using MSDentalSys.Web.Models.ViewModels;
using Xunit;

namespace MSDentalSys.Tests.Controllers;

public sealed class SqlServerH8TheoryAttribute : TheoryAttribute
{
    public SqlServerH8TheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MSDENTALSYS_H8_SQLSERVER")))
            Skip = "SQL Server aislado no configurado: MSDENTALSYS_H8_SQLSERVER.";
    }
}

public partial class CitasControllerTests
{
    [SqlServerH8Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task H8_SqlServer_DosSolicitudesSoloUnaReserva(bool firstReschedule, bool secondReschedule)
    {
        var builder = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("MSDENTALSYS_H8_SQLSERVER"));
        // Only an explicitly supplied administration connection on a TEST server is accepted.
        Assert.Equal("master", builder.InitialCatalog, ignoreCase: true);
        var databaseName = "MSDentalSys_H8Tests_" + Guid.NewGuid().ToString("N");
        Console.WriteLine($"H8 database: {databaseName}; scenarios: {firstReschedule}/{secondReschedule}");
        builder.InitialCatalog = databaseName;
        builder.Pooling = false;
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(builder.ConnectionString).Options;
        await using var schema = new ApplicationDbContext(options);
        try
        {
            await schema.Database.EnsureCreatedAsync();
            var barrier = new ScheduleReadBarrier();
            var commits = new ScheduleCommitProbe(barrier);
            var saves = new ScheduleSaveProbe(barrier);
            await using var first = await TestDatabase.CreateWithConnectionAsync(new SqlConnection(builder.ConnectionString), false, barrier, commits, saves);
            await first.AddSupportDataAsync();
            var oldFirst = first.CreateAppointment(ScheduleStart.AddDays(3));
            var oldSecond = first.CreateAppointment(ScheduleStart.AddDays(4));
            oldFirst.DuracionProgramadaMinutos = oldSecond.DuracionProgramadaMinutos = 60;
            oldFirst.SubservicioOdontologicoId = oldSecond.SubservicioOdontologicoId = first.SubserviceId;
            first.Context.AddRange(oldFirst, oldSecond);
            await first.Context.SaveChangesAsync();
            await using var second = await TestDatabase.CreateWithConnectionAsync(new SqlConnection(builder.ConnectionString), false, barrier, commits, saves);
            var a = first.CreateController();
            var b = second.CreateController();
            barrier.Enabled = true;
            var taskA = firstReschedule
                ? a.Reschedule(oldFirst.CitaId, new ReagendarCitaViewModel { CitaId = oldFirst.CitaId, FechaHoraInicio = ScheduleStart })
                : a.Create(first.CreateAppointmentModel(ScheduleStart));
            var taskB = secondReschedule
                ? b.Reschedule(oldSecond.CitaId, new ReagendarCitaViewModel { CitaId = oldSecond.CitaId, FechaHoraInicio = ScheduleStart.AddMinutes(30) })
                : b.Create(first.CreateAppointmentModel(ScheduleStart.AddMinutes(30)));
            var results = await Task.WhenAll(taskA, taskB).WaitAsync(TimeSpan.FromSeconds(60));
            Assert.Single(results.OfType<RedirectToActionResult>());
            Assert.Single(results.OfType<ViewResult>());
            Assert.Equal(2, barrier.Arrivals);
            Assert.Equal(1, barrier.Deadlocks);
            Assert.Single(barrier.Transactions, pair => pair.Value == 3);
            Assert.Single(barrier.Transactions, pair => pair.Value == 2);
            var loser = results[0] is ViewResult ? a : b;
            Console.WriteLine($"H8 results: {results[0].GetType().Name}/{results[1].GetType().Name}; errors: {string.Join("; ", loser.ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage))}");
            Assert.Contains(loser.ModelState.Values.SelectMany(v => v.Errors), e =>
                e.ErrorMessage.Contains("Otra operación está modificando la agenda") || e.ErrorMessage.Contains("se superpone"));
            var stored = await schema.Citas.AsNoTracking().ToListAsync();
            Assert.Single(stored, c => c.FechaHoraInicio >= ScheduleStart && c.FechaHoraInicio < ScheduleStart.AddHours(1));
            Assert.All(stored, c => Assert.Equal(60, c.DuracionProgramadaMinutos));
            Assert.Equal(results[0] is RedirectToActionResult && firstReschedule ? ScheduleStart : oldFirst.FechaHoraInicio,
                stored.Single(c => c.CitaId == oldFirst.CitaId).FechaHoraInicio);
            Assert.Equal(results[1] is RedirectToActionResult && secondReschedule ? ScheduleStart.AddMinutes(30) : oldSecond.FechaHoraInicio,
                stored.Single(c => c.CitaId == oldSecond.CitaId).FechaHoraInicio);
        }
        finally
        {
            // This context can only address the random database created above, never the application DB.
            Assert.Equal(databaseName, schema.Database.GetDbConnection().Database);
            await schema.Database.EnsureDeletedAsync();
            Assert.False(await schema.Database.CanConnectAsync());
            Console.WriteLine($"H8 deleted: {databaseName}");
        }
    }

    private sealed class ScheduleReadBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _bothRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;
        private int _deadlocks;
        public int Deadlocks => _deadlocks;
        public System.Collections.Concurrent.ConcurrentDictionary<DbTransaction, int> Transactions { get; } = new();
        public bool Enabled { get; set; }
        public int Arrivals => _arrivals;
        private void ObserveWrite(DbCommand command)
        {
            if (!Enabled || (!command.CommandText.Contains("INSERT INTO [Citas]") && !command.CommandText.StartsWith("UPDATE"))) return;
            Assert.NotNull(command.Transaction);
            Assert.Equal(IsolationLevel.Serializable, command.Transaction.IsolationLevel);
            Assert.True(Transactions.TryUpdate(command.Transaction, 2, 1), "Write must follow availability on the same transaction.");
            Console.WriteLine("H8 write: same Serializable transaction as availability");
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            ObserveWrite(command);
            return ValueTask.FromResult(result);
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            ObserveWrite(command);
            return ValueTask.FromResult(result);
        }
        public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            ObserveError(eventData.Exception);
            return Task.CompletedTask;
        }
        public void ObserveError(Exception exception)
        {
            while (exception.InnerException is { } inner) exception = inner;
            if (exception is SqlException { Number: 1205 })
            {
                Interlocked.Exchange(ref _deadlocks, 1);
                Console.WriteLine("H8 SQL Server error: 1205");
            }
        }
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Enabled && command.Transaction is not null && command.CommandText.StartsWith("SELECT") &&
                command.CommandText.Contains("DuracionProgramadaMinutos") && command.CommandText.Contains(">="))
            {
                Assert.Equal(IsolationLevel.Serializable, command.Transaction.IsolationLevel);
                Assert.True(Transactions.TryAdd(command.Transaction, 1));
                if (Interlocked.Increment(ref _arrivals) == 2) _bothRead.TrySetResult();
                await _bothRead.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return result;
        }
    }

    private sealed class ScheduleCommitProbe(ScheduleReadBarrier barrier) : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (barrier.Enabled)
            {
                Assert.True(barrier.Transactions.TryUpdate(transaction, 3, 2), "Commit must follow write on the same transaction.");
                Console.WriteLine("H8 commit: after write on same transaction");
            }
            return Task.CompletedTask;
        }
    }

    // INSERT errors may surface while consuming OUTPUT, after ReaderExecuted.
    private sealed class ScheduleSaveProbe(ScheduleReadBarrier barrier) : SaveChangesInterceptor
    {
        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            barrier.ObserveError(eventData.Exception);
            return Task.CompletedTask;
        }
    }
}
