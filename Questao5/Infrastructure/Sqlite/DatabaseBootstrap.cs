using Dapper;

namespace Questao5.Infrastructure.Sqlite;

public sealed class DatabaseBootstrap(SqliteConnectionFactory connections) : IDatabaseBootstrap
{
    private const string BrokenId = "B6BAFC09 -6967-ED11-A567-055DFA4A16C9";
    private const string CorrectId = "B6BAFC09-6967-ED11-A567-055DFA4A16C9";
    private const string MovementDdl = """
        CREATE TABLE movimento (
            idmovimento TEXT(37) PRIMARY KEY,
            idcontacorrente TEXT(37) NOT NULL,
            datamovimento TEXT(25) NOT NULL,
            tipomovimento TEXT(1) NOT NULL CHECK(tipomovimento IN ('C', 'D')),
            valor REAL NOT NULL,
            FOREIGN KEY(idcontacorrente) REFERENCES contacorrente(idcontacorrente)
        );
        """;

    public void Setup()
    {
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        connection.Execute("PRAGMA defer_foreign_keys = ON;", transaction: transaction);
        connection.Execute("""
            CREATE TABLE IF NOT EXISTS contacorrente (
                idcontacorrente TEXT(37) PRIMARY KEY,
                numero INTEGER(10) NOT NULL UNIQUE,
                nome TEXT(100) NOT NULL,
                ativo INTEGER(1) NOT NULL DEFAULT 0 CHECK(ativo IN (0,1))
            );
            CREATE TABLE IF NOT EXISTS idempotencia (
                chave_idempotencia TEXT(37) PRIMARY KEY,
                requisicao TEXT(1000),
                resultado TEXT(1000)
            );
            """, transaction: transaction);

        var exists = connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='movimento'", transaction: transaction) != 0;
        if (!exists)
            connection.Execute(MovementDdl, transaction: transaction);
        else
        {
            var type = connection.Query<ColumnInfo>("PRAGMA table_info(movimento)", transaction: transaction)
                .Single(column => column.Name == "idcontacorrente").Type;
            if (!type.StartsWith("TEXT", StringComparison.OrdinalIgnoreCase))
            {
                // Rebuild only the incompatible legacy table, preserving all existing movements.
                connection.Execute("ALTER TABLE movimento RENAME TO movimento_legacy;", transaction: transaction);
                connection.Execute(MovementDdl, transaction: transaction);
                connection.Execute("""
                    INSERT INTO movimento (idmovimento, idcontacorrente, datamovimento, tipomovimento, valor)
                    SELECT idmovimento, CAST(idcontacorrente AS TEXT), datamovimento, tipomovimento, valor FROM movimento_legacy;
                    DROP TABLE movimento_legacy;
                    """, transaction: transaction);
            }
        }

        // Fix the supplied seed typo together with its dependent rows.
        connection.Execute("UPDATE movimento SET idcontacorrente=@CorrectId WHERE idcontacorrente=@BrokenId", new { CorrectId, BrokenId }, transaction);
        connection.Execute("UPDATE contacorrente SET idcontacorrente=@CorrectId WHERE idcontacorrente=@BrokenId", new { CorrectId, BrokenId }, transaction);
        connection.Execute("CREATE INDEX IF NOT EXISTS ix_movimento_conta ON movimento(idcontacorrente)", transaction: transaction);

        var accounts = new[]
        {
            new { Id = CorrectId, Number = 123, Holder = "Katherine Sanchez", Active = 1 },
            new { Id = "FA99D033-7067-ED11-96C6-7C5DFA4A16C9", Number = 456, Holder = "Eva Woodward", Active = 1 },
            new { Id = "382D323D-7067-ED11-8866-7D5DFA4A16C9", Number = 789, Holder = "Tevin Mcconnell", Active = 1 },
            new { Id = "F475F943-7067-ED11-A06B-7E5DFA4A16C9", Number = 741, Holder = "Ameena Lynn", Active = 0 },
            new { Id = "BCDACA4A-7067-ED11-AF81-825DFA4A16C9", Number = 852, Holder = "Jarrad Mckee", Active = 0 },
            new { Id = "D2E02051-7067-ED11-94C0-835DFA4A16C9", Number = 963, Holder = "Elisha Simons", Active = 0 }
        };
        connection.Execute("""
            INSERT INTO contacorrente (idcontacorrente, numero, nome, ativo)
            SELECT @Id, @Number, @Holder, @Active
            WHERE NOT EXISTS (SELECT 1 FROM contacorrente WHERE numero=@Number OR idcontacorrente=@Id)
            """, accounts, transaction);
        var violations = connection.Query("PRAGMA foreign_key_check", transaction: transaction);
        if (violations.Any())
            throw new InvalidOperationException("O banco contém movimentos sem conta correspondente. A inicialização foi revertida.");
        transaction.Commit();
    }

    private sealed class ColumnInfo
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
    }
}
