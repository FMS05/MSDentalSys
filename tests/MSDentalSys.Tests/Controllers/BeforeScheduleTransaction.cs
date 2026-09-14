using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MSDentalSys.Tests.Controllers;

// La escritura ganadora debe confirmar antes de la transacción SQLite compartida.
internal sealed class BeforeScheduleTransaction : DbTransactionInterceptor
{
    public Func<Task>? Callback { get; set; }
    public bool Invoked { get; private set; }
    public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
        DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
        CancellationToken cancellationToken = default)
    {
        if (Callback is { } callback)
        {
            Callback = null; Invoked = true; await callback();
        }
        return result;
    }
}
