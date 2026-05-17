using DocParseLab.Server.Models;
using DocParseLab.Server.Services;
using Microsoft.EntityFrameworkCore;

namespace DocParseLab.Server.Data;

/// <summary>Очистка БД и загрузка демо-подразделений и учётных записей.</summary>
public static class DemoDataSeeder
{
    public static async Task ClearAndSeedAsync(AppDbContext context, CancellationToken cancellationToken = default)
    {
        await context.DocumentAccessLogs.ExecuteDeleteAsync(cancellationToken);
        await context.DocumentComments.ExecuteDeleteAsync(cancellationToken);
        await context.UserNotifications.ExecuteDeleteAsync(cancellationToken);
        await context.DocumentApprovalSteps.ExecuteDeleteAsync(cancellationToken);
        await context.DocumentSignatures.ExecuteDeleteAsync(cancellationToken);
        await context.DocumentWorkflowHistory.ExecuteDeleteAsync(cancellationToken);
        await context.DocumentVersions.ExecuteDeleteAsync(cancellationToken);
        await context.DocumentShares.ExecuteDeleteAsync(cancellationToken);
        await context.AuditLogEntries.ExecuteDeleteAsync(cancellationToken);
        await context.ParsedDocuments.ExecuteDeleteAsync(cancellationToken);
        await context.Users.ExecuteDeleteAsync(cancellationToken);
        await context.Departments.ExecuteDeleteAsync(cancellationToken);

        var departments = new[]
        {
            new Department { Name = "Администрация" },
            new Department { Name = "Бухгалтерия" },
            new Department { Name = "Юридический отдел" },
            new Department { Name = "Общий отдел" },
        };
        context.Departments.AddRange(departments);
        await context.SaveChangesAsync(cancellationToken);

        var dept = departments.ToDictionary(d => d.Name, d => d.Id);

        var users = new (string Email, string Password, string DisplayName, string Role, string Department)[]
        {
            ("admin@docparselab.local", "Admin123!", "Администратор Системы", UserRoles.Admin, "Администрация"),
            ("manager@docparselab.local", "Manager123!", "Петров Пётр Петрович", UserRoles.Manager, "Бухгалтерия"),
            ("ivanov@docparselab.local", "Employee123!", "Иванов Иван Иванович", UserRoles.Employee, "Юридический отдел"),
            ("sidorova@docparselab.local", "Employee123!", "Сидорова Мария Сергеевна", UserRoles.Employee, "Общий отдел"),
            ("viewer@docparselab.local", "Viewer123!", "Козлов Алексей Николаевич", UserRoles.Viewer, "Общий отдел"),
        };

        foreach (var (email, password, displayName, role, departmentName) in users)
        {
            var (hash, salt) = PasswordHasher.HashPassword(password);
            context.Users.Add(new AppUser
            {
                Email = email,
                DisplayName = displayName,
                Role = role,
                DepartmentId = dept[departmentName],
                PasswordHash = hash,
                PasswordSalt = salt,
                CreatedAt = DateTime.UtcNow,
            });
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
