using System.Globalization;
using System.Text.Json;
using NSubstitute;
using Questao5.Application.Abstractions;
using Questao5.Application.Commands.Requests;
using Questao5.Application.Commands.Responses;
using Questao5.Application.Handlers;
using Questao5.Application.Queries.Requests;
using Questao5.Domain.Entities;
using Questao5.Domain.Language;

namespace Questao5.Tests;

public sealed class HandlerTests
{
    private const string AccountId = "B6BAFC09-6967-ED11-A567-055DFA4A16C9";
    private readonly IBankingStore store = Substitute.For<IBankingStore>();
    private readonly IBankingSession session = Substitute.For<IBankingSession>();
    private readonly FixedClock clock = new();

    public HandlerTests()
    {
        store.BeginMovement().Returns(session);
        session.FindAccountAsync(AccountId, Arg.Any<CancellationToken>()).Returns(new Account(AccountId, 123, "Katherine Sanchez", true));
    }

    private static RegisterMovementCommand Command(decimal value = 100m, string? type = "C") => new()
    {
        RequestId = "80ba88aa-63de-4ab5-827b-907b403502be", AccountId = AccountId, Value = value, Type = type
    };

    [Theory]
    [InlineData("C")]
    [InlineData("D")]
    public async Task Valid_movement_is_normalized_and_committed(string type)
    {
        var response = await new RegisterMovementHandler(store, clock).Handle(Command(25.505m, type), default);
        Assert.True(Guid.TryParse(response.MovementId, out _));
        await session.Received(1).AddMovementAsync(Arg.Is<Movement>(m => m.Id == response.MovementId && m.Value == 25.51m && m.Type == type && m.CreatedAt == clock.GetUtcNow()), default);
        await session.Received(1).AddIdempotencyAsync(Arg.Any<string>(), Arg.Any<string>(), response, default);
        session.Received(1).Commit();
        session.Received(1).Dispose();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.004")]
    [InlineData("1000000000000")]
    public async Task Invalid_values_do_not_write(string value)
    {
        var error = await Assert.ThrowsAsync<BusinessException>(() => new RegisterMovementHandler(store, clock)
            .Handle(Command(decimal.Parse(value, CultureInfo.InvariantCulture)), default));
        Assert.Equal("INVALID_VALUE", error.Type);
        await AssertNoWrites();
    }

    [Theory]
    [InlineData("X")]
    [InlineData("c")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Invalid_types_do_not_write(string? type)
    {
        var error = await Assert.ThrowsAsync<BusinessException>(() => new RegisterMovementHandler(store, clock).Handle(Command(type: type), default));
        Assert.Equal("INVALID_TYPE", error.Type);
        await AssertNoWrites();
    }

    [Theory]
    [InlineData(false, "INVALID_ACCOUNT")]
    [InlineData(true, "INACTIVE_ACCOUNT")]
    public async Task Invalid_accounts_do_not_write(bool exists, string expected)
    {
        session.FindAccountAsync(AccountId, Arg.Any<CancellationToken>()).Returns(exists ? new Account(AccountId, 123, "Titular", false) : null);
        var error = await Assert.ThrowsAsync<BusinessException>(() => new RegisterMovementHandler(store, clock).Handle(Command(), default));
        Assert.Equal(expected, error.Type);
        await AssertNoWrites();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Invalid_request_key_does_not_open_transaction(string? key)
    {
        var error = await Assert.ThrowsAsync<BusinessException>(() => new RegisterMovementHandler(store, clock).Handle(Command() with { RequestId = key }, default));
        Assert.Equal("INVALID_REQUEST", error.Type);
        store.DidNotReceive().BeginMovement();
    }

    [Fact]
    public async Task Replay_returns_original_even_if_account_became_inactive()
    {
        var request = Command();
        var payload = JsonSerializer.Serialize(new { AccountId, Value = "100", request.Type });
        var original = new MovementResponse(Guid.NewGuid().ToString());
        session.FindIdempotencyAsync(request.RequestId!, default).Returns(new IdempotencyRecord(payload, original));
        var response = await new RegisterMovementHandler(store, clock).Handle(request, default);
        Assert.Equal(original, response);
        await session.DidNotReceive().FindAccountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await AssertNoWrites();
    }

    [Fact]
    public async Task Reused_key_with_different_payload_conflicts()
    {
        session.FindIdempotencyAsync(Arg.Any<string>(), default).Returns(new IdempotencyRecord("different", new MovementResponse("original")));
        var error = await Assert.ThrowsAsync<BusinessException>(() => new RegisterMovementHandler(store, clock).Handle(Command(), default));
        Assert.Equal("IDEMPOTENCY_CONFLICT", error.Type);
        Assert.Equal(409, error.StatusCode);
        await AssertNoWrites();
    }

    [Fact]
    public async Task Storage_failure_does_not_commit_and_disposes_session()
    {
        session.AddIdempotencyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<MovementResponse>(), default)
            .Returns(Task.FromException(new InvalidOperationException("failure")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new RegisterMovementHandler(store, clock).Handle(Command(), default));
        session.DidNotReceive().Commit();
        session.Received(1).Dispose();
    }

    [Fact]
    public async Task Cancellation_before_commit_disposes_without_committing()
    {
        using var cancellation = new CancellationTokenSource();
        session.AddIdempotencyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<MovementResponse>(), cancellation.Token)
            .Returns(_ => { cancellation.Cancel(); return Task.CompletedTask; });
        await Assert.ThrowsAsync<OperationCanceledException>(() => new RegisterMovementHandler(store, clock).Handle(Command(), cancellation.Token));
        session.DidNotReceive().Commit();
        session.Received(1).Dispose();
    }

    [Theory]
    [InlineData("0.00")]
    [InlineData("74.50")]
    [InlineData("-25.50")]
    public async Task Balance_returns_account_snapshot_and_utc_time(string amount)
    {
        var balance = decimal.Parse(amount, CultureInfo.InvariantCulture);
        store.ReadBalanceAsync(AccountId, default).Returns(new AccountBalance(new Account(AccountId, 123, "Katherine Sanchez", true), balance));
        var response = await new GetBalanceHandler(store, clock).Handle(new GetBalanceQuery(AccountId.ToLowerInvariant()), default);
        Assert.Equal(123, response.AccountNumber);
        Assert.Equal("Katherine Sanchez", response.AccountHolder);
        Assert.Equal(clock.GetUtcNow(), response.QueriedAt);
        Assert.Equal(balance, response.Balance);
    }

    [Theory]
    [InlineData(false, "INVALID_ACCOUNT")]
    [InlineData(true, "INACTIVE_ACCOUNT")]
    public async Task Balance_rejects_missing_or_inactive_account(bool exists, string expected)
    {
        store.ReadBalanceAsync(AccountId, default).Returns(exists ? new AccountBalance(new Account(AccountId, 123, "Titular", false), 0m) : null);
        var error = await Assert.ThrowsAsync<BusinessException>(() => new GetBalanceHandler(store, clock).Handle(new GetBalanceQuery(AccountId), default));
        Assert.Equal(expected, error.Type);
    }

    [Fact]
    public async Task Malformed_account_id_is_rejected_before_storage()
    {
        var error = await Assert.ThrowsAsync<BusinessException>(() => new GetBalanceHandler(store, clock).Handle(new GetBalanceQuery("123"), default));
        Assert.Equal("INVALID_ACCOUNT", error.Type);
        await store.DidNotReceive().ReadBalanceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private async Task AssertNoWrites()
    {
        await session.DidNotReceive().AddMovementAsync(Arg.Any<Movement>(), Arg.Any<CancellationToken>());
        await session.DidNotReceive().AddIdempotencyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<MovementResponse>(), Arg.Any<CancellationToken>());
        session.DidNotReceive().Commit();
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    }
}
