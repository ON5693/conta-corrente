namespace Questao5.Domain.Entities;

public sealed record Movement(string Id, string AccountId, DateTimeOffset CreatedAt, string Type, decimal Value);
