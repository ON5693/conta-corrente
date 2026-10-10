using Microsoft.Data.Sqlite;

namespace Questao5.Infrastructure.Sqlite;

public sealed class SqliteConnectionFactory(DatabaseConfig config)
{
    public SqliteConnection Open()
    {
        var settings = new SqliteConnectionStringBuilder(config.Name)
        {
            ForeignKeys = true,
            DefaultTimeout = 30
        };
        var connection = new SqliteConnection(settings.ToString());
        try
        {
            connection.Open();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }
}
