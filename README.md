# API bancária — Questão 5

Implementação da [especificação](Questao5/Questão%205%20-%20Especificação%20da%20API%20Bancária.md), com ASP.NET Core, CQRS/MediatR, Dapper e SQLite. Requer SDK .NET 10 (LTS); o projeto original usava .NET 6, já fora de suporte. As dependências de testes ficam no projeto separado `Questao5.Tests`.

## Executar e testar

```powershell
dotnet restore Exercicio.sln
dotnet build Exercicio.sln --no-restore
dotnet test Exercicio.sln --no-build
dotnet run --project Questao5
```

Swagger em `https://localhost:7140/swagger` no ambiente Development. Os exemplos de requisição e de respostas também estão no OpenAPI em `/swagger/v1/swagger.json`. O perfil local usa HTTPS; se necessário, confie no certificado com `dotnet dev-certs https --trust`.

O banco é configurado por `DatabaseName` em `appsettings.json` ou pela variável de ambiente de mesmo nome. Caminhos relativos são resolvidos a partir da pasta do projeto da API. A inicialização cria as tabelas ausentes e as seis contas de exemplo, sem sobrescrever titulares ou status existentes. Não há endpoint de cadastro.

## Contrato HTTP

`POST /api/movements`:

```json
{
  "requestId": "80ba88aa-63de-4ab5-827b-907b403502be",
  "accountId": "B6BAFC09-6967-ED11-A567-055DFA4A16C9",
  "value": 100.00,
  "type": "C"
}
```

Resposta HTTP 200:

```json
{ "movementId": "9d05572e-dacf-4662-ae24-e41923a5f345" }
```

`GET /api/accounts/B6BAFC09-6967-ED11-A567-055DFA4A16C9/balance` retorna HTTP 200:

```json
{
  "accountNumber": 123,
  "accountHolder": "Katherine Sanchez",
  "queriedAt": "2026-10-10T01:45:00+00:00",
  "balance": 100.00
}
```

Datas e IDs das respostas acima são ilustrativos. Datas novas são UTC/ISO 8601. A data de movimento tem formato `yyyy-MM-ddTHH:mm:ss.fffZ`, compatível com o tamanho informado. Datas legadas permanecem preservadas.

| HTTP | Tipo | Situação |
|---|---|---|
| 400 | `INVALID_ACCOUNT` | UUID inválido ou conta inexistente |
| 400 | `INACTIVE_ACCOUNT` | Conta inativa |
| 400 | `INVALID_VALUE` | Valor inválido |
| 400 | `INVALID_TYPE` | Tipo diferente de `C` ou `D` (sensível a maiúsculas) |
| 400 | `INVALID_REQUEST` | Chave ausente/inválida, JSON ou parâmetros inválidos |
| 409 | `IDEMPOTENCY_CONFLICT` | Chave já utilizada com outro conteúdo |
| 503 | `DATABASE_BUSY` | SQLite ocupado após timeout; repetir com a mesma chave |
| 500 | `INTERNAL_ERROR` | Falha inesperada; detalhes registrados no servidor |

Erros têm o formato `{ "type": "INVALID_ACCOUNT", "message": "A conta corrente não existe." }`.

## Decisões e garantias

- A identificação pública é `idcontacorrente`, um UUID, e não o número. Os IDs são normalizados, permitindo diferenças de caixa e formatação de UUID. `requestId` também é UUID não vazio.
- Uma transação `BEGIN IMMEDIATE` adquire o bloqueio de escrita **antes** da consulta da chave. Escritores concorrentes aguardam até 30 segundos e então consultam o resultado já confirmado. Movimento e idempotência são inseridos na mesma transação; falhas e cancelamento antes do commit revertem ambos. A chave primária continua sendo uma proteção adicional. Não depende de bloqueios em memória ou de uma única instância da API.
- Repetições retornam o movimento original, inclusive se a conta tiver sido desativada depois. Chave reutilizada com conta, valor ou tipo diferente retorna 409. Valores numericamente equivalentes (`100` e `100.00`) são a mesma requisição; valores diferentes antes do arredondamento causam conflito, mesmo quando produziriam os mesmos centavos.
- Tentativas inválidas não persistem a chave e podem ser corrigidas e reenviadas. Débitos podem produzir saldo negativo, pois a especificação não exige limite ou saldo disponível.
- Valores usam `decimal`, com arredondamento de duas casas `AwayFromZero` (`0.005` vira `0.01`). Valores que resultam em zero são rejeitados. O máximo por movimento é `999999999999.99`, para manter centavos representáveis no `REAL` legado. A consulta converte cada valor em centavos inteiros antes de acumular em `decimal`, evitando somas em ponto flutuante e overflow de `SUM` inteiro no SQLite. O saldo é uma leitura consistente de conta e movimentos na mesma transação. O número JSON pode omitir zeros à direita sem alterar a precisão monetária.
- A migração transacional corrige `movimento.idcontacorrente` de `INTEGER` para `TEXT`, preservando movimentos, valores e datas; corrige o espaço no ID da conta 123 e suas referências. Registros existentes de idempotência são preservados. Chaves estrangeiras são habilitadas por conexão e verificadas no bootstrap; dados órfãos impedem a inicialização e revertem a migração. Foi criado um índice de movimentos por conta.
- A API mantém as três tabelas da especificação. Para volumes maiores, vale planejar uma migração versionada para centavos inteiros e cálculo agregado no banco; a implementação atual lê os centavos de todos os movimentos da conta.

## Contas de exemplo

| Número | UUID | Titular | Ativa |
|---|---|---|---|
| 123 | B6BAFC09-6967-ED11-A567-055DFA4A16C9 | Katherine Sanchez | Sim |
| 456 | FA99D033-7067-ED11-96C6-7C5DFA4A16C9 | Eva Woodward | Sim |
| 789 | 382D323D-7067-ED11-8866-7D5DFA4A16C9 | Tevin Mcconnell | Sim |
| 741 | F475F943-7067-ED11-A06B-7E5DFA4A16C9 | Ameena Lynn | Não |
| 852 | BCDACA4A-7067-ED11-AF81-825DFA4A16C9 | Jarrad Mckee | Não |
| 963 | D2E02051-7067-ED11-94C0-835DFA4A16C9 | Elisha Simons | Não |

## Testes

Execute os comandos na raiz do repositório, onde fica a solução `Exercicio.sln`:

```powershell
# Restaura os pacotes, compila a solução e executa todos os testes.
dotnet test .\Exercicio.sln

# Executa somente o projeto de testes.
dotnet test .\Questao5.Tests\Questao5.Tests.csproj

# Executa os testes dos handlers ou da API por vez.
dotnet test .\Questao5.Tests\Questao5.Tests.csproj --filter "FullyQualifiedName~HandlerTests"
dotnet test .\Questao5.Tests\Questao5.Tests.csproj --filter "FullyQualifiedName~ApiTests"

# Reexecuta os testes em Release após restaurar e compilar a solução.
dotnet test .\Exercicio.sln --configuration Release --no-restore
```

`dotnet test` restaura e compila antes de testar. Use `--no-restore` quando os pacotes já tiverem sido restaurados. Os filtros usam os nomes das classes em `Questao5.Tests`.

Testes unitários substituem o armazenamento com NSubstitute e o relógio com `TimeProvider`. Testes de integração exercitam a API com SQLite temporário, incluindo validações, saldo, idempotência concorrente, rollback forçado, JSON inválido e geração do Swagger. Testes de bootstrap cobrem schema parcial, migração legada, preservação dos dados e rollback por referência órfã. Nenhum teste usa ou altera `Questao5/database.sqlite`.
