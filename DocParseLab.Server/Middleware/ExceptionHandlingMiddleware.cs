using System.Net;
using System.Text.Json;
using DocParseLab.Server.DTOs;

namespace DocParseLab.Server.Middleware;

/// <summary>
/// Middleware для глобальной обработки исключений
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var response = context.Response;
        response.ContentType = "application/json";

        var errorResponse = new ErrorResponse
        {
            Message = "Произошла непредвиденная ошибка",
            Details = null,
            Timestamp = DateTime.UtcNow
        };

        switch (exception)
        {
            case ArgumentException argEx:
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                errorResponse.Message = argEx.Message;
                _logger.LogWarning(argEx, "Ошибка аргумента");
                break;

            case UnauthorizedAccessException unauthEx:
                response.StatusCode = (int)HttpStatusCode.Unauthorized;
                errorResponse.Message = unauthEx.Message;
                _logger.LogWarning(unauthEx, "Ошибка авторизации");
                break;

            case KeyNotFoundException notFoundEx:
                response.StatusCode = (int)HttpStatusCode.NotFound;
                errorResponse.Message = notFoundEx.Message;
                _logger.LogWarning(notFoundEx, "Ресурс не найден");
                break;

            case InvalidOperationException invalidOpEx:
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                errorResponse.Message = invalidOpEx.Message;
                _logger.LogWarning(invalidOpEx, "Недопустимая операция");
                break;

            default:
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
                errorResponse.Message = "Внутренняя ошибка сервера";
                _logger.LogError(exception, "Необработанное исключение");
                break;
        }

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize(errorResponse, options);
        await response.WriteAsync(json);
    }
}

/// <summary>
/// Методы расширения для middleware
/// </summary>
public static class ExceptionHandlingMiddlewareExtensions
{
    public static IApplicationBuilder UseExceptionHandling(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ExceptionHandlingMiddleware>();
    }
}
