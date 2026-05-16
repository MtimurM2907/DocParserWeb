using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DocParseLab.Server.Data;
using DocParseLab.Server.DTOs;
using DocParseLab.Server.Extensions;
using DocParseLab.Server.Models;

namespace DocParseLab.Server.Controllers;

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

    /// <summary>Первый запуск: в системе ещё нет пользователей.</summary>
    [HttpGet("setup-status")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(SetupStatusResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<SetupStatusResponse>> SetupStatus(CancellationToken cancellationToken)
    {
        var hasUsers = await _db.Users.AnyAsync(cancellationToken);
        return Ok(new SetupStatusResponse { NeedsBootstrap = !hasUsers });
    }

    /// <summary>Создание первого администратора (только если пользователей нет).</summary>
    [HttpPost("bootstrap")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthResponse>> Bootstrap([FromBody] AuthRequest request, CancellationToken cancellationToken)
    {
        if (await _db.Users.AnyAsync(cancellationToken))
        {
            return Conflict(new ErrorResponse
            {
                Message = "Система уже настроена. Войдите под учётной записью администратора.",
            });
        }

        return await CreateUserInternalAsync(request, UserRoles.Admin, null, null, cancellationToken);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] AuthRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return BadRequest("Некорректные данные запроса.");

        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _db.Users
            .Include(u => u.Department)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email, cancellationToken);
        if (user == null || !VerifyPassword(request.Password, user.PasswordHash, user.PasswordSalt))
            return Unauthorized("Неверный email или пароль.");

        _logger.LogInformation("Пользователь {Email} вошёл в систему", email);
        return Ok(await BuildAuthResponseAsync(user, cancellationToken));
    }

    /// <summary>Регистрация нового пользователя — только администратором.</summary>
    [HttpPost("users")]
    [Authorize]
    [ProducesResponseType(typeof(UserBriefResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserBriefResponse>> CreateUser(
        [FromBody] CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.IsAdmin())
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
            {
                Message = "Создавать пользователей может только администратор.",
            });
        }

        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new ErrorResponse { Message = "Email и пароль обязательны." });

        var role = string.IsNullOrWhiteSpace(request.Role) ? UserRoles.Employee : request.Role.Trim();
        if (!UserRoles.All.Contains(role))
            return BadRequest(new ErrorResponse { Message = "Недопустимая роль." });

        if (request.DepartmentId is int depId)
        {
            var depExists = await _db.Departments.AnyAsync(d => d.Id == depId, cancellationToken);
            if (!depExists)
                return BadRequest(new ErrorResponse { Message = "Подразделение не найдено." });
        }

        var email = request.Email.Trim().ToLowerInvariant();
        if (await _db.Users.AnyAsync(u => u.Email.ToLower() == email, cancellationToken))
            return Conflict(new ErrorResponse { Message = "Пользователь с таким email уже существует." });

        var (hash, salt) = HashPassword(request.Password);
        var user = new AppUser
        {
            Email = email,
            PasswordHash = hash,
            PasswordSalt = salt,
            Role = role,
            DepartmentId = request.DepartmentId,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim(),
            CreatedAt = DateTime.UtcNow,
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken);
        await _db.Entry(user).Reference(u => u.Department).LoadAsync(cancellationToken);

        _logger.LogInformation("Администратор создал пользователя {Email} с ролью {Role}", email, role);

        return Ok(new UserBriefResponse
        {
            Id = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Role = user.Role,
            DepartmentId = user.DepartmentId,
            DepartmentName = user.Department?.Name,
        });
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(CurrentUserResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<CurrentUserResponse>> Me(CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var user = await _db.Users.Include(u => u.Department).FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user == null)
            return Unauthorized();

        return Ok(new CurrentUserResponse
        {
            Id = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Role = user.Role,
            DepartmentId = user.DepartmentId,
            DepartmentName = user.Department?.Name,
        });
    }

    private async Task<ActionResult<AuthResponse>> CreateUserInternalAsync(
        AuthRequest request,
        string role,
        int? departmentId,
        string? displayName,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return BadRequest("Некорректные данные запроса.");

        var email = request.Email.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new ErrorResponse { Message = "Email и пароль обязательны." });

        if (request.Password.Length < 6)
            return BadRequest(new ErrorResponse { Message = "Пароль должен быть не короче 6 символов." });

        if (await _db.Users.AnyAsync(u => u.Email.ToLower() == email, cancellationToken))
            return Conflict(new ErrorResponse { Message = "Пользователь с таким email уже существует." });

        var (hash, salt) = HashPassword(request.Password);
        var user = new AppUser
        {
            Email = email,
            PasswordHash = hash,
            PasswordSalt = salt,
            Role = role,
            DepartmentId = departmentId,
            DisplayName = displayName,
            CreatedAt = DateTime.UtcNow,
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Создан пользователь {Email} (роль {Role})", email, role);
        return Ok(await BuildAuthResponseAsync(user, cancellationToken));
    }

    private async Task<AuthResponse> BuildAuthResponseAsync(AppUser user, CancellationToken cancellationToken)
    {
        await _db.Entry(user).Reference(u => u.Department).LoadAsync(cancellationToken);
        var token = GenerateToken(user);
        return new AuthResponse
        {
            UserId = user.Id,
            Email = user.Email,
            Token = token,
            Role = user.Role,
            DepartmentId = user.DepartmentId,
            DepartmentName = user.Department?.Name,
            DisplayName = user.DisplayName,
        };
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

    private static bool VerifyPassword(string password, string storedHash, string storedSalt)
    {
        var saltBytes = Convert.FromBase64String(storedSalt);
        using var deriveBytes = new System.Security.Cryptography.Rfc2898DeriveBytes(
            password, saltBytes, 100_000, System.Security.Cryptography.HashAlgorithmName.SHA256);
        var hashBytes = deriveBytes.GetBytes(32);
        var hash = Convert.ToBase64String(hashBytes);

        return hash == storedHash;
    }

    private string GenerateToken(AppUser user)
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
            new(System.Security.Claims.ClaimTypes.Name, user.Email),
            new(ClaimsPrincipalExtensions.RoleClaimType, user.Role),
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
