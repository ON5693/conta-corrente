using Dapper;
using Microsoft.Data.Sqlite;
using Questao5.Infrastructure.Sqlite;

namespace Questao5.Tests;

public sealed class DatabaseBootstrapTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"questao5-bootstrap-{Guid.NewGuid():N}.sqlite");
    private SqliteConnectionFactory Connections => new(new DatabaseConfig { Name = $"Data Source={path};Pooling=False" });

    [Fact]
    public void Setup_is_repeatable_and_repairs_partial_schema_without_overwriting_accounts()
    {
        using var connection = Connections.Open();
        connection.Execute("""
            CREATE TABLE contacorrente (idcontacorrente TEXT PRIMARY KEY, numero INTEGER NOT NULL UNIQUE, nome TEXT NOT NULL, ativo INTEGER NOT NULL);
            INSERT INTO contacorrente VALUES ('B6BAFC09-6967-ED11-A567-055DFA4A16C9',123,'Nome atualizado',0);
            """);
        var bootstrap = new DatabaseBootstrap(Connections);
        bootstrap.Setup();
        bootstrap.Setup();
        Assert.Equal(6, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM contacorrente"));
        Assert.Equal("Nome atualizado", connection.ExecuteScalar<string>("SELECT nome FROM contacorrente WHERE numero=123"));
        Assert.Equal(0, connection.ExecuteScalar<int>("SELECT ativo FROM contacorrente WHERE numero=123"));
        Assert.Equal(3, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='table'"));
    }

    [Fact]
    public void Legacy_migration_preserves_movements_and_idempotency_and_fixes_foreign_key()
    {
        using var connection = Connections.Open();
        CreateLegacy(connection, false);
        new DatabaseBootstrap(Connections).Setup();
        new DatabaseBootstrap(Connections).Setup();
        Assert.Equal("B6BAFC09-6967-ED11-A567-055DFA4A16C9", connection.ExecuteScalar<string>("SELECT idcontacorrente FROM contacorrente WHERE numero=123"));
        Assert.Equal("B6BAFC09-6967-ED11-A567-055DFA4A16C9", connection.ExecuteScalar<string>("SELECT idcontacorrente FROM movimento"));
        Assert.Equal(12.34, connection.ExecuteScalar<double>("SELECT valor FROM movimento"));
        Assert.Equal("original-result", connection.ExecuteScalar<string>("SELECT resultado FROM idempotencia"));
        Assert.Equal("TEXT(37)", connection.ExecuteScalar<string>("SELECT type FROM pragma_table_info('movimento') WHERE name='idcontacorrente'"));
        Assert.Empty(connection.Query("PRAGMA foreign_key_check"));
        var exception = Assert.Throws<SqliteException>(() => connection.Execute("INSERT INTO movimento VALUES ('invalid','missing','2026-10-09','C',1)"));
        Assert.Equal(19, exception.SqliteErrorCode);
    }

    [Fact]
    public void Invalid_legacy_data_rolls_back_the_entire_migration()
    {
        using var connection = Connections.Open();
        CreateLegacy(connection, true);
        Assert.Throws<InvalidOperationException>(() => new DatabaseBootstrap(Connections).Setup());
        Assert.Equal("INTEGER(10)", connection.ExecuteScalar<string>("SELECT type FROM pragma_table_info('movimento') WHERE name='idcontacorrente'"));
        Assert.Equal("B6BAFC09 -6967-ED11-A567-055DFA4A16C9", connection.ExecuteScalar<string>("SELECT idcontacorrente FROM contacorrente WHERE numero=123"));
        Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM contacorrente"));
    }

    private static void CreateLegacy(SqliteConnection connection, bool orphan)
    {
        connection.Execute("PRAGMA foreign_keys=OFF;");
        connection.Execute("""
            CREATE TABLE contacorrente (idcontacorrente TEXT(37) PRIMARY KEY, numero INTEGER NOT NULL UNIQUE, nome TEXT NOT NULL, ativo INTEGER NOT NULL);
            CREATE TABLE movimento (idmovimento TEXT PRIMARY KEY, idcontacorrente INTEGER(10) NOT NULL, datamovimento TEXT NOT NULL, tipomovimento TEXT NOT NULL, valor REAL NOT NULL, FOREIGN KEY(idcontacorrente) REFERENCES contacorrente(idcontacorrente));
            CREATE TABLE idempotencia (chave_idempotencia TEXT PRIMARY KEY, requisicao TEXT, resultado TEXT);
            INSERT INTO contacorrente VALUES ('B6BAFC09 -6967-ED11-A567-055DFA4A16C9',123,'Katherine Sanchez',1);
            INSERT INTO movimento VALUES ('original-movement',@accountId,'09/10/2026','C',12.34);
            INSERT INTO idempotencia VALUES ('original-key','original-request','original-result');
            """, new { accountId = orphan ? "missing-account" : "B6BAFC09 -6967-ED11-A567-055DFA4A16C9" });
        connection.Execute("PRAGMA foreign_keys=ON;");
    }

    public void Dispose() => File.Delete(path);
}
