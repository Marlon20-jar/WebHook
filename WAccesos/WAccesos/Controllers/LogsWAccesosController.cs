using Microsoft.AspNetCore.Mvc;

namespace WAccesos.Controllers
{
    [ApiController]
    [Route("api/logs")]
    public class LogsWAccesosController : ControllerBase
    {
        [HttpGet]
        public IActionResult GetLogs()
        {
            try
            {
                string today = DateTime.Now.ToString("yyyyMMdd");
                string logFilePath = $"logs/WAccesos_log{today}.txt";
                //string logFilePath = $"logs/app_log20250401.txt";

                if (!System.IO.File.Exists(logFilePath))
                    return NotFound($"No hay logs disponibles para {today}.");

                using (var fs = new FileStream(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new StreamReader(fs))
                {
                    var logs = reader.ReadToEnd(); // Leer todo el archivo
                    return Ok(logs);
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error al leer logs: {ex.Message}");
            }
        }
    }
}
