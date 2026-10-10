using Questao5.Domain.Entities;

namespace Questao5.Domain.Language;

public static class AccountRules
{
    public static string NormalizeId(string? id)
    {
        if (!Guid.TryParse(id, out var guid) || guid == Guid.Empty)
            throw new BusinessException("INVALID_ACCOUNT", "A identificação da conta deve ser um UUID válido.");
        return guid.ToString("D").ToUpperInvariant();
    }

    public static Account RequireActive(Account? account)
    {
        if (account is null)
            throw new BusinessException("INVALID_ACCOUNT", "A conta corrente não existe.");
        if (!account.Active)
            throw new BusinessException("INACTIVE_ACCOUNT", "A conta corrente está inativa.");
        return account;
    }
}
