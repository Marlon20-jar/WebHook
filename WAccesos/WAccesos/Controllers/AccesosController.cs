using EasyNetQ;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;
using Serilog;
using WAccesos.Models;

namespace WAccesos.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AccesosController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<AccesosController> _logger;
        private readonly ConexionSQL _conexion;
        private readonly PublisherAccesos _publisherAccesos;
        private readonly IBus _bus;

        public AccesosController(ILogger<AccesosController> logger, IConfiguration configuration, ConexionSQL conexion, IBus bus, PublisherAccesos publisherAccesos)
        {
            _logger = logger;
            _configuration = configuration;
            _conexion = conexion;
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _publisherAccesos = publisherAccesos;
        }

        [HttpPost]
        public async Task<ActionResult<Respuesta>> Post(AccesoRequest Acceso)
        {
            var headers = Request.Headers;
            var msg = new Respuesta { Success = true, Mensaje = "Datos Encolados" };

            //VALIDACIONES
            string UUID = Acceso.Uuid;
            bool isEntrance = Acceso.isEntrance;
            string Date = Acceso.date;
            //Recurso
            string uuidRecurso = Acceso.Resource.Id ?? "";
            string NombreRecurso = Acceso.Resource.Name ?? "";
            string idRecurso = Acceso.Resource.ExternalId ?? ""; ;
            string horaInicial = Acceso.Resource.StartHour ?? "";
            string horaFinal = Acceso.Resource.EndHour ?? "";
            //Acces Point
            string uuidacceso = Acceso.AccessPoint.Id ?? "";
            int operacion = Acceso.AccessPoint.Operation;
            int modo = Acceso.AccessPoint.Mode;
            bool active = Acceso.AccessPoint.active;
            string tenantId = Acceso.AccessPoint.tenantId;
            //Credencial
            string uuidcredencial = Acceso.Credential.Id ?? "";
            //Beneficiario
            string uuidPersona = Acceso.Credential.Beneficiary.Id ?? "";
            string NombrePersona = Acceso.Credential.Beneficiary.Name ?? "";
            string idPersona = Acceso.Credential.Beneficiary.ExternalId ?? "";

            try
            {
                // Verificación del API Key
                var token = _configuration["datos:ApiKey"];
                if (!headers.TryGetValue("x-api-key", out StringValues xApiKey) || token != xApiKey)
                {
                    Log.Error("Token no válido o no proporcionado.");
                    return await RegistrarErrorAsync(Acceso, "Token no válido o no proporcionado.");
                }

                // Validación de campos obligatorios
                if (string.IsNullOrWhiteSpace(uuidRecurso) ||
                    string.IsNullOrWhiteSpace(idRecurso) ||
                    string.IsNullOrWhiteSpace(uuidacceso) ||
                    operacion == 0 || modo == 0 ||
                    string.IsNullOrWhiteSpace(uuidcredencial) ||
                    string.IsNullOrWhiteSpace(uuidPersona) ||
                    string.IsNullOrWhiteSpace(idPersona) ||
                    string.IsNullOrWhiteSpace(Date))
                {
                    Log.Error("Faltan campos Obligatorios.");
                    return await RegistrarErrorAsync(Acceso, "Faltan campos obligatorios.");
                }

                bool isBeneficiario = false;

                // Ejecutar validación en la base de datos
                string query = "SELECT ID_DETALLE FROM DETALLE_PERSONA_PROGRAMA WHERE ID_PERSONA = @IdPersona AND ID_PROGRAMA = @IdPrograma";

                var parametros = new
                {
                    IdPersona = int.Parse(idPersona),
                    IdPrograma = int.Parse(idRecurso)
                };

                var resultado = await _conexion.ObtenerValorAsync<int?>(query, parametros);

                if (resultado.HasValue)
                {
                    isBeneficiario = true;
                }

                // Encolar mensaje en RabbitMQ
                var message = new AccesoRequest();
                message.Uuid = UUID;
                message.isEntrance = isEntrance;
                message.Resource.Id = uuidRecurso;
                message.Resource.Name = NombreRecurso;
                message.Resource.ExternalId = idRecurso;
                message.Resource.StartHour = horaInicial;
                message.Resource.EndHour = horaFinal;
                message.AccessPoint.Id = uuidacceso;
                message.AccessPoint.Operation = operacion;
                message.AccessPoint.Mode = modo;
                message.AccessPoint.active = active;
                message.Credential.Id = uuidcredencial;
                message.Credential.Beneficiary.Id = uuidPersona;
                message.Credential.Beneficiary.Name = NombrePersona.ToUpper();
                message.Credential.Beneficiary.ExternalId = idPersona;
                message.AccessPoint.tenantId = tenantId;
                message.isBeneficiario = isBeneficiario;
                message.date = Date;
                //await _publisherAccesos.MsgPublisherAsync(message);
                await _publisherAccesos.PublicarEnLotesAsync(new List<AccesoRequest> { message });

                Log.Information("Se pasaron al reciveAccesos");

                msg.hash = Acceso.Uuid;
                return Ok(msg);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error en el servidor WAccesos");
                return await RegistrarErrorAsync(Acceso, ex.Message);
            }
        }

        private async Task<ActionResult<Respuesta>> RegistrarErrorAsync(AccesoRequest acceso, string mensaje)
        {
            var msg = new Respuesta { Success = false, Mensaje = mensaje, hash = acceso.Uuid };

            await _conexion.EjecutarAsync(
            "INSERT INTO BITACORA_ACCESOS (DATOS, MENSAJE, ESTATUS, IS_ENTRANCE, UUID_PROGRAMA_ACCESOS, NOMBRE_PROGRAMA, ID_PROGRAMA, HORA_INICIO, HORA_FINALIZACION, UUID_ACCESS_POINT_ACCESOS, OPERATION, MODE, ACTIVE, UUID_CREDENCIAL, UUID_PERSONA, NOMBRE_PERSONA, ID_PERSONA, TENANT_ID, FECHA_REG) " +
            "VALUES (@Uuid, @Mensaje, 'RESPUESTA_ERRONEA', @IS_ENTRANCE, @UUID_PROGRAMA_ACCESOS, @NOMBRE_PROGRAMA, @ID_PROGRAMA, @HORA_INICIO, @HORA_FINALIZACION, @UUID_ACCESS_POINT_ACCESOS, @OPERATION, @MODE, @ACTIVE, @UUID_CREDENCIAL, @UUID_PERSONA, @NOMBRE_PERSONA, @ID_PERSONA, @TENANT_ID, GETDATE())",
            new
            {
                Uuid = acceso.Uuid,
                Mensaje = mensaje,
                IS_ENTRANCE = acceso.isEntrance,
                UUID_PROGRAMA_ACCESOS = acceso.Resource.Id,
                NOMBRE_PROGRAMA = acceso.Resource.Name,
                ID_PROGRAMA = int.Parse(acceso.Resource.ExternalId),
                HORA_INICIO = acceso.Resource.StartHour,
                HORA_FINALIZACION = acceso.Resource.EndHour,
                UUID_ACCESS_POINT_ACCESOS = acceso.AccessPoint.Id,
                OPERATION = acceso.AccessPoint.Operation,
                MODE = acceso.AccessPoint.Mode,
                ACTIVE = acceso.AccessPoint.active,
                UUID_CREDENCIAL = acceso.Credential.Id,
                UUID_PERSONA = acceso.Credential.Beneficiary.Id,
                NOMBRE_PERSONA = acceso.Credential.Beneficiary.Name.ToUpper(),
                ID_PERSONA = int.Parse(acceso.Credential.Beneficiary.ExternalId),
                TENANT_ID = acceso.AccessPoint.tenantId,
            });

            return Ok(msg);
        }
    }
}
