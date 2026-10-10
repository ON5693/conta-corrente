using System.Globalization;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Questao5.Application.Abstractions;
using Questao5.Application.Commands.Responses;
using Questao5.Domain.Entities;
using Questao5.Infrastructure.Sqlite;

namespace Questao5.Infrastructure.Database;

public sealed class SqliteBankingStore(SqliteConnectionFactory connections) : IBankingStore
{
    private const string AccountSql = "SELECT idcontacorrente AS Id, numero AS Number, nome AS Holder, ativo AS Active FROM contacorrente WHERE idcontacorrente = @accountId";

    public IBankingSession BeginMovement() => new Session(connections.Open());

    public async Task<AccountBalance?> ReadBalanceAsync(string accountId, CancellationToken cancellationToken)
    {
        using var connection = connections.Open();
        // Both reads share a snapshot while allowing other readers.
        using var transaction = connection.BeginTransaction(deferred: true);
        var row = await connection.QuerySingleOrDefaultAsync<AccountRow>(new CommandDefinition(AccountSql,
            new { accountId }, transaction, cancellationToken: cancellationToken));
        if (row is null)
            return null;

        // Convert each REAL to integer cents before summing in decimal; never SUM floating-point amounts.
        var cents = await connection.QueryAsync<long>(new CommandDefinition(
            "SELECT CASE WHEN tipomovimento = 'C' THEN 1 ELSE -1 END * CAST(ROUND(valor * 100, 0) AS INTEGER) FROM movimento WHERE idcontacorrente = @accountId",
            new { accountId }, transaction, cancellationToken: cancellationToken));
        var balance = cents.Aggregate(0m, (total, value) => total + value) / 100m;
        transaction.Commit();
        return new AccountBalance(row.ToAccount(), decimal.Round(balance, 2));
    }

    private sealed class Session : IBankingSession
    {
        private readonly SqliteConnection connection;
        private readonly SqliteTransaction transaction;

        public Session(SqliteConnection connection)
        {
            this.connection = connection;
            try
            {
                // BEGIN IMMEDIATE serializes writers before checking the idempotency key.
                // A competing request waits, then observes the already committed result.
                transaction = connection.BeginTransaction(deferred: false);
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        public async Task<IdempotencyRecord?> FindIdempotencyAsync(string key, CancellationToken cancellationToken)
        {
            var row = await connection.QuerySingleOrDefaultAsync<IdempotencyRow>(new CommandDefinition(
                "SELECT requisicao AS Request, resultado AS Result FROM idempotencia WHERE chave_idempotencia = @key",
                new { key }, transaction, cancellationToken: cancellationToken));
            return row is null ? null : new IdempotencyRecord(row.Request,
                JsonSerializer.Deserialize<MovementResponse>(row.Result) ?? throw new InvalidOperationException("Resultado idempotente inválido."));
        }

        public async Task<Account?> FindAccountAsync(string accountId, CancellationToken cancellationToken)
        {
            var row = await connection.QuerySingleOrDefaultAsync<AccountRow>(new CommandDefinition(AccountSql,
                new { accountId }, transaction, cancellationToken: cancellationToken));
            return row?.ToAccount();
        }

        public Task AddMovementAsync(Movement movement, CancellationToken cancellationToken) => connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO movimento (idmovimento, idcontacorrente, datamovimento, tipomovimento, valor) VALUES (@Id, @AccountId, @createdAt, @Type, @Value)",
            new { movement.Id, movement.AccountId, createdAt = movement.CreatedAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture), movement.Type, movement.Value },
            transaction, cancellationToken: cancellationToken));

        public Task AddIdempotencyAsync(string key, string request, MovementResponse result, CancellationToken cancellationToken) => connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO idempotencia (chave_idempotencia, requisicao, resultado) VALUES (@key, @request, @result)",
            new { key, request, result = JsonSerializer.Serialize(result) }, transaction, cancellationToken: cancellationToken));

        public void Commit() => transaction.Commit();

        public void Dispose()
        {
            transaction.Dispose(); // Rolls back any uncommitted operation, including validation failures.
            connection.Dispose();
        }
    }

    private sealed class AccountRow
    {
        public string Id { get; set; } = "";
        public int Number { get; set; }
        public string Holder { get; set; } = "";
        public bool Active { get; set; }
        public Account ToAccount() => new(Id, Number, Holder, Active);
    }

    private sealed class IdempotencyRow
    {
        public string Request { get; set; } = "";
        public string Result { get; set; } = "";
    }
}
