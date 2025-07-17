using Serilog;
using reciveAccesos;
using reciveAccesos.Models;
using EasyNetQ;
using Microsoft.Extensions.Hosting;
using Polly;
using Polly.Extensions.Http;
using Polly.Timeout;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// Configuración de Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console() // Para visualizar logs en consola
                       //.WriteTo.File("logs/reciveAccesos_log.txt", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7) // Guarda logs diarios y mantiene 7 días de logs
    .WriteTo.File(Path.Combine(AppContext.BaseDirectory, "logs", "reciveAccesos_log.txt"),
    rollingInterval: RollingInterval.Day,
    retainedFileCountLimit: 7,
    buffered: true
    )
    .CreateLogger();


try
{

    Log.Information("Iniciando la aplicación...");

    // Captura excepciones no manejadas (recomendado en apps de larga ejecución)
    AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
    {
        Log.Fatal(e.ExceptionObject as Exception, "Excepción no manejada.");
        Log.CloseAndFlush();
    };

    TaskScheduler.UnobservedTaskException += (sender, e) =>
    {
        Log.Fatal(e.Exception, "Excepción no observada en una tarea.");
        e.SetObserved();
    };

    // Log de entorno
    if (Environment.UserInteractive)
    {
        Log.Information("La aplicación se está ejecutando en modo interactivo (local/consola).");
    }
    else
    {
        Log.Information("La aplicación se está ejecutando como un servicio de Windows.");
    }

    // Configuración de EasyNetQ
    var connectionString = builder.Configuration.GetConnectionString("EasyNetQConfig");
    var bus = RabbitHutch.CreateBus(connectionString);
    builder.Services.AddSingleton<IAdvancedBus>(bus.Advanced);

    // Reemplaza el sistema de logging por defecto con Serilog
    builder.Host.UseSerilog();

    builder.Services.AddControllers();
    // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    // Registrar dependencias
    builder.Services.AddScoped<ConexionSQL>();
    builder.Services.AddScoped<SubscriberA>();
    builder.Services.AddScoped<ConsumirApiWhats>();

    builder.Services.AddHttpClient<ConsumirApiWhats>()
        .SetHandlerLifetime(TimeSpan.FromMinutes(5)) // Reutiliza el handler por 5 mins (evita socket leaks)
        .AddPolicyHandler(GetRetryPolicy())
        .AddPolicyHandler(Policy.TimeoutAsync<HttpResponseMessage>(10)); // ⏱ Timeout de 10 segundos

    void AddPolicyHandler(AsyncTimeoutPolicy<HttpResponseMessage> asyncTimeoutPolicy)
    {
        throw new NotImplementedException();
    }

    static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)), // 2s, 4s, 8s
                onRetry: (outcome, timespan, attempt, context) =>
                {
                    Log.Warning($"Reintentando... intento #{attempt} luego de {timespan.TotalSeconds} segundos.");
                });
    }

    // Configurar la aplicación como servicio de Windows
    builder.Host.UseWindowsService(); // Esto configura la app para ejecutarse como servicio de Windows

    var app = builder.Build();

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    // Iniciar servicio de suscripción en segundo plano
    using (var scope = app.Services.CreateScope())
    {
        var subscriber = scope.ServiceProvider.GetRequiredService<SubscriberA>();
        Task.Run(() => subscriber.msgSubscriber()); // Inicia el proceso en segundo plano
    }

    app.UseHttpsRedirection();

    app.UseAuthorization();

    app.MapControllers();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "La aplicación terminó de forma inesperada.");
}

finally
{
    Log.CloseAndFlush(); // 💾 Asegura que todos los logs se escriban correctamente
}