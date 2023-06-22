using System;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Avalon.Database
{
    public class Database : IDisposable
    {
        private SqliteConnection _connection;
        
        public Database()
        {
            _connection = new SqliteConnection("Data Source=avalon.db");
        }
        
        public async Task Open(CancellationToken cancellationToken = default)
        {
            await _connection.OpenAsync(cancellationToken);
        }
        
        public async Task<T> Query<T>(string sql)
        {

            var result = await _connection.QueryFirstAsync<T>("SELECT * FROM users WHERE id = @Id", new { Id = 1 });

            return result;
        }


        public void Dispose()
        {
            _connection?.Close();
            _connection?.Dispose();
        }
    }
}
