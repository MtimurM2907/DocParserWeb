using DocParseLab.Server.Swagger;
using Swashbuckle.AspNetCore.SwaggerGen;
using DocParseLab.Server.Extensions;
using DocParseLab.Server.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Добавление сервисов
builder.Services.AddControllers();
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

// Применение миграций базы данных
await app.MigrateDatabaseAsync();

// Глобальная обработка исключений (должна быть перед UseApplicationPipeline)
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandling();
}

// Настройка конвейера запросов
app.UseApplicationPipeline();

app.Run();
