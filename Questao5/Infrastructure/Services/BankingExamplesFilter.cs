using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Questao5.Infrastructure.Services;

public sealed class BankingExamplesFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var movement = context.ApiDescription.HttpMethod == "POST";
        if (movement && operation.RequestBody?.Content.TryGetValue("application/json", out var body) == true)
            body.Example = new OpenApiObject
            {
                ["requestId"] = new OpenApiString("80ba88aa-63de-4ab5-827b-907b403502be"),
                ["accountId"] = new OpenApiString("B6BAFC09-6967-ED11-A567-055DFA4A16C9"),
                ["value"] = new OpenApiDouble(100.00),
                ["type"] = new OpenApiString("C")
            };
        foreach (var parameter in operation.Parameters.Where(parameter => parameter.Name == "accountId"))
            parameter.Example = new OpenApiString("B6BAFC09-6967-ED11-A567-055DFA4A16C9");

        foreach (var response in operation.Responses)
        {
            if (!response.Value.Content.TryGetValue("application/json", out var content))
                continue;
            content.Example = response.Key switch
            {
                "200" when movement => new OpenApiObject { ["movementId"] = new OpenApiString("9d05572e-dacf-4662-ae24-e41923a5f345") },
                "200" => new OpenApiObject
                {
                    ["accountNumber"] = new OpenApiInteger(123),
                    ["accountHolder"] = new OpenApiString("Katherine Sanchez"),
                    ["queriedAt"] = new OpenApiString("2026-10-10T01:45:00Z"),
                    ["balance"] = new OpenApiDouble(74.50)
                },
                "400" => Error("INVALID_ACCOUNT", "A conta corrente não existe."),
                "409" => Error("IDEMPOTENCY_CONFLICT", "A chave de idempotência já foi utilizada com outro conteúdo."),
                "503" => Error("DATABASE_BUSY", "O banco está ocupado. Repita a requisição com a mesma chave de idempotência."),
                _ => Error("INTERNAL_ERROR", "Não foi possível concluir a operação.")
            };
            if (response.Key == "400")
            {
                content.Example = null;
                content.Examples = new Dictionary<string, OpenApiExample>
                {
                    ["INVALID_ACCOUNT"] = new() { Value = Error("INVALID_ACCOUNT", "A conta corrente não existe.") },
                    ["INACTIVE_ACCOUNT"] = new() { Value = Error("INACTIVE_ACCOUNT", "A conta corrente está inativa.") }
                };
                if (movement)
                {
                    content.Examples["INVALID_VALUE"] = new() { Value = Error("INVALID_VALUE", "O valor deve ser positivo, resultar em pelo menos 0.01 e não exceder 999999999999.99.") };
                    content.Examples["INVALID_TYPE"] = new() { Value = Error("INVALID_TYPE", "O tipo do movimento deve ser C (crédito) ou D (débito).") };
                    content.Examples["INVALID_REQUEST"] = new() { Value = Error("INVALID_REQUEST", "A chave de idempotência deve ser um UUID válido.") };
                }
            }
        }
    }

    private static OpenApiObject Error(string type, string message) => new()
    {
        ["type"] = new OpenApiString(type), ["message"] = new OpenApiString(message)
    };
}
