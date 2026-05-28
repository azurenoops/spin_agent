using Microsoft.Extensions.Hosting;

namespace Ato.Copilot.Core.Interfaces.Auth;

/// <summary>
/// Feature 051 — singleton hosted service that migrates rows older than
/// 13 months from the hot <c>LoginAuditEvents</c> table to the immutable
/// cold archive via <see cref="ILoginAuditArchiveSink"/>. Wakes daily at
/// <c>Auth:Archive:RunHourUtc</c> (default 02:00 UTC) and archives in
/// 1,000-row batches. See
/// <c>contracts/internal-services.md § 4.1 / § 4.4</c>.
/// </summary>
/// <remarks>
/// The marker interface exists so consumers can verify the service is
/// registered without taking a hard reference on the concrete
/// implementation; the type is otherwise driven entirely through
/// <see cref="IHostedService"/>.
/// </remarks>
public interface ILoginAuditArchiveService : IHostedService
{
}
