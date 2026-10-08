using Microsoft.Data.Sqlite;

namespace Horyzont.Persistence.Tests.Support;

/// <summary>Direct SQL on a world file for assertions and for damaging it on purpose.</summary>
internal static class RawDb
{
    public static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    public static object? Scalar(string path, string sql)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    public static long Long(string path, string sql) => Scalar(path, sql) is long value ? value : -1;

    public static void Execute(string path, string sql, params (string Name, object Value)[] parameters)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.ExecuteNonQuery();
    }

    public static List<long> JournalIds(string path)
    {
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT op_id FROM journal ORDER BY op_id;";
        using var reader = command.ExecuteReader();
        var ids = new List<long>();
        while (reader.Read())
        {
            ids.Add(reader.GetInt64(0));
        }

        return ids;
    }
}
