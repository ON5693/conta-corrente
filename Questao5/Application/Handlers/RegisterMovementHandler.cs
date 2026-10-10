using System.Text.Json;
using System.Globalization;
using MediatR;
using Questao5.Application.Abstractions;
using Questao5.Application.Commands.Requests;
using Questao5.Application.Commands.Responses;
using Questao5.Domain.Entities;
using Questao5.Domain.Language;

namespace Questao5.Application.Handlers;

public sealed class RegisterMovementHandler(IBankingStore store, TimeProvider clock)
    : IRequestHandler<RegisterMovementCommand, MovementResponse>
{
    public async Task<MovementResponse> Handle(RegisterMovementCommand request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(request.RequestId, out var key) || key == Guid.Empty)
            throw new BusinessException("INVALID_REQUEST", "A chave de idempotência deve ser um UUID válido.");

        var accountId = AccountRules.NormalizeId(request.AccountId);
        var requestKey = key.ToString("D");
        // Preserve the original value: different amounts must conflict even if they round equally.
        var payload = JsonSerializer.Serialize(new { AccountId = accountId, Value = request.Value.ToString("G29", CultureInfo.InvariantCulture), request.Type });

        using var session = store.BeginMovement();
        var previous = await session.FindIdempotencyAsync(requestKey, cancellationToken);
        if (previous is not null)
        {
            if (previous.Request != payload)
                throw new BusinessException("IDEMPOTENCY_CONFLICT", "A chave de idempotência já foi utilizada com outro conteúdo.", 409);
            return previous.Result;
        }

        AccountRules.RequireActive(await session.FindAccountAsync(accountId, cancellationToken));
        var value = decimal.Round(request.Value, 2, MidpointRounding.AwayFromZero);
        if (request.Value <= 0 || value <= 0 || request.Value > 999999999999.99m)
            throw new BusinessException("INVALID_VALUE", "O valor deve ser positivo, resultar em pelo menos 0.01 e não exceder 999999999999.99.");
        if (request.Type is not ("C" or "D"))
            throw new BusinessException("INVALID_TYPE", "O tipo do movimento deve ser C (crédito) ou D (débito).");

        var movement = new Movement(Guid.NewGuid().ToString("D"), accountId, clock.GetUtcNow(), request.Type, value);
        var response = new MovementResponse(movement.Id);
        await session.AddMovementAsync(movement, cancellationToken);
        await session.AddIdempotencyAsync(requestKey, payload, response, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        session.Commit();
        return response;
    }
}
