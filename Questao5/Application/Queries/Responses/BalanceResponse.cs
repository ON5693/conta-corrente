namespace Questao5.Application.Queries.Responses;

public sealed record BalanceResponse(int AccountNumber, string AccountHolder, DateTimeOffset QueriedAt, decimal Balance);
