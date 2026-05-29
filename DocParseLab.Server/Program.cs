using System.Text;
using DocParseLab.Server.Data;
using DocParseLab.Server.Extensions;
using DocParseLab.Server.Middleware;
using DocParseLab.Server.Services;
using DocParseLab.Server.Swagger;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.SwaggerGen;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var builder = WebApplication.CreateBuilder(args);
var seedDatabase = args.Contains("--seed-database", StringComparer.OrdinalIgnoreCase);

var gigaSecretsLoadedFrom = AddGigaChatSecretsFiles(builder.Configuration, builder.Environment.ContentRootPath);

// Добавление сервисов
builder.Services.AddControllers();
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState.Values
            .SelectMany(v => v.Errors)
            .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? "Некорректное поле." : e.ErrorMessage)
            .Take(5)
            .ToArray();
        return new BadRequestObjectResult(new DocParseLab.Server.DTOs.ErrorResponse
        {
            Message = "Некорректные данные запроса.",
            Details = errors.Length > 0 ? string.Join("; ", errors) : null,
        });
    };
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.OperationFilter<FileUploadOperationFilter>();
});

// Регистрация сервисов приложения
builder.Services.AddDatabase(builder.Configuration);
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddCorsPolicies(builder.Configuration, builder.Environment);
builder.Services.AddApplicationServices(builder.Configuration);

// Добавление логирования
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var app = builder.Build();

var gigaOpts = app.Services.GetRequiredService<IOptions<GigaChatOptions>>().Value;
if (gigaOpts.IsConfigured())
{
    var from = string.IsNullOrEmpty(gigaSecretsLoadedFrom) ? "конфигурации" : gigaSecretsLoadedFrom;
    app.Logger.LogInformation("GigaChat: учётные данные загружены ({Source}), нейросеть доступна.", from);
}
else
{
    app.Logger.LogWarning(
        "GigaChat: не настроен. Скопируйте gigachat.secrets.json.example → gigachat.secrets.json в папке DocParseLab.Server " +
        "и укажите ClientId/ClientSecret с developers.sber.ru (или переменные GigaChat__ClientId / GigaChat__ClientSecret). " +
        "Орфография — через Hunspell; AI-описание — локальное.");
}

static string? AddGigaChatSecretsFiles(IConfigurationBuilder configuration, string contentRootPath)
{
    var candidates = new[]
    {
        Path.Combine(contentRootPath, "gigachat.secrets.json"),
        Path.Combine(AppContext.BaseDirectory, "gigachat.secrets.json"),
    };

    foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
    {
        if (!File.Exists(path))
        {
            continue;
        }

        configuration.AddJsonFile(path, optional: false, reloadOnChange: true);
        return path;
    }

    return null;
}

var autoMigrate = builder.Configuration.GetValue<bool>("Database:AutoMigrateOnStartup", true);
if (autoMigrate)
{
    await app.MigrateDatabaseAsync();
}
else
{
    app.Logger.LogInformation("Автомиграция отключена (Database:AutoMigrateOnStartup=false).");
}

if (seedDatabase)
{
    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DemoDataSeeder.ClearAndSeedAsync(context);
    Console.WriteLine("База данных очищена и заполнена тестовыми данными.");
    return;
}

// Глобальная обработка исключений (должна быть перед UseApplicationPipeline)
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandling();
}

// Настройка конвейера запросов
app.UseApplicationPipeline();

app.Run();
