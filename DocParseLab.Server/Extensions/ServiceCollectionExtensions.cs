using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Polly;
using DocParseLab.Server.Data;
using DocParseLab.Server.Hubs;
using DocParseLab.Server.Services;

namespace DocParseLab.Server.Extensions;

/// <summary>
/// Методы расширения для регистрации сервисов приложения
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует сервисы приложения с настройками для GigaChat
    /// </summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.AddHttpContextAccessor();
        services.AddHttpClient();
        services.Configure<EnterpriseOptions>(configuration.GetSection(EnterpriseOptions.SectionName));
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IWebhookService, WebhookService>();
        services.AddScoped<IChecklistService, ChecklistService>();
        services.AddScoped<IEntityExtractionService, EntityExtractionService>();
        services.AddScoped<IDocumentAccessService, DocumentAccessService>();
        services.AddScoped<IDocumentVersionService, DocumentVersionService>();
        services.AddScoped<IDocumentWorkflowService, DocumentWorkflowService>();
        services.AddScoped<IDocumentSignatureService, DocumentSignatureService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IDocumentAccessLogService, DocumentAccessLogService>();
        services.AddScoped<IDocumentFileStorageService, LocalDocumentFileStorageService>();
        services.AddSingleton<ITextDiffService, TextDiffService>();
        services.Configure<LdapOptions>(configuration.GetSection(LdapOptions.SectionName));
        services.Configure<FileScanOptions>(configuration.GetSection(FileScanOptions.SectionName));
        services.AddScoped<ILdapAuthenticationService, LdapAuthenticationService>();
        services.AddScoped<IFileScanService, FileScanService>();
        services.AddScoped<IDocumentEditLockService, DocumentEditLockService>();
        services.AddScoped<IExternalSignatureService, ExternalSignatureService>();
        services.AddScoped<IDocumentRealtimeService, DocumentRealtimeService>();
        services.AddSignalR();

        services.AddScoped<IPdfParserService, PdfParserService>();
        services.AddScoped<IDocumentTextExtractor, PdfTextExtractor>();
        services.AddScoped<IDocumentTextExtractor, DocxTextExtractor>();
        services.AddScoped<IDocumentExportService, DocumentExportService>();
        services.AddSingleton<RussianHunspellDictionary>();
        services.AddSingleton<HunspellSpellcheckService>();
        services.AddScoped<AiSpellcheckService>();
        services.AddScoped<ISpellcheckService, HunspellSpellcheckService>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddSingleton<TesseractOcrService>();
        services.AddSingleton<IOcrService, CompositeOcrService>();
        services.AddSingleton<IPdfPageRenderer, DocnetPdfPageRenderer>();
        services.Configure<GigaChatOptions>(configuration.GetSection(GigaChatOptions.SectionName));
        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));
        services.Configure<OcrOptions>(configuration.GetSection(OcrOptions.SectionName));
        
        // Регистрация GigaChat клиента с настройками сертификатов
        // Polly retry логика реализована внутри GigaChatClient
        services.AddHttpClient<IGigaChatClient, GigaChatClient>()
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
                var certPath = Path.Combine(AppContext.BaseDirectory, "certs", "russian_trusted_root_ca_pem.crt");
                var handler = new HttpClientHandler();

                if (!File.Exists(certPath))
                {
                    Console.WriteLine($"Предупреждение: сертификат Минцифры не найден по пути: {certPath}");
                    return handler;
                }

                try
                {
                    var rootCert = X509Certificate2.CreateFromPemFile(certPath);

                    handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
                    {
                        if (errors == System.Net.Security.SslPolicyErrors.None)
                            return true;

                        var chain2 = new X509Chain();
                        chain2.ChainPolicy.ExtraStore.Add(rootCert);
                        chain2.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                        chain2.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;

                        if (cert != null && chain2.Build(new X509Certificate2(cert)))
                        {
                            if (chain2.ChainElements.Count > 0)
                            {
                                var root = chain2.ChainElements[chain2.ChainElements.Count - 1].Certificate;
                                if (root.Thumbprint == rootCert.Thumbprint)
                                    return true;
                            }
                        }

                        return false;
                    };
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Не удалось загрузить сертификат Минцифры: {ex.Message}");
                }

                return handler;
            });
        
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddFixedWindowLimiter("api", limiter =>
            {
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.PermitLimit = 180;
                limiter.QueueLimit = 0;
            });
        });

        return services;
    }

    /// <summary>
    /// Регистрирует базу данных
    /// </summary>
    public static IServiceCollection AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));
        
        return services;
    }

    /// <summary>
    /// Регистрирует JWT аутентификацию
    /// </summary>
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var jwtSection = configuration.GetSection("Jwt");
        var jwtKey = jwtSection["Key"] ?? throw new InvalidOperationException("Jwt:Key is not configured");
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtSection["Issuer"],
                    ValidateAudience = true,
                    ValidAudience = jwtSection["Audience"],
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signingKey,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(2)
                };
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        var path = context.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                            context.Token = accessToken;
                        return Task.CompletedTask;
                    }
                };
            });

        return services;
    }

    /// <summary>
    /// Регистрирует CORS политики
    /// </summary>
    public static IServiceCollection AddCorsPolicies(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        services.AddCors(options =>
        {
            if (environment.IsDevelopment())
            {
                options.AddDefaultPolicy(policy =>
                    policy.WithOrigins("https://localhost:53671")
                        .AllowAnyHeader()
                        .AllowAnyMethod());
            }
            else
            {
                var allowedOrigins = configuration.GetSection("AllowedOrigins").Get<string[]>()
                    ?? new[] { "https://yourdomain.com" };

                options.AddDefaultPolicy(policy =>
                    policy.WithOrigins(allowedOrigins)
                        .AllowAnyHeader()
                        .AllowAnyMethod());
            }
        });

        return services;
    }
}
