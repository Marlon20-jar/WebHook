using EasyNetQ;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption.ConfigurationModel;
using Newtonsoft.Json;
using reciveAccesos.Models;
using Serilog;
using System;
using System.Collections;
using System.Data.SqlClient;
using System.Drawing;
using System.Text;
using System.Text.RegularExpressions;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace reciveAccesos
{
    public class SubscriberA
    {
        private readonly IConfiguration _configuration;
        private readonly IAdvancedBus _bus;
        private readonly ConexionSQL _conexionSQL;
        private readonly ConsumirApiWhats _consumirApiWhats;
        private readonly int _IdGrupoUsuario;
        private int _totalRecibidos = 0;
        private int _procesadosExitosos = 0;
        private int _procesadosFallidos = 0;

        public SubscriberA(IConfiguration configuration, IAdvancedBus bus, ConexionSQL conexionSQL, ConsumirApiWhats consumirApiWhats)
        {
            _configuration = configuration;
            _bus = bus;
            _conexionSQL = conexionSQL;
            _consumirApiWhats = consumirApiWhats;
            if (!int.TryParse(configuration["datos:ID_PARAMETRO"], out _IdGrupoUsuario))
            {
                Log.Error("El valor de 'datos:ID_PARAMETRO' no es un número entero válido.");
                throw new Exception("El valor de 'datos:ID_PARAMETRO' no es un número entero válido.");
            }
        }

        //public async Task msgSubscriber()
        //{
        //    var rabbitMqConnectionString = _configuration.GetConnectionString("EasyNetQConfig");
        //    try
        //    {
        //        Log.Information("Conectando a RabbitMQ...");
        //        var queue = _bus.QueueDeclare("AccesosResultadosCRMPrueba");
        //        Log.Information("Suscripción a la cola AccesosResultadosCRMPrueba completada.");
        //        _bus.Consume(queue, async (body, properties, info) =>
        //        {
        //            var message = Encoding.UTF8.GetString(body.ToArray());
        //            Message objectDeserialized = JsonConvert.DeserializeObject<Message>(message);

        //            if (objectDeserialized == null)
        //            {
        //                Log.Error("El mensaje deserializado es nulo para recibir datos.");
        //                throw new Exception("El mensaje deserializado es nulo.");
        //                //await RegistrarErrorAsync("Sin UUID", "Objeto Recive vacio en Rabbit");
        //            }
        //            Log.Information(objectDeserialized.ToString());
        //            await ProcessMessage(objectDeserialized);
        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine("Ocurrió una excepción: " + ex.Message);

        //        // Log del error
        //        Log.Error($"Error al suscribirse a la cola AccesosResultadosCRMPrueba: {ex.Message}");

        //        await RegistrarErrorAsync("Sin UUID", "Algo paso en RecciveAccesos");
        //    }
        //}

        public async Task msgSubscriber()
        {
            var rabbitMqConnectionString = _configuration.GetConnectionString("EasyNetQConfig");
            var maxConcurrency = 5; // Ajusta según tu base de datos
            var semaphore = new SemaphoreSlim(maxConcurrency);
            try
            {
                var queue = _bus.QueueDeclare("AccesosResultadosCRM");
                Log.Information("Esperando mensajes de la cola...");

                _bus.Consume(queue, async (body, properties, info) =>
                {
                    Interlocked.Increment(ref _totalRecibidos);

                    await semaphore.WaitAsync(); // Espera turno

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var message = Encoding.UTF8.GetString(body.ToArray());
                            var objectDeserialized = JsonConvert.DeserializeObject<Message>(message);

                            Log.Information($"📩 Recibido: {objectDeserialized.Uuid}");

                            await ProcessMessage(objectDeserialized);

                            Interlocked.Increment(ref _procesadosExitosos);
                            Log.Information($"✅ Procesado correctamente: {objectDeserialized.Uuid}");
                        }
                        catch (Exception ex)
                        {
                            var message = new Message();

                            Interlocked.Increment(ref _procesadosFallidos);
                            Log.Error("⚠️ Error al procesar mensaje: " + ex.ToString());

                            await RegistrarErrorAsync(message.Uuid, ex.ToString());

                            // Si tu cola usa DLX, esto es correcto:
                            throw;
                        }
                        finally
                        {
                            semaphore.Release(); // Libera el slot
                            Log.Information($"📊 Resumen actual: TotalRecibidos={_totalRecibidos}, Exitosos={_procesadosExitosos}, Fallidos={_procesadosFallidos}");
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                //Console.WriteLine("Ocurrió una excepción para reciveAccesos Accesos: " + ex.Message);
                var message = new Message();

                // Log del error
                Log.Error($"Ocurrió una excepción al recibir datos de la cola: {ex}");

                if (ex is TaskCanceledException)
                {
                    Log.Warning("El mensaje fue cancelado. Posible timeout o cierre del servicio.");
                }
                Log.Information(_bus.IsConnected ? "RabbitMQ sigue conectado." : "RabbitMQ se desconectó.");

                await RegistrarErrorAsync(message.Uuid, ex.ToString());
            }
        }

        private async Task ProcessMessage(Message message)
        {
            try
            {
                //Validar si los datos encolados llegan completos

                if (!ValidarCamposObligatorios(message, out string errorValidacion))
                {
                    await RegistrarErrorAsync(message.Uuid, errorValidacion);
                    Log.Error("❌ Faltan campos obligatorios en el reciveAccesos.");
                    return;
                }


                //bool insertado = await InsertarEnWebhookAsync(message);
                string? uuidInsertado = await InsertarEnWebhookAsync(message);

                if (uuidInsertado == null)
                {
                    await RegistrarErrorAsync(message.Uuid, "Error al insertar en WEBHOOK_ACCESOS");
                    Log.Error("Error al insertar en WEBHOOK_ACCESOS");
                    return;
                }

                var DatosNecesarios = await ObtenerCelularesAsync(uuidInsertado);

                if (DatosNecesarios == null)
                {
                    await RegistrarErrorAsync(message.Uuid, "Error al consultar datos necesarios para mandar la notificación");
                    Log.Error("Error al consultar datos necesarios para mandar la notificación");
                    return;
                }

                var diccionario = ConvertirAStringDictionary(DatosNecesarios);
                
                //ObtenerIdGrupoUsuario
                var IdGrupoUsuario = await ObtenerIdGrupoUsuario(_IdGrupoUsuario);

                if (IdGrupoUsuario == null)
                {
                    await RegistrarErrorAsync(message.Uuid, "Error al consultar EL ID_Usuario_Grupo");
                    Log.Error("Error al consultar consultar EL ID_Usuario_Grupo para mandar la notificación");
                    return;
                }

                await ProcesarNotificacionesAsync(diccionario, message.Resource.ExternalId, message.Resource.Name, message.Uuid, uuidInsertado, IdGrupoUsuario.VALOR);


                await RegistrarCorrectoAsync(message.Uuid, "Proceso Exitoso");

            }
            catch (Exception ex)
            {
                Log.Error($"Error procesando mensaje {message.Uuid}: {ex.Message}");

                // Llamar a la función para registrar el error
                await RegistrarErrorAsync(message.Uuid, ex.Message);
            }
        }

        private bool ValidarCamposObligatorios(Message message, out string error)
        {
            if (string.IsNullOrWhiteSpace(message.Uuid) ||
                string.IsNullOrWhiteSpace(message.Resource?.Id) ||
                string.IsNullOrWhiteSpace(message.Resource?.ExternalId) ||
                string.IsNullOrWhiteSpace(message.AccessPoint?.Id) ||
                message.AccessPoint?.Operation == 0 || message.AccessPoint?.Mode == 0 ||
                string.IsNullOrWhiteSpace(message.Credential?.Id) ||
                string.IsNullOrWhiteSpace(message.Credential?.Beneficiary?.Id) ||
                string.IsNullOrWhiteSpace(message.Credential?.Beneficiary?.ExternalId) || string.IsNullOrWhiteSpace(message.date))
            {
                error = "Faltan campos obligatorios en el reciveAccesos.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private async Task<string?> InsertarEnWebhookAsync(Message message)
        {
            string? resultado = await _conexionSQL.ObtenerValorAsync<string>(
                "DECLARE @UUID_CONSECUTIVO UNIQUEIDENTIFIER = NEWID(); " +
                "BEGIN TRY " +
                "INSERT INTO [dbo].[WEBHOOK_ACCESOS] (UUID_CONSECUTIVO, IS_ENTRANCE, UUID_PROGRAMA_ACCESOS, NOMBRE_PROGRAMA, ID_PROGRAMA, HORA_INICIO, HORA_FINALIZACION, UUID_ACCESS_POINT_ACCESOS, OPERATION, MODE, ACTIVE, UUID_CREDENCIAL, UUID_PERSONA, NOMBRE_PERSONA, ID_PERSONA, TENANT_ID, FECHA_REG, TIENE_BENEFICIO, DATE) " +
                "VALUES (@UUID_CONSECUTIVO, @IS_ENTRANCE, @UUID_PROGRAMA_ACCESOS, @NOMBRE_PROGRAMA, @ID_PROGRAMA,@HORA_INICIO, @HORA_FINALIZACION, @UUID_ACCESS_POINT_ACCESOS, @OPERATION, @MODE, @ACTIVE, @UUID_CREDENCIAL, @UUID_PERSONA, @NOMBRE_PERSONA, @ID_PERSONA, @TENANT_ID, GETDATE(), @TIENE_BENEFICIO, @DATE); " +
                "END TRY BEGIN CATCH SET @UUID_CONSECUTIVO = NULL; END CATCH; " +
                "SELECT CONVERT(VARCHAR(100), @UUID_CONSECUTIVO);",
                new
                {
                    IS_ENTRANCE = message.isEntrance,
                    UUID_PROGRAMA_ACCESOS = message.Resource.Id,
                    NOMBRE_PROGRAMA = message.Resource.Name,
                    ID_PROGRAMA = int.TryParse(message.Resource.ExternalId, out var idProg) ? idProg : 0,
                    HORA_INICIO = message.Resource.StartHour,
                    HORA_FINALIZACION = message.Resource.EndHour,
                    UUID_ACCESS_POINT_ACCESOS = message.AccessPoint.Id,
                    OPERATION = message.AccessPoint.Operation,
                    MODE = message.AccessPoint.Mode,
                    ACTIVE = message.AccessPoint.active,
                    UUID_CREDENCIAL = message.Credential.Id,
                    UUID_PERSONA = message.Credential.Beneficiary.Id,
                    NOMBRE_PERSONA = message.Credential.Beneficiary.Name,
                    ID_PERSONA = int.TryParse(message.Credential.Beneficiary.ExternalId, out var idPers) ? idPers : 0,
                    TENANT_ID = message.AccessPoint.tenantId,
                    TIENE_BENEFICIO = message.isBeneficiario,
                    DATE = message.date
                });

            return resultado;
        }

        private async Task ProcesarNotificacionesAsync(Dictionary<string, string> datos, string id_programa, string nombre_programa, string uuid, string uuidConsecutivo, string VALOR)
        {
            var notificaciones = await _conexionSQL.ObtenerListaAsync<NotificacionMensaje>(
                "SELECT ID_NOTIFICACION_DESTINATARIO, MENSAJE FROM NOTIFICACIONES_MENSAJES WHERE ID_PROGRAMA = @ID_PROGRAMA AND ACTIVO = 1 order by orden",
                new { ID_PROGRAMA = int.Parse(id_programa) });

            // Si no se obtuvieron notificaciones, registrar el historial y continuar
            if (notificaciones == null || !notificaciones.Any())
            {
                string programName = nombre_programa;
                string notificationText = $"{programName}: Notificación vacía";
                await HistorialNotificacionesAsync(uuid, notificationText, "", "", false, uuidConsecutivo, "");
                return; // Sale del método para continuar el flujo
            }

            foreach (var item in notificaciones)
            {
                if (string.IsNullOrWhiteSpace(item.MENSAJE) || item.ID_NOTIFICACION_DESTINATARIO == 0)
                {
                    string programName = nombre_programa;
                    string notificationText = $"{programName}: Notificación vacía";
                    await HistorialNotificacionesAsync(uuid, notificationText, "", "", false, uuidConsecutivo, "");
                    continue;
                }

                string mensaje = item.MENSAJE;

                switch (item.ID_NOTIFICACION_DESTINATARIO)
                {
                    case 1:
                        datos["nombre"] = datos.GetValueOrDefault("nombre_beneficiario");
                        datos["celular"] = datos.GetValueOrDefault("celular_beneficiario");
                        datos["mensajeExito"] = "Mensaje enviado a beneficiario";
                        datos["mensajeError"] = "No hay número de celular del beneficiario";
                        break;
                    case 2:
                        datos["nombre"] = datos.GetValueOrDefault("nombre_tutor");
                        datos["celular"] = datos.GetValueOrDefault("celular_tutor");
                        datos["mensajeExito"] = "Mensaje enviado al tutor";
                        datos["mensajeError"] = "No hay número de celular del tutor";
                        break;
                    default:
                        await HistorialNotificacionesAsync(uuid, "Proceso Exitoso (sin notificación definida)", "", "", false, uuidConsecutivo, item.MENSAJE);
                        break;
                }

                string marcadores = MarcadorMensajes(mensaje);

                if(marcadores != "")
                {
                    marcadores = marcadores.Substring(0, marcadores.Length - 1);
                    var traduccion = await ObtenerTraduccionMarcadoresAsync(marcadores);

                    //Dictionary<string, string> diccionarioTraductor = new Dictionary<string, string>();

                    //foreach (var i in traduccion)
                    //{
                    //    diccionarioTraductor[i.DESCRIPCION] = i.CAMPO;
                    //}

                    var diccionarioTraductor = ConvertirMarcadorDiccionario(traduccion);
                    mensaje = ReemplazarVariablesEnMensaje(mensaje, datos, diccionarioTraductor);
                }

                await ProcesarMensajeAsync(datos, mensaje, uuid, uuidConsecutivo, VALOR);
            }
        }

        private async Task ProcesarMensajeAsync(Dictionary<string, string> datos, string mensaje, string uuid, string uuidConsecutivo, string VALOR)
        {
            var celular = datos.GetValueOrDefault("celular");
            var mensajeExito = datos.GetValueOrDefault("mensajeExito");
            var mensajeError = datos.GetValueOrDefault("mensajeError");

            if (!string.IsNullOrWhiteSpace(celular))
            {
                string mensajeFinal;

                var resultado = await _consumirApiWhats.MandarMensajeWhatsAsync(mensaje, celular, VALOR);

                if (resultado.Exitoso)
                {
                    // Validación de éxito por campo status opcional
                    if (resultado.ContenidoRaw.Contains("\"status\":\"Exitoso\"", StringComparison.OrdinalIgnoreCase))
                    {
                        mensajeFinal = mensajeExito;
                    }
                    else
                    {
                        mensajeFinal = $"{mensajeExito} - Respuesta ambigua";
                    }
                }
                else
                {
                    if (resultado.ContenidoRaw.Contains("No hay ningún número", StringComparison.OrdinalIgnoreCase))
                    {
                        mensajeFinal = $"{mensajeError} - Número sin sesión activa";
                    }
                    else
                    {
                        mensajeFinal = $"{resultado.Mensaje}";
                    }
                }

                await HistorialNotificacionesAsync(uuid, mensajeFinal, "", celular, false, uuidConsecutivo, mensaje);
                Log.Information(mensajeFinal);
            }
            else
            {
                await HistorialNotificacionesAsync(uuid, mensajeError, "", "", false, uuidConsecutivo, mensaje);
                Log.Information(mensajeError);
            }
        }

        private string MarcadorMensajes(string mensaje)
        {
            //string mensaje = "Hi {Name}, You should come and see this {PLACE} - From {SenderName}";
            Regex reg = new Regex(@"{{\w+}}");
            string resultado = "";
            foreach (Match match in reg.Matches(mensaje))
            {
                //Console.WriteLine(match.Value);
                resultado += "'" + match.Value +  "',";
            }

            return resultado;
        }

        private async Task RegistrarErrorAsync(string uuid, string mensaje)
        {
            //await _conexionSQL.EjecutarAsync(
            //    "UPDATE BITACORA_ACCESOS SET MENSAJE = @Mensaje, ESTATUS = @Estatus " +
            //    " WHERE DATOS = @Datos;",
            //    new { Mensaje = mensaje, Estatus = "PROCESADO_ERRONEO", Datos = uuid });

            await ExecuteWithRetry(() => Task.Run(() =>
            {
                _conexionSQL.EjecutarAsync(
                    "UPDATE BITACORA_ACCESOS SET MENSAJE = @Mensaje, ESTATUS = @Estatus " +
                    " WHERE DATOS = @Datos;",
                    new { Mensaje = mensaje, Estatus = "PROCESADO_ERRONEO", Datos = uuid });
            }));

            Log.Error($"Error registrado en BITACORA_ACCESOS para UUID {uuid}: {mensaje}");
        }


        private async Task RegistrarCorrectoAsync(string uuid, string mensaje)
        {
            //await _conexionSQL.EjecutarAsync(
            //    "UPDATE BITACORA_ACCESOS SET MENSAJE = @Mensaje, ESTATUS = @Estatus" +
            //    " WHERE DATOS = @Datos;",
            //   new { Mensaje = mensaje, Estatus = "PROCESADO_CORRECTAMENTE", Datos = uuid });

            await ExecuteWithRetry(() => Task.Run(() =>
            {
                 _conexionSQL.EjecutarAsync(
                    "UPDATE BITACORA_ACCESOS SET MENSAJE = @Mensaje, ESTATUS = @Estatus" +
                    " WHERE DATOS = @Datos;",
                   new { Mensaje = mensaje, Estatus = "PROCESADO_CORRECTAMENTE", Datos = uuid });

            }));

            Log.Information("Proceso Exitoso");
        }


        private async Task HistorialNotificacionesAsync(string uuid, string mensaje, string celularTutor, string celularBeneficiario, bool tiene_tutor, string uuidConsecutivo, string mensajeNotificacion)
        {
            await _conexionSQL.EjecutarAsync(
                "INSERT INTO [dbo].[HISTORIAL_ACCESOS_NOTIFICACIONES] (DATOS, DETALLE, CELULAR_TUTOR, CELULAR_BENEFICIARIO, FECHA_REG, TIENE_TUTOR, UUID_CONSECUTIVO, MENSAJE) " +
                "VALUES (@DATOS, @DETALLE, @CELULAR_TUTOR, @CELULAR_BENEFICIARIO, GETDATE(), @TIENE_TUTOR, @UUID_CONSECUTIVO, @MENSAJE)",
               new
               {
                   DATOS = uuid,
                   DETALLE = mensaje,
                   CELULAR_TUTOR = celularTutor,
                   CELULAR_BENEFICIARIO = celularBeneficiario,
                   TIENE_TUTOR = tiene_tutor,
                   UUID_CONSECUTIVO = uuidConsecutivo,
                   MENSAJE = mensajeNotificacion,
               });

            Log.Information(mensaje);
        }

        private async Task<CelularesDto> ObtenerCelularesAsync(string uuid)
        {
            string query = @"
            SELECT 
            cp.nombre nombre_beneficiario, 
            cp.celular celular_beneficiario,
            cpt.nombre nombre_tutor,
            cpt.celular celular_tutor,
            SUBSTRING(wac.[date], 1, CHARINDEX('T', wac.[date]) - 1) fecha,
            SUBSTRING(wac.[date], CHARINDEX('T', wac.[date]) + 1, 5) hora,
            cua.NOMBRE ubicacion
            FROM webhook_accesos wac
            INNER JOIN cat_personas cp
            ON cp.id_persona = wac.id_persona
            LEFT JOIN detalle_persona_familia dpf ON dpf.id_persona = cp.id_persona
            LEFT JOIN detalle_persona_familia dpft
            ON dpft.id_familia = dpf.id_familia AND dpft.tutor = 1
            LEFT JOIN cat_personas cpt ON cpt.id_persona = dpft.id_persona
            LEFT JOIN CAT_DISPOSITIVOS_ACCESO  cda ON cda.ID_PUNTO_ACCESO_API = wac.UUID_ACCESS_POINT_ACCESOS
            LEFT JOIN CAT_UBICACIONES_ACCESO cua ON cua.ID_UBICACION = cda.ID_UBICACION_ASIGNADA
            WHERE wac.uuid_consecutivo = @uuid";

            return await _conexionSQL.ObtenerAsync<CelularesDto>(query, new { uuid = uuid });
        }

        private async Task<IDGrupoUsuarioDto> ObtenerIdGrupoUsuario(int id_parametro)
        {
            string query = @"
            SELECT 
            VALOR FROM CAT_PARAMETROS WHERE ID_PARAMETRO = @ID_PARAMETRO";

            return await _conexionSQL.ObtenerAsync<IDGrupoUsuarioDto>(query, new { ID_PARAMETRO = id_parametro });
        }

        private async Task<IEnumerable<NotificacionCampoDto>> ObtenerTraduccionMarcadoresAsync(string marcadores)
        {
            string queryNoti = @"
            SELECT DESCRIPCION, CAMPO 
            FROM NOTIFICACIONES_CAMPOS 
            WHERE DESCRIPCION IN (" + marcadores + ")";

            //var parametros = new { descripciones = new[] { marcadores } };

            return await _conexionSQL.ObtenerListaAsync<NotificacionCampoDto>(queryNoti);
        }

        public class NotificacionMensaje
        {
            public int ID_NOTIFICACION_DESTINATARIO { get; set; }
            public string MENSAJE { get; set; }
        }

        public class CelularesDto
        {
            public string nombre_beneficiario { get; set; }
            public string celular_beneficiario { get; set; }
            public string nombre_tutor { get; set; }
            public string celular_tutor { get; set; }
            public string fecha { get; set; }
            public string hora { get; set; }
            public string ubicacion { get; set; }
        }

        public class IDGrupoUsuarioDto
        {
            public string VALOR { get; set; }
        }

        public class NotificacionCampoDto
        {
            public string DESCRIPCION { get; set; }
            public string CAMPO { get; set; }
        }

        public static Dictionary<string, string> ConvertirAStringDictionary<T>(T objeto)
        {
            if (objeto == null)
                return new Dictionary<string, string>();

            return objeto.GetType()
                         .GetProperties()
                         .ToDictionary(
                             prop => prop.Name,
                             prop => prop.GetValue(objeto)?.ToString() ?? ""
                         );
        }

        public static Dictionary<string, string> ConvertirMarcadorDiccionario<T>(T objeto) where T: IEnumerable<NotificacionCampoDto>
        {
            if (objeto == null)
                return new Dictionary<string, string>();

            Dictionary<string, string> diccionarioTraductor = new Dictionary<string, string>();

            foreach (var i in objeto)
            {
                diccionarioTraductor[i.DESCRIPCION] = i.CAMPO;
            }
            return diccionarioTraductor;
        }

        public static string ReemplazarVariablesEnMensaje(string mensaje, Dictionary<string, string> datos, Dictionary<string, string> traduccion)
        {
            foreach (var par in traduccion)
            {
                mensaje = mensaje.Replace(par.Key, datos.GetValueOrDefault(par.Value) );
            }
            return mensaje;
        }

        private async Task ExecuteWithRetry(Func<Task> action, int maxRetries = 3)
        {
            int retries = 0;
            while (true)
            {
                try
                {
                    await action();
                    return;
                }
                catch (SqlException ex) when (ex.Number == 1205) // deadlock
                {
                    retries++;
                    if (retries > maxRetries)
                        throw;

                    Log.Warning("Deadlock detectado, reintentando... intento {retries}", retries);
                    await Task.Delay(500 * retries); // espera exponencial
                }
            }
        }
    }
}