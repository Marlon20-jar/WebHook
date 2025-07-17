using System.Data;
using System.Data.SqlClient;
using Dapper;
using Serilog;

namespace WAccesos.Models
{
    public class ConexionSQL
    {
        private readonly string _connectionString;
        private readonly ILogger<ConexionSQL> _logger;

        public ConexionSQL(IConfiguration configuration, ILogger<ConexionSQL> logger)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string not found.");
            _logger = logger;
        }

        private async Task<T> ExecuteAsync<T>(Func<SqlConnection, Task<T>> operation)
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            return await operation(connection);
        }

        public async Task<int> EjecutarAsync(string query, object parametros = null)
        {
            try
            {
                return await ExecuteAsync(conn => conn.ExecuteAsync(query, parametros));
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Error ejecutando la consulta: {Query}", query);
                Log.Error(ex, "Error ejecutando la consulta: {Query}", query);
                return 0;
            }
        }

        public async Task<T> ObtenerValorAsync<T>(string query, object parametros = null)
        {
            try
            {
                return await ExecuteAsync(conn => conn.ExecuteScalarAsync<T>(query, parametros));
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Error obteniendo valor con la consulta: {Query}", query);
                Log.Error(ex, "Error obteniendo valor con la consulta: {Query}", query);
                return default;
            }
        }

        public async Task<IEnumerable<T>> ObtenerListaAsync<T>(string query, object parametros = null)
        {
            try
            {
                return await ExecuteAsync(conn => conn.QueryAsync<T>(query, parametros));
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Error obteniendo lista con la consulta: {Query}", query);
                Log.Error(ex, "Error obteniendo lista con la consulta: {Query}", query);
                return Array.Empty<T>();
            }
        }

        public async Task<T> ObtenerAsync<T>(string query, object parametros = null)
        {
            try
            {
                return await ExecuteAsync(conn => conn.QueryFirstOrDefaultAsync<T>(query, parametros));
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Error obteniendo objeto con la consulta: {Query}", query);
                Log.Error(ex, "Error obteniendo objeto con la consulta: {Query}", query);
                return default;
            }
        }

        public async Task<IEnumerable<T>> EjecutarSPAsync<T>(string nombreSP, object parametros = null)
        {
            try
            {
                return await ExecuteAsync(conn => conn.QueryAsync<T>(nombreSP, parametros, commandType: CommandType.StoredProcedure));
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Error ejecutando procedimiento almacenado: {StoredProc}", nombreSP);
                Log.Error(ex, "Error obteniendo objeto con la consulta: {StoredProc}", nombreSP);
                return Array.Empty<T>();
            }
        }

        public async Task<DataTable> ExecuteQueryAsyncVallidar(string queryString)
        {
            DataTable dt = new DataTable();
            try
            {
                using (SqlConnection connection = new SqlConnection(_connectionString))
                {
                    SqlCommand command = new SqlCommand(queryString, connection);
                    SqlDataAdapter adapter = new SqlDataAdapter(command);
                    await Task.Run(() => adapter.Fill(dt));  // Llenado asincrónico
                }
            }
            catch (SqlException ex)
            {
                Console.WriteLine($"Error en la ejecución de la consulta: {ex}");
                Log.Error(ex, "Error en la ejecución de la consulta. Query: {Query}", queryString);
            }
            return dt;
        }
    }
}