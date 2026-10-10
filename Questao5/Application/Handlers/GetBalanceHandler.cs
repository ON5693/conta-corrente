using MediatR;
using Questao5.Application.Abstractions;
using Questao5.Application.Queries.Requests;
using Questao5.Application.Queries.Responses;
using Questao5.Domain.Language;

namespace Questao5.Application.Handlers;

public sealed class GetBalanceHandler(IBankingStore store, TimeProvider clock) : IRequestHandler<GetBalanceQuery, BalanceResponse>
{
    public async Task<BalanceResponse> Handle(GetBalanceQuery request, CancellationToken cancellationToken)
    {
        var snapshot = await store.ReadBalanceAsync(AccountRules.NormalizeId(request.AccountId), cancellationToken);
        var account = AccountRules.RequireActive(snapshot?.Account);
        return new BalanceResponse(account.Number, account.Holder, clock.GetUtcNow(), snapshot!.Balance);
    }
}
