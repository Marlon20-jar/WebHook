using EasyNetQ;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Polly.Timeout;
using Serilog;
using System.Net;

namespace reciveAccesos.Models
{
    public class ConsumirApiWhats
    {
        private readonly string _HosWhatsAppp;
        private readonly string _TokenBasicWhats;
        private readonly HttpClient _httpClient;
        private readonly ILogger<ConsumirApiWhats> _logger;

        public ConsumirApiWhats(IConfiguration configuration, HttpClient httpClient, ILogger<ConsumirApiWhats> logger)
        {
            _HosWhatsAppp = configuration.GetConnectionString("HosWhatsAppp");
            _TokenBasicWhats = configuration["datos:TokenBasicWhats"];
            _httpClient = httpClient;
            _logger = logger;
        }

        //public async Task<bool> MandarMensajeWhatsAsync(string mensaje, string celular, string VALOR)
        //{
        //    string url = $"{_HosWhatsAppp}/api/mensajes";

        //    try
        //    {

        //        //var requestDataEnviar = new
        //        //{
        //        //    numero = "5214427731475",
        //        //    mensaje = mensaje,
        //        //    idGrupoUsuario = 3,
        //        //    contacto = new[] { celular }
        //        //};

        //        var requestDataEnviar = new
        //        {
        //            contactos = new[]
        //            {
        //                new {
        //                numero = celular,
        //                data = (object)null,
        //                databag = (object)null
        //            }
        //        },
        //            mensaje = mensaje,
        //            idGrupoUsuario = Convert.ToInt32(VALOR)
        //        };

        //        string jsonEnviarMensaje = JsonConvert.SerializeObject(requestDataEnviar);
        //        var content = new StringContent(jsonEnviarMensaje, System.Text.Encoding.UTF8, "application/json");

        //        _httpClient.DefaultRequestHeaders.Authorization =
        //            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", _TokenBasicWhats);

        //        var response = await _httpClient.PostAsync(url, content);

        //        _logger.LogInformation($"[WhatsApp] Status: {response.StatusCode}");
        //        Log.Information($"[WhatsApp] Status: {response.StatusCode}");

        //        return response.IsSuccessStatusCode; // Retorna true si es 2xx
        //    }
        //    catch (TimeoutRejectedException trex)
        //    {
        //        _logger.LogError(trex, "La solicitud al API de WhatsApp superó el tiempo de espera.");
        //        Log.Error($"La solicitud al API de WhatsApp superó el tiempo de espera: {trex.Message}");
        //        return false;
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "Error al enviar mensaje WhatsApp.");
        //        Log.Error($"Error al enviar mensaje WhatsApp: {ex.Message}");
        //        return false;
        //    }
        //}

        public async Task<ResultadoEnvioWhatsApp> MandarMensajeWhatsAsync(string mensaje, string celular, string VALOR)
        {
            string url = $"{_HosWhatsAppp}/api/mensajes";

            try
            {

                //var requestDataEnviar = new
                //{
                //    numero = "5214427731475",
                //    mensaje = mensaje,
                //    idGrupoUsuario = 3,
                //    contacto = new[] { celular }
                //};

                var requestDataEnviar = new
                {
                    contactos = new[]
                    {
                        new {
                        numero = celular,
                        data = (object)null,
                        databag = (object)null
                    }
                },
                    mensaje = mensaje,
                    idGrupoUsuario = Convert.ToInt32(VALOR)
                };

                string jsonEnviarMensaje = JsonConvert.SerializeObject(requestDataEnviar);
                var content = new StringContent(jsonEnviarMensaje, System.Text.Encoding.UTF8, "application/json");

                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", _TokenBasicWhats);

                var response = await _httpClient.PostAsync(url, content);
                string respuestaJson = await response.Content.ReadAsStringAsync();

                _logger.LogInformation($"[WhatsApp] Status: {response.StatusCode}, Respuesta: {respuestaJson}");
                Log.Information($"[WhatsApp] Status: {response.StatusCode}, Respuesta: {respuestaJson}");

                if (response.IsSuccessStatusCode)
                {
                    var json = JsonConvert.DeserializeObject<JObject>(respuestaJson);
                    return new ResultadoEnvioWhatsApp
                    {
                        Exitoso = true,
                        Mensaje = json?["mensaje"]?.ToString() ?? "Mensaje enviado correctamente",
                        ContenidoRaw = respuestaJson
                    };
                }
                else
                {
                    var json = JsonConvert.DeserializeObject<JObject>(respuestaJson);
                    return new ResultadoEnvioWhatsApp
                    {
                        Exitoso = false,
                        Mensaje = json?["message"]?.ToString() ?? "Error al enviar mensaje",
                        ContenidoRaw = respuestaJson
                    };
                }
            }
            catch (TimeoutRejectedException trex)
            {
                _logger.LogError(trex, "La solicitud al API de WhatsApp superó el tiempo de espera.");
                Log.Error($"La solicitud al API de WhatsApp superó el tiempo de espera: {trex.Message}");
                return new ResultadoEnvioWhatsApp
                {
                    Exitoso = false,
                    Mensaje = "Timeout al conectar con WhatsApp",
                    ContenidoRaw = trex.ToString()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al enviar mensaje WhatsApp.");
                Log.Error(ex, "Error al enviar mensaje WhatsApp.");
                return new ResultadoEnvioWhatsApp
                {
                    Exitoso = false,
                    Mensaje = "Error inesperado al enviar mensaje",
                    ContenidoRaw = ex.ToString()
                };
            }
        }

        public class ResultadoEnvioWhatsApp
        {
            public bool Exitoso { get; set; }
            public string Mensaje { get; set; }
            public string ContenidoRaw { get; set; } // por si quieres guardar el json completo
        }
    }
}