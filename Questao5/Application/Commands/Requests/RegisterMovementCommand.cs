using MediatR;
using Questao5.Application.Commands.Responses;

namespace Questao5.Application.Commands.Requests;

/// <summary>Registra um movimento; a chave deve ser reutilizada somente para a mesma requisição.</summary>
public sealed record RegisterMovementCommand : IRequest<MovementResponse>
{
    /// <summary>UUID da requisição. Exemplo: 80ba88aa-63de-4ab5-827b-907b403502be.</summary>
    public string? RequestId { get; init; }
    /// <summary>UUID da conta. Exemplo: B6BAFC09-6967-ED11-A567-055DFA4A16C9.</summary>
    public string? AccountId { get; init; }
    /// <summary>Valor positivo, arredondado para duas casas, até 999999999999.99. Exemplo: 100.00.</summary>
    public decimal Value { get; init; }
    /// <summary>C para crédito ou D para débito. Exemplo: C.</summary>
    public string? Type { get; init; }
}
