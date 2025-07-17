using Serilog;
using EasyNetQ;
using WAccesos;
using WAccesos.Models;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Configuración de Serilog con mejoras
Log.Logger = new LoggerConfiguration()
    //.MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning) // Evita logs innecesarios del sistema
    .WriteTo.Console()
    //.WriteTo.File("logs/WAccesos_log.txt", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7, buffered: true) // Mejor uso de disco
    .WriteTo.File(Path.Combine(AppContext.BaseDirectory, "logs", "WAccesos_log.txt"),
    rollingInterval: RollingInterval.Day,
    retainedFileCountLimit: 7,
    buffered: true
    )
    .CreateLogger();

// Reemplaza el sistema de logging por defecto con Serilog
builder.Host.UseSerilog();

// Lazy Initialization de EasyNetQ para evitar crear conexión innecesaria
builder.Services.AddSingleton<IBus>(_ => RabbitHutch.CreateBus(builder.Configuration.GetConnectionString("EasyNetQConfig")));

// Agregar servicios al contenedor
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configuración de Swagger con API Key
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "API de Accesos", Version = "v1" });

    // Definir esquema de autenticación con API Key
    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Description = "Ingrese su API Key en el campo de abajo. Ejemplo: `x-api-key: {valor}`",
        Type = SecuritySchemeType.ApiKey,
        Name = "x-api-key",  // Nombre del encabezado
        In = ParameterLocation.Header,
        Scheme = "ApiKeyScheme"
    });

    // Aplicar la seguridad a todas las rutas
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKey" }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddScoped<ConexionSQL>();
builder.Services.AddScoped<PublisherAccesos>();

var app = builder.Build();

// Middleware de logs de Serilog
app.UseSerilogRequestLogging();

// Habilitar CORS (opcional, si necesitas llamadas externas)
app.UseCors(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());

// Configuración del pipeline HTTP
app.UseRouting();
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

// Swagger solo en desarrollo
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    //Verificar si el IBus esta nulo 
    var bus = app.Services.GetService<IBus>();
    if (bus == null)
    {
        Console.WriteLine("Error: IBus no está registrado en la inyección de dependencias.");
        Log.Error("Error: IBus no está registrado en la inyección de dependencias.");
    }

}

try
{
    Log.Information("La aplicación WAccesos se ha iniciado correctamente.");
    app.Run();
}
finally
{
    Log.CloseAndFlush();
}