using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Data.Sqlite;
using Questao5.Domain.Language;

namespace Questao5.Infrastructure.Services;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var (status, error) = exception switch
        {
            BusinessException business => (business.StatusCode, new ApiError(business.Type, business.Message)),
            SqliteException { SqliteErrorCode: 5 or 6 } => (503, new ApiError("DATABASE_BUSY", "O banco está ocupado. Repita a requisição com a mesma chave de idempotência.")),
            _ => (500, new ApiError("INTERNAL_ERROR", "Não foi possível concluir a operação."))
        };
        if (status >= 500)
            logger.LogError(exception, "Falha ao processar a operação bancária. TraceId: {TraceId}", context.TraceIdentifier);
        context.Response.StatusCode = status;
        if (status == 503)
            context.Response.Headers.RetryAfter = "1";
        await context.Response.WriteAsJsonAsync(error, cancellationToken);
        return true;
    }
}
