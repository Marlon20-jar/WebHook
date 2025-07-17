using EasyNetQ;
using EasyNetQ.Topology;
using Polly;
using Serilog;
using System;
using System.Data;
using System.Threading.Channels;
using WAccesos.Models;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace WAccesos
{
    public class PublisherAccesos
    {
        //private readonly IBus _bus;
        private readonly IConfiguration _configuration;
        private readonly ILogger<PublisherAccesos> _logger;
        private readonly ConexionSQL _conexionSQL;
        //private readonly string _connectionRabbit;
        private readonly IAdvancedBus _advancedBus;

        public PublisherAccesos(IConfiguration configuration, ILogger<PublisherAccesos> logger, ConexionSQL conexionSQL, IBus bus)
        {
            _configuration = configuration;
            //_connectionRabbit = configuration.GetConnectionString("EasyNetQConfig")
            //   ?? throw new InvalidOperationException("Connection Rabbit not Found.");
            _logger = logger;
            _conexionSQL = conexionSQL;
            _advancedBus = bus.Advanced;
        }

        //public async Task MsgPublisherAsync(AccesoRequest msg)
        //{
        //    try
        //    {
        //        using (var bus = RabbitHutch.CreateBus(_connectionRabbit).Advanced)
        //        {
        //            Log.Information("Conectando a RabbitMQ...");
        //            var queue = bus.QueueDeclare("AccesosResultadosCRMPrueba");
        //            var exchange = bus.ExchangeDeclare("AccesosResultadosCRMPrueba", ExchangeType.Topic);
        //            bus.Bind(exchange, queue, "AccesosResultadosCRMPrueba");

        //            var publishMessage = new Message<AccesoRequest>(msg);

        //            await Policy
        //                .Handle<Exception>()
        //                .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(2),
        //                    (exception, timeSpan, retryCount, context) =>
        //                    {
        //                        Log.Warning($"Reintentando publicación ({retryCount}) después de fallo: {exception.Message}");
        //                    })
        //                .ExecuteAsync(() => bus.PublishAsync(exchange, "AccesosResultadosCRMPrueba", false, publishMessage));

        //            Log.Information($"Mensaje publicado con éxito: {msg.Uuid}");

        //            // Registrar en la bitácora
        //            await _conexionSQL.EjecutarAsync(
        //                "INSERT INTO BITACORA_ACCESOS (DATOS, MENSAJE, ESTATUS, IS_ENTRANCE, UUID_PROGRAMA_ACCESOS, NOMBRE_PROGRAMA, ID_PROGRAMA, HORA_INICIO, HORA_FINALIZACION, UUID_ACCESS_POINT_ACCESOS, OPERATION, MODE, ACTIVE, UUID_CREDENCIAL, UUID_PERSONA, NOMBRE_PERSONA, ID_PERSONA, TENANT_ID, FECHA_REG, TIENE_BENEFICIO, DATE) " +
        //                "VALUES (@Uuid, @Mensaje, 'ENCOLADO', @IS_ENTRANCE, @UUID_PROGRAMA_ACCESOS, @NOMBRE_PROGRAMA, @ID_PROGRAMA, @HORA_INICIO, @HORA_FINALIZACION, @UUID_ACCESS_POINT_ACCESOS, @OPERATION, @MODE, @ACTIVE, @UUID_CREDENCIAL, @UUID_PERSONA, @NOMBRE_PERSONA, @ID_PERSONA, @TENANT_ID, GETDATE(), @TIENE_BENEFICIO, @DATE)",
        //                new
        //                {
        //                    Uuid = msg.Uuid,
        //                    Mensaje = "Datos Encolados",
        //                    IS_ENTRANCE = msg.isEntrance,
        //                    UUID_PROGRAMA_ACCESOS = msg.Resource.Id,
        //                    NOMBRE_PROGRAMA = msg.Resource.Name,
        //                    ID_PROGRAMA = int.Parse(msg.Resource.ExternalId),
        //                    HORA_INICIO = msg.Resource.StartHour,
        //                    HORA_FINALIZACION = msg.Resource.EndHour,
        //                    UUID_ACCESS_POINT_ACCESOS = msg.AccessPoint.Id,
        //                    OPERATION = msg.AccessPoint.Operation,
        //                    MODE = msg.AccessPoint.Mode,
        //                    ACTIVE = msg.AccessPoint.active,
        //                    UUID_CREDENCIAL = msg.Credential.Id,
        //                    UUID_PERSONA = msg.Credential.Beneficiary.Id,
        //                    NOMBRE_PERSONA = msg.Credential.Beneficiary.Name.ToUpper(),
        //                    ID_PERSONA = int.Parse(msg.Credential.Beneficiary.ExternalId),
        //                    TENANT_ID = msg.AccessPoint.tenantId,
        //                    TIENE_BENEFICIO = msg.isBeneficiario,
        //                    DATE = msg.date
        //                });
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        Log.Error(ex, "Error al publicar mensaje en RabbitMQ");

        //        // Registrar el error en la base de datos (bitácora)
        //        await _conexionSQL.EjecutarAsync(
        //            "INSERT INTO BITACORA_ACCESOS (DATOS, MENSAJE, ESTATUS, FECHA_REG) " +
        //            "VALUES (@Uuid, @Mensaje, 'ERROR_PUBLICACION', GETDATE())",
        //            new
        //            {
        //                Uuid = msg.Uuid,
        //                Mensaje = ex.Message,
        //            });

        //        // Opcional: Si necesitas re-levantar la excepción para ser capturada en un nivel superior
        //        throw;
        //    }
        //}
        //ESTA FUNCIÓN SE PLASMO POR SI SUCEDE TIME OUT DEPLEGADO POR SERVIDORES LENTOS COMO AGUASCALIENTES
        public async Task PublicarEnLotesAsync(List<AccesoRequest> mensajes, int maxConcurrent = 10)
        {
            var queue = _advancedBus.QueueDeclare("AccesosResultadosCRM", durable: true, exclusive: false, autoDelete: false);
            var exchange = _advancedBus.ExchangeDeclare("AccesosResultadosCRM", ExchangeType.Topic, durable: true);
            _advancedBus.Bind(exchange, queue, "AccesosResultadosCRM");

            var semaphore = new SemaphoreSlim(maxConcurrent); // controla concurrencia

            int total = mensajes.Count;
            int publicados = 0;
            int fallidos = 0;

            var timeout = TimeSpan.FromSeconds(20); // Timeout individual

            var tasks = mensajes.Select(async msg =>
            {
                await semaphore.WaitAsync();
                try
                {
                    // LIMPIAR TODOS LOS CAMPOS STRING
                    msg.Uuid = LimpiaTexto(msg.Uuid);
                    msg.Resource.Id = LimpiaTexto(msg.Resource.Id);
                    msg.Resource.Name = LimpiaTexto(msg.Resource.Name);
                    msg.Resource.ExternalId = LimpiaTexto(msg.Resource.ExternalId);
                    msg.Resource.StartHour = LimpiaTexto(msg.Resource.StartHour);
                    msg.Resource.EndHour = LimpiaTexto(msg.Resource.EndHour);
                    msg.AccessPoint.Id = LimpiaTexto(msg.AccessPoint.Id);
                    msg.Credential.Id = LimpiaTexto(msg.Credential.Id);
                    msg.Credential.Beneficiary.Id = LimpiaTexto(msg.Credential.Beneficiary.Id);
                    msg.Credential.Beneficiary.Name = LimpiaTexto(msg.Credential.Beneficiary.Name);
                    msg.Credential.Beneficiary.ExternalId = LimpiaTexto(msg.Credential.Beneficiary.ExternalId);
                    msg.AccessPoint.tenantId = LimpiaTexto(msg.AccessPoint.tenantId);
                    msg.date = LimpiaTexto(msg.date);

                    var message = new Message<AccesoRequest>(msg);

                    await Policy
                       .Handle<Exception>()
                       .WaitAndRetryAsync(3, retry => TimeSpan.FromSeconds(2),
                           (exception, _, retryCount, _) =>
                           {
                               Log.Warning($"Reintento #{retryCount} por fallo: {exception.Message}");
                           })
                       .ExecuteAsync(() => PublicarConTimeout(() =>
                           _advancedBus.PublishAsync(exchange, "AccesosResultadosCRM", false, message), timeout));

                    Interlocked.Increment(ref publicados);
                    Log.Information($"✅ Publicado: {msg.Uuid}");

                    await RegistrarBitacoraAsync(msg, "ENCOLADO", "Datos Encolados");


                }
                catch (Exception ex)
                {

                    Interlocked.Increment(ref fallidos);
                    Log.Error($"❌ Error publicando {msg.Uuid}: {ex.Message}");

                    Log.Error("Excepción completa: {Exception}", ex);

                    Log.Error("StackTrace: {StackTrace}", ex.StackTrace);

                    if (ex is TimeoutException)
                    {
                        Log.Warning($"⏱ Timeout al publicar mensaje {msg.Uuid}: {ex.Message}");
                    }
                    else
                    {
                        Log.Error($"❌ Error publicando {msg.Uuid}: {ex.Message}");
                        Log.Error("Excepción completa: {Exception}", ex);

                        if (ex.InnerException != null)
                        {
                            Log.Error("InnerException: {InnerException}", ex.InnerException.Message);
                            Log.Error("Inner StackTrace: {InnerStackTrace}", ex.InnerException.StackTrace);
                        }
                    }


                    await RegistrarErrorBitacoraAsync(msg.Uuid, ex.ToString());
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            // Resumen
            Log.Information($"📊 Publicación finalizada: Total={total}, Publicados={publicados}, Fallidos={fallidos}");
        }
        private async Task PublicarConTimeout(Func<Task> publishFunc, TimeSpan timeout)
        {
            var publishTask = publishFunc();
            var delayTask = Task.Delay(timeout);

            var completedTask = await Task.WhenAny(publishTask, delayTask);

            if (completedTask == delayTask)
                throw new TimeoutException("⏱ Timeout al intentar publicar en RabbitMQ.");

            await publishTask;
        }
        private string LimpiaTexto(string input)
        {
            return (input ?? "")
                .Replace("\"", "'")  // reemplaza comillas dobles por simples
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();
        }

        private async Task RegistrarBitacoraAsync(AccesoRequest msg, string estatus, string mensaje)
        {
            await _conexionSQL.EjecutarAsync(
                "INSERT INTO BITACORA_ACCESOS (DATOS, MENSAJE, ESTATUS, IS_ENTRANCE, UUID_PROGRAMA_ACCESOS, NOMBRE_PROGRAMA, ID_PROGRAMA, HORA_INICIO, HORA_FINALIZACION, UUID_ACCESS_POINT_ACCESOS, OPERATION, MODE, ACTIVE, UUID_CREDENCIAL, UUID_PERSONA, NOMBRE_PERSONA, ID_PERSONA, TENANT_ID, FECHA_REG, TIENE_BENEFICIO, DATE) " +
                "VALUES (@Uuid, @Mensaje, @Estatus, @IS_ENTRANCE, @UUID_PROGRAMA_ACCESOS, @NOMBRE_PROGRAMA, @ID_PROGRAMA, @HORA_INICIO, @HORA_FINALIZACION, @UUID_ACCESS_POINT_ACCESOS, @OPERATION, @MODE, @ACTIVE, @UUID_CREDENCIAL, @UUID_PERSONA, @NOMBRE_PERSONA, @ID_PERSONA, @TENANT_ID, GETDATE(), @TIENE_BENEFICIO, @DATE)",
                new
                {
                    Uuid = msg.Uuid,
                    Mensaje = mensaje,
                    Estatus = estatus,
                    IS_ENTRANCE = msg.isEntrance,
                    UUID_PROGRAMA_ACCESOS = msg.Resource.Id,
                    NOMBRE_PROGRAMA = msg.Resource.Name,
                    ID_PROGRAMA = int.Parse(msg.Resource.ExternalId),
                    HORA_INICIO = msg.Resource.StartHour,
                    HORA_FINALIZACION = msg.Resource.EndHour,
                    UUID_ACCESS_POINT_ACCESOS = msg.AccessPoint.Id,
                    OPERATION = msg.AccessPoint.Operation,
                    MODE = msg.AccessPoint.Mode,
                    ACTIVE = msg.AccessPoint.active,
                    UUID_CREDENCIAL = msg.Credential.Id,
                    UUID_PERSONA = msg.Credential.Beneficiary.Id,
                    NOMBRE_PERSONA = msg.Credential.Beneficiary.Name.ToUpper(),
                    ID_PERSONA = int.Parse(msg.Credential.Beneficiary.ExternalId),
                    TENANT_ID = msg.AccessPoint.tenantId,
                    TIENE_BENEFICIO = msg.isBeneficiario,
                    DATE = msg.date
                });
        }

        private async Task RegistrarErrorBitacoraAsync(string uuid, string mensaje)
        {
            await _conexionSQL.EjecutarAsync(
                "INSERT INTO BITACORA_ACCESOS (DATOS, MENSAJE, ESTATUS, FECHA_REG) " +
                "VALUES (@Uuid, @Mensaje, 'ERROR_PUBLICACION', GETDATE())",
                new
                {
                    Uuid = uuid,
                    Mensaje = mensaje
                });
        }
    }
}