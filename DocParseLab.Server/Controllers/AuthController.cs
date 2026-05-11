using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DocParseLab.Server.Data;
using DocParseLab.Server.DTOs;

namespace DocParseLab.Server.Controllers;

/// <summary>
/// Контроллер для управления аутентификацией
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthController> _logger;

    public AuthController(AppDbContext db, IConfiguration configuration, ILogger<AuthController> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Регистрация нового пользователя
    /// </summary>
    /// <param name="request">Данные для регистрации</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>JWT токен и email пользователя</returns>
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] AuthRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest("Некорректные данные запроса.");
        }

        var email = request.Email.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest("Email и пароль обязательны.");
        }

        var exists = await _db.Users.AnyAsync(u => u.Email.ToLower() == email, cancellationToken);
        if (exists)
        {
            return Conflict("Пользователь с таким email уже существует.");
        }

        var (hash, salt) = HashPassword(request.Password);

        var user = new Models.AppUser
        {
            Email = email,
            PasswordHash = hash,
            PasswordSalt = salt,
            CreatedAt = DateTime.UtcNow
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Пользователь {Email} успешно зарегистрирован", email);

        var token = GenerateToken(user);

        return Ok(new AuthResponse
        {
            Email = user.Email,
            Token = token
        });
    }

    /// <summary>
    /// Вход пользователя
    /// </summary>
    /// <param name="request">Данные для входа</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>JWT токен и email пользователя</returns>
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] AuthRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest("Некорректные данные запроса.");
        }

        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email, cancellationToken);
        if (user == null)
        {
            return Unauthorized("Неверный email или пароль.");
        }

        if (!VerifyPassword(request.Password, user.PasswordHash, user.PasswordSalt))
        {
            return Unauthorized("Неверный email или пароль.");
        }

        _logger.LogInformation("Пользователь {Email} успешно вошел в систему", email);

        var token = GenerateToken(user);

        return Ok(new AuthResponse
        {
            Email = user.Email,
            Token = token
        });
    }

    private (string Hash, string Salt) HashPassword(string password)
    {
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        var saltBytes = new byte[16];
        rng.GetBytes(saltBytes);
        var salt = Convert.ToBase64String(saltBytes);

        using var deriveBytes = new System.Security.Cryptography.Rfc2898DeriveBytes(
            password, saltBytes, 100_000, System.Security.Cryptography.HashAlgorithmName.SHA256);
        var hashBytes = deriveBytes.GetBytes(32);
        var hash = Convert.ToBase64String(hashBytes);

        return (hash, salt);
    }

    private bool VerifyPassword(string password, string storedHash, string storedSalt)
    {
        var saltBytes = Convert.FromBase64String(storedSalt);
        using var deriveBytes = new System.Security.Cryptography.Rfc2898DeriveBytes(
            password, saltBytes, 100_000, System.Security.Cryptography.HashAlgorithmName.SHA256);
        var hashBytes = deriveBytes.GetBytes(32);
        var hash = Convert.ToBase64String(hashBytes);

        return hash == storedHash;
    }

    private string GenerateToken(Models.AppUser user)
    {
        var jwtSection = _configuration.GetSection("Jwt");
        var key = jwtSection["Key"] ?? throw new InvalidOperationException("Jwt:Key is not configured");
        var issuer = jwtSection["Issuer"];
        var audience = jwtSection["Audience"];

        var signingKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
            System.Text.Encoding.UTF8.GetBytes(key));
        var credentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(signingKey, 
            Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);

        var claims = new List<System.Security.Claims.Claim>
        {
            new(System.Security.Claims.ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(System.Security.Claims.ClaimTypes.Name, user.Email)
        };

        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer,
            audience,
            claims,
            expires: DateTime.UtcNow.AddHours(12),
            signingCredentials: credentials);

        return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);
    }
}
