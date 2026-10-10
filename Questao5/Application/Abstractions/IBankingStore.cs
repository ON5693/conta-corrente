using Questao5.Application.Commands.Responses;
using Questao5.Domain.Entities;

namespace Questao5.Application.Abstractions;

public interface IBankingSession : IDisposable
{
    Task<IdempotencyRecord?> FindIdempotencyAsync(string key, CancellationToken cancellationToken);
    Task<Account?> FindAccountAsync(string accountId, CancellationToken cancellationToken);
    Task AddMovementAsync(Movement movement, CancellationToken cancellationToken);
    Task AddIdempotencyAsync(string key, string request, MovementResponse result, CancellationToken cancellationToken);
    void Commit();
}

public interface IBankingStore
{
    IBankingSession BeginMovement();
    Task<AccountBalance?> ReadBalanceAsync(string accountId, CancellationToken cancellationToken);
}

public sealed record IdempotencyRecord(string Request, MovementResponse Result);
public sealed record AccountBalance(Account Account, decimal Balance);
