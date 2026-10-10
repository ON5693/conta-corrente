using MediatR;
using Microsoft.AspNetCore.Mvc;
using Questao5.Application.Commands.Requests;
using Questao5.Application.Commands.Responses;
using Questao5.Application.Queries.Requests;
using Questao5.Application.Queries.Responses;

namespace Questao5.Infrastructure.Services.Controllers;

[ApiController]
[Route("api")]
[Produces("application/json")]
public sealed class AccountsController(IMediator mediator) : ControllerBase
{
    /// <summary>Registra um crédito ou débito com idempotência.</summary>
    /// <remarks>Uma repetição retorna o mesmo movementId. Débitos podem produzir saldo negativo.
    /// Exemplo: { "requestId": "80ba88aa-63de-4ab5-827b-907b403502be", "accountId": "B6BAFC09-6967-ED11-A567-055DFA4A16C9", "value": 100.00, "type": "C" }.</remarks>
    /// <response code="200">Movimento criado ou resultado original de uma repetição.</response>
    /// <response code="400">INVALID_ACCOUNT, INACTIVE_ACCOUNT, INVALID_VALUE, INVALID_TYPE ou INVALID_REQUEST.</response>
    /// <response code="409">IDEMPOTENCY_CONFLICT: mesma chave com conteúdo diferente.</response>
    /// <response code="503">DATABASE_BUSY: repetir com a mesma chave.</response>
    [HttpPost("movements")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(MovementResponse), 200)]
    [ProducesResponseType(typeof(ApiError), 400)]
    [ProducesResponseType(typeof(ApiError), 409)]
    [ProducesResponseType(typeof(ApiError), 500)]
    [ProducesResponseType(typeof(ApiError), 503)]
    public async Task<ActionResult<MovementResponse>> RegisterMovement([FromBody] RegisterMovementCommand request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(request, cancellationToken));

    /// <summary>Consulta os dados e o saldo de uma conta ativa.</summary>
    /// <param name="accountId">UUID da conta; exemplo: B6BAFC09-6967-ED11-A567-055DFA4A16C9.</param>
    /// <param name="cancellationToken">Cancelamento da requisição.</param>
    /// <response code="200">Número, titular, data/hora UTC e saldo calculado a partir dos movimentos.</response>
    /// <response code="400">INVALID_ACCOUNT ou INACTIVE_ACCOUNT.</response>
    [HttpGet("accounts/{accountId}/balance")]
    [ProducesResponseType(typeof(BalanceResponse), 200)]
    [ProducesResponseType(typeof(ApiError), 400)]
    [ProducesResponseType(typeof(ApiError), 500)]
    [ProducesResponseType(typeof(ApiError), 503)]
    public async Task<ActionResult<BalanceResponse>> GetBalance(string accountId, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetBalanceQuery(accountId), cancellationToken));
}
