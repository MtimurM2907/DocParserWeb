using DocParseLab.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace DocParseLab.Server.Extensions;

/// <summary>
/// Методы расширения для настройки middleware и конвейера запросов
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Применяет миграции базы данных при запуске
    /// </summary>
    public static async Task MigrateDatabaseAsync(this IApplicationBuilder app)
    {
        using var scope = app.ApplicationServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
        try
        {
            await context.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<AppDbContext>>();
            logger.LogError(ex, "Ошибка при применении миграций базы данных");
            throw;
        }
    }

    /// <summary>
    /// Настраивает конвейер запросов для приложения
    /// </summary>
    public static WebApplication UseApplicationPipeline(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseHttpsRedirection();
        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapFallbackToFile("/index.html");

        return app;
    }
}
