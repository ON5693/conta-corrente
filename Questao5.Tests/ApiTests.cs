using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Questao5.Application.Commands.Requests;
using Questao5.Application.Commands.Responses;
using Questao5.Application.Queries.Responses;
using Questao5.Infrastructure.Services;

namespace Questao5.Tests;

public sealed class ApiTests : IDisposable
{
    private const string AccountId = "B6BAFC09-6967-ED11-A567-055DFA4A16C9";
    private readonly BankingApiFactory factory = new();
    private readonly HttpClient client;

    public ApiTests() => client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });

    private static RegisterMovementCommand Command(decimal value = 100m, string type = "C", string accountId = AccountId) => new()
    {
        RequestId = Guid.NewGuid().ToString(), AccountId = accountId, Value = value, Type = type
    };

    [Fact]
    public async Task Credit_debit_replay_and_zero_balance()
    {
        var empty = await client.GetFromJsonAsync<BalanceResponse>($"/api/accounts/{AccountId}/balance");
        Assert.Equal(0m, empty!.Balance);
        var request = Command();
        var credit = await Post(request);
        var replay = await Post(request with { Value = 100.00m, AccountId = AccountId.ToLowerInvariant() });
        Assert.Equal(credit, replay);
        await Post(Command(25.50m, "D"));
        var balance = await client.GetFromJsonAsync<BalanceResponse>($"/api/accounts/{AccountId}/balance");
        Assert.Equal(74.50m, balance!.Balance);
        Assert.Equal(123, balance.AccountNumber);
        Assert.Equal("Katherine Sanchez", balance.AccountHolder);
        Assert.Equal(TimeSpan.Zero, balance.QueriedAt.Offset);
        Assert.Equal(2, Count("movimento"));
        Assert.Equal(2, Count("idempotencia"));
    }

    [Fact]
    public async Task Simultaneous_replays_create_one_movement()
    {
        var request = Command();
        var responses = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => Post(request))));
        Assert.Single(responses.Select(result => result.MovementId).Distinct());
        Assert.Equal(1, Count("movimento"));
        Assert.Equal(1, Count("idempotencia"));
    }

    [Fact]
    public async Task Simultaneous_different_payloads_have_one_winner_and_one_conflict()
    {
        var request = Command();
        var responses = await Task.WhenAll(
            Task.Run(() => client.PostAsJsonAsync("/api/movements", request)),
            Task.Run(() => client.PostAsJsonAsync("/api/movements", request with { Value = 200m })));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        var conflict = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        await AssertError(conflict, HttpStatusCode.Conflict, "IDEMPOTENCY_CONFLICT");
        Assert.Equal(1, Count("movimento"));
        Assert.Equal(1, Count("idempotencia"));
    }

    [Fact]
    public async Task Distinct_accounts_have_independent_balances()
    {
        await Post(Command());
        const string otherAccount = "FA99D033-7067-ED11-96C6-7C5DFA4A16C9";
        await Post(Command(25m, "D", otherAccount));
        var first = await client.GetFromJsonAsync<BalanceResponse>($"/api/accounts/{AccountId}/balance");
        var second = await client.GetFromJsonAsync<BalanceResponse>($"/api/accounts/{otherAccount}/balance");
        Assert.Equal(100m, first!.Balance);
        Assert.Equal(-25m, second!.Balance);
    }

    [Fact]
    public async Task Maximum_movement_keeps_exact_cents()
    {
        await Post(Command(999999999999.99m));
        await Post(Command(0.01m, "D"));
        var balance = await client.GetFromJsonAsync<BalanceResponse>($"/api/accounts/{AccountId}/balance");
        Assert.Equal(999999999999.98m, balance!.Balance);
    }

    [Fact]
    public async Task Different_amounts_that_round_equally_still_conflict()
    {
        var request = Command(1.001m);
        await Post(request);
        await AssertError(await client.PostAsJsonAsync("/api/movements", request with { Value = 1.002m }), HttpStatusCode.Conflict, "IDEMPOTENCY_CONFLICT");
    }

    [Fact]
    public async Task Reused_key_conflicts_and_does_not_change_balance()
    {
        var request = Command();
        await Post(request);
        await AssertError(await client.PostAsJsonAsync("/api/movements", request with { Value = 99m }), HttpStatusCode.Conflict, "IDEMPOTENCY_CONFLICT");
        Assert.Equal(1, Count("movimento"));
    }

    [Theory]
    [InlineData(0, "C", "INVALID_VALUE")]
    [InlineData(-1, "C", "INVALID_VALUE")]
    [InlineData(100, "X", "INVALID_TYPE")]
    public async Task Validation_failures_leave_no_records(int value, string type, string error)
    {
        await AssertError(await client.PostAsJsonAsync("/api/movements", Command(value, type)), HttpStatusCode.BadRequest, error);
        Assert.Equal(0, Count("movimento"));
        Assert.Equal(0, Count("idempotencia"));
    }

    [Theory]
    [InlineData("F475F943-7067-ED11-A06B-7E5DFA4A16C9", "INACTIVE_ACCOUNT")]
    [InlineData("11111111-1111-1111-1111-111111111111", "INVALID_ACCOUNT")]
    [InlineData("not-a-uuid", "INVALID_ACCOUNT")]
    public async Task Both_endpoints_reject_invalid_accounts(string accountId, string error)
    {
        await AssertError(await client.PostAsJsonAsync("/api/movements", Command(accountId: accountId)), HttpStatusCode.BadRequest, error);
        await AssertError(await client.GetAsync($"/api/accounts/{accountId}/balance"), HttpStatusCode.BadRequest, error);
    }

    [Fact]
    public async Task Invalid_attempt_does_not_consume_key()
    {
        var request = Command(0);
        await AssertError(await client.PostAsJsonAsync("/api/movements", request), HttpStatusCode.BadRequest, "INVALID_VALUE");
        await Post(request with { Value = 100m });
        Assert.Equal(1, Count("movimento"));
    }

    [Fact]
    public async Task Decimal_cents_and_negative_balances_are_exact()
    {
        foreach (var amount in new[] { 0.1m, 0.2m, 0.005m })
            await Post(Command(amount));
        await Post(Command(0.4m, "D"));
        var balance = await client.GetFromJsonAsync<BalanceResponse>($"/api/accounts/{AccountId}/balance");
        Assert.Equal(-0.09m, balance!.Balance);
    }

    [Fact]
    public async Task Idempotency_storage_failure_rolls_back_movement()
    {
        using var connection = OpenDatabase();
        connection.Execute("CREATE TRIGGER fail_idempotency BEFORE INSERT ON idempotencia BEGIN SELECT RAISE(ABORT, 'forced failure'); END;");
        await AssertError(await client.PostAsJsonAsync("/api/movements", Command()), HttpStatusCode.InternalServerError, "INTERNAL_ERROR");
        Assert.Equal(0, Count("movimento"));
        Assert.Equal(0, Count("idempotencia"));
    }

    [Fact]
    public async Task Malformed_json_has_consistent_error_contract()
    {
        using var content = new StringContent("{\"value\":\"not-a-number\"}", Encoding.UTF8, "application/json");
        await AssertError(await client.PostAsync("/api/movements", content), HttpStatusCode.BadRequest, "INVALID_REQUEST");
    }

    [Fact]
    public async Task Swagger_documents_endpoints_responses_and_examples()
    {
        using var document = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var paths = document.RootElement.GetProperty("paths");
        var post = paths.GetProperty("/api/movements").GetProperty("post");
        Assert.True(post.GetProperty("responses").TryGetProperty("409", out _));
        Assert.True(post.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").TryGetProperty("example", out _));
        Assert.True(paths.TryGetProperty("/api/accounts/{accountId}/balance", out _));
        Assert.Equal(2, paths.EnumerateObject().Count());
    }

    private async Task<MovementResponse> Post(RegisterMovementCommand command)
    {
        var response = await client.PostAsJsonAsync("/api/movements", command);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MovementResponse>())!;
    }

    private static async Task AssertError(HttpResponseMessage response, HttpStatusCode status, string type)
    {
        Assert.Equal(status, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal(type, error!.Type);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    private SqliteConnection OpenDatabase()
    {
        var connection = new SqliteConnection($"Data Source={factory.DatabasePath};Pooling=False");
        connection.Open();
        return connection;
    }

    private long Count(string table)
    {
        using var connection = OpenDatabase();
        return connection.ExecuteScalar<long>($"SELECT COUNT(*) FROM {table}");
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
    }
}
