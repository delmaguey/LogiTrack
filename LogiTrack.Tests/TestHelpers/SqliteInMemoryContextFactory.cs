using LogiTrack.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LogiTrack.Tests.TestHelpers
{
    // Backs tests with a real SQLite database (in memory) rather than EF Core's InMemory
    // provider, because ExecuteUpdateAsync/ExecuteDeleteAsync - used throughout the
    // controllers - require a relational provider and throw on InMemory.
    public sealed class SqliteInMemoryContextFactory : IDisposable
    {
        private readonly SqliteConnection _connection;

        public SqliteInMemoryContextFactory()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            using var context = CreateContext();
            context.Database.EnsureCreated();
        }

        public LogiTrackDBContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<LogiTrackDBContext>()
                .UseSqlite(_connection)
                .Options;

            return new LogiTrackDBContext(options);
        }

        public void Dispose() => _connection.Dispose();
    }
}
