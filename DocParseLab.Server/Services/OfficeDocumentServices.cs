using Microsoft.EntityFrameworkCore;
using DocParseLab.Server.Data;
using DocParseLab.Server.DTOs;
using DocParseLab.Server.Models;

namespace DocParseLab.Server.Services;

public interface IDocumentAccessService
{
    Task<AppUser?> GetUserAsync(int userId, CancellationToken cancellationToken = default);
    Task<bool> CanReadAsync(int userId, ParsedDocument doc, CancellationToken cancellationToken = default);
    Task<bool> CanEditContentAsync(int userId, ParsedDocument doc, CancellationToken cancellationToken = default);
    Task<bool> CanEditMetadataAsync(int userId, ParsedDocument doc, CancellationToken cancellationToken = default);
    Task<bool> CanApproveAsync(int userId, ParsedDocument doc, CancellationToken cancellationToken = default);
    Task<bool> CanDeleteAsync(int userId, ParsedDocument doc, CancellationToken cancellationToken = default);
    IQueryable<ParsedDocument> AccessibleDocumentsQuery(int userId, AppUser user);
}

public sealed class DocumentAccessService : IDocumentAccessService
{
    private readonly AppDbContext _db;

    public DocumentAccessService(AppDbContext db) => _db = db;

    public Task<AppUser?> GetUserAsync(int userId, CancellationToken cancellationToken = default) =>
        _db.Users.Include(u => u.Department).FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

    public async Task<bool> CanReadAsync(int userId, ParsedDocument doc, CancellationToken cancellationToken = default)
    {
        var user = await GetUserAsync(userId, cancellationToken);
        if (user == null) return false;
        if (user.Role == UserRoles.Admin) return true;
        if (doc.OwnerId == userId) return true;
        if (doc.ResponsibleUserId == userId) return true;
        if (doc.CurrentApproverUserId == userId) return true;
        if (doc.Shares.Any(s => s.ToUserId == userId)) return true;
        if (user.Role == UserRoles.Manager && doc.DepartmentId != null && doc.DepartmentId == user.DepartmentId)
            return true;
        if (user.Role == UserRoles.Viewer && doc.DepartmentId != null && doc.DepartmentId == user.DepartmentId)
            return doc.WorkflowStatus is DocumentWorkflowStatuses.Approved
                or DocumentWorkflowStatuses.Signed
                or DocumentWorkflowStatuses.Archived;
        return false;
    }

    public async Task<bool> CanEditContentAsync(int userId, ParsedDocument doc, CancellationToken cancellationToken = default)
    {
        var user = await GetUserAsync(userId, cancellationToken);
        if (user == null || user.Role == UserRoles.Viewer) return false;
        if (user.Role == UserRoles.Admin)
            return doc.WorkflowStatus is not (DocumentWorkflowStatuses.Archived or DocumentWorkflowStatuses.Signed);
        if (doc.WorkflowStatus is DocumentWorkflowStatuses.OnApproval
            or DocumentWorkflowStatuses.Approved
            or DocumentWorkflowStatuses.Signed
            or DocumentWorkflowStatuses.Archived)
            return false;
        if (doc.OwnerId == userId || doc.ResponsibleUserId == userId) return true;
        if (doc.Shares.Any(s => s.ToUserId == userId)) return true;
        return false;
    }

    public async Task<bool> CanEditMetadataAsync(int userId, ParsedDocument doc, CancellationToken cancellationToken = default)
    {
        if (!await CanEditContentAsync(userId, doc, cancellationToken)) return false;
        var user = await GetUserAsync(userId, cancellationToken);
        return user is { Role: not UserRoles.Viewer };
    }

    public Task<bool> CanApproveAsync(int userId, ParsedDocument doc, CancellationToken cancellationToken = default)
    {
        if (doc.WorkflowStatus != DocumentWorkflowStatuses.OnApproval) return Task.FromResult(false);
        if (doc.CurrentApproverUserId == userId) return Task.FromResult(true);
        return Task.FromResult(false);
    }

    public async Task<bool> CanDeleteAsync(int userId, ParsedDocument doc, CancellationToken cancellationToken = default)
    {
        var user = await GetUserAsync(userId, cancellationToken);
        if (user == null) return false;
        if (user.Role == UserRoles.Admin) return true;
        return doc.OwnerId == userId && doc.WorkflowStatus == DocumentWorkflowStatuses.Draft;
    }

    public IQueryable<ParsedDocument> AccessibleDocumentsQuery(int userId, AppUser user)
    {
        var q = _db.ParsedDocuments
            .Include(d => d.Owner)
            .Include(d => d.Department)
            .Include(d => d.ResponsibleUser)
            .Include(d => d.CurrentApprover)
            .Include(d => d.Shares)
            .AsQueryable();

        if (user.Role == UserRoles.Admin)
            return q;

        if (user.Role == UserRoles.Manager)
        {
            return q.Where(d =>
                d.OwnerId == userId
                || d.ResponsibleUserId == userId
                || d.CurrentApproverUserId == userId
                || d.Shares.Any(s => s.ToUserId == userId)
                || (user.DepartmentId != null && d.DepartmentId == user.DepartmentId));
        }

        if (user.Role == UserRoles.Viewer)
        {
            return q.Where(d =>
                user.DepartmentId != null
                && d.DepartmentId == user.DepartmentId
                && (d.WorkflowStatus == DocumentWorkflowStatuses.Approved
                    || d.WorkflowStatus == DocumentWorkflowStatuses.Archived));
        }

        return q.Where(d =>
            d.OwnerId == userId
            || d.ResponsibleUserId == userId
            || d.CurrentApproverUserId == userId
            || d.Shares.Any(s => s.ToUserId == userId));
    }
}

public interface IDocumentVersionService
{
    Task SaveVersionAsync(ParsedDocument doc, int userId, string text, string changeType, CancellationToken cancellationToken = default);
}

public sealed class DocumentVersionService : IDocumentVersionService
{
    private readonly AppDbContext _db;

    public DocumentVersionService(AppDbContext db) => _db = db;

    public async Task SaveVersionAsync(
        ParsedDocument doc,
        int userId,
        string text,
        string changeType,
        CancellationToken cancellationToken = default)
    {
        var max = await _db.DocumentVersions
            .Where(v => v.DocumentId == doc.Id)
            .Select(v => (int?)v.VersionNumber)
            .MaxAsync(cancellationToken) ?? 0;

        _db.DocumentVersions.Add(new DocumentVersion
        {
            DocumentId = doc.Id,
            VersionNumber = max + 1,
            Text = text,
            ChangeType = changeType,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        });
    }
}

public interface IDocumentWorkflowService
{
    Task SubmitAsync(
        ParsedDocument doc,
        int authorUserId,
        IReadOnlyList<int> approverUserIds,
        string? comment,
        DateTime? approvalDueAt = null,
        CancellationToken cancellationToken = default);
    Task ResubmitAfterRevisionAsync(
        ParsedDocument doc,
        int authorUserId,
        string? comment,
        CancellationToken cancellationToken = default);
    Task ApproveAsync(ParsedDocument doc, int approverUserId, string? comment, CancellationToken cancellationToken = default);
    Task RejectAsync(ParsedDocument doc, int approverUserId, string comment, CancellationToken cancellationToken = default);
    Task ReturnToDraftAsync(ParsedDocument doc, int userId, CancellationToken cancellationToken = default);
    Task ArchiveAsync(ParsedDocument doc, int userId, CancellationToken cancellationToken = default);
    Task<int> BulkArchiveAsync(IReadOnlyList<int> documentIds, int userId, CancellationToken cancellationToken = default);
}

public sealed class DocumentWorkflowService : IDocumentWorkflowService
{
    private readonly AppDbContext _db;
    private readonly INotificationService _notifications;

    public DocumentWorkflowService(AppDbContext db, INotificationService notifications)
    {
        _db = db;
        _notifications = notifications;
    }

    public async Task SubmitAsync(
        ParsedDocument doc,
        int authorUserId,
        IReadOnlyList<int> approverUserIds,
        string? comment,
        DateTime? approvalDueAt = null,
        CancellationToken cancellationToken = default)
    {
        if (doc.WorkflowStatus != DocumentWorkflowStatuses.Draft)
            throw new InvalidOperationException("Назначить согласующих и отправить можно только из черновика.");

        var ids = approverUserIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0)
            throw new InvalidOperationException("Укажите хотя бы одного согласующего.");

        var submitter = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == authorUserId, cancellationToken);
        if (submitter == null)
            throw new InvalidOperationException("Пользователь не найден.");

        if (submitter.DepartmentId == null)
            throw new InvalidOperationException(
                "У вашей учётной записи не указано подразделение. Обратитесь к администратору.");

        foreach (var id in ids)
        {
            var approver = await _db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
            if (approver == null)
                throw new InvalidOperationException($"Согласующий с id={id} не найден.");

            if (!WorkflowApproverRules.CanBeApprover(approver, submitter))
            {
                var name = approver.DisplayName ?? approver.Email;
                throw new InvalidOperationException(
                    $"«{name}» не может быть согласующим. Доступны только сотрудники и руководители вашего подразделения (не вы, не администратор, не наблюдатель).");
            }
        }

        var oldSteps = await _db.DocumentApprovalSteps.Where(s => s.DocumentId == doc.Id).ToListAsync(cancellationToken);
        _db.DocumentApprovalSteps.RemoveRange(oldSteps);

        var order = 1;
        foreach (var approverId in ids)
        {
            _db.DocumentApprovalSteps.Add(new DocumentApprovalStep
            {
                DocumentId = doc.Id,
                StepOrder = order++,
                ApproverUserId = approverId,
                Status = ApprovalStepStatuses.Pending,
            });
        }

        doc.WorkflowStatus = DocumentWorkflowStatuses.OnApproval;
        doc.CurrentApproverUserId = ids[0];
        doc.WorkflowComment = comment;
        doc.SubmittedAt = DateTime.UtcNow;
        doc.WorkflowCompletedAt = null;
        doc.ApprovalDueAt = approvalDueAt;

        AddHistory(doc.Id, authorUserId, WorkflowActions.Submitted, comment);
        await _notifications.NotifyUserAsync(
            ids[0],
            "Документ на согласовании",
            $"Вам направлен документ «{OfficeDtoMapper.DisplayTitle(doc)}» на согласование.",
            doc.Id,
            cancellationToken);
    }

    public async Task ResubmitAfterRevisionAsync(
        ParsedDocument doc,
        int authorUserId,
        string? comment,
        CancellationToken cancellationToken = default)
    {
        if (doc.WorkflowStatus != DocumentWorkflowStatuses.Rejected)
            throw new InvalidOperationException("Повторная отправка доступна только для документа на доработке.");

        var author = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == authorUserId, cancellationToken);
        if (author == null)
            throw new InvalidOperationException("Пользователь не найден.");

        if (author.Role != UserRoles.Admin
            && doc.OwnerId != authorUserId
            && doc.ResponsibleUserId != authorUserId)
            throw new InvalidOperationException("Повторно отправить может владелец, ответственный или администратор.");

        var steps = await _db.DocumentApprovalSteps
            .Where(s => s.DocumentId == doc.Id)
            .OrderBy(s => s.StepOrder)
            .ToListAsync(cancellationToken);

        if (steps.Count == 0)
            throw new InvalidOperationException(
                "Маршрут согласования не найден. Верните документ в черновик и отправьте на согласование заново.");

        foreach (var step in steps)
        {
            step.Status = ApprovalStepStatuses.Pending;
            step.Comment = null;
            step.ActedAt = null;
        }

        doc.WorkflowStatus = DocumentWorkflowStatuses.OnApproval;
        doc.CurrentApproverUserId = steps[0].ApproverUserId;
        doc.WorkflowComment = comment;
        doc.WorkflowCompletedAt = null;
        doc.SubmittedAt = DateTime.UtcNow;

        AddHistory(doc.Id, authorUserId, WorkflowActions.Resubmitted, comment);
        await _notifications.NotifyUserAsync(
            steps[0].ApproverUserId,
            "Документ снова на согласовании",
            $"Документ «{OfficeDtoMapper.DisplayTitle(doc)}» повторно направлен на согласование после доработки.",
            doc.Id,
            cancellationToken);
    }

    public async Task ApproveAsync(ParsedDocument doc, int approverUserId, string? comment, CancellationToken cancellationToken = default)
    {
        if (doc.WorkflowStatus != DocumentWorkflowStatuses.OnApproval)
            throw new InvalidOperationException("Документ не на согласовании.");
        if (doc.CurrentApproverUserId != approverUserId)
            throw new InvalidOperationException("Вы не назначены согласующим по этому документу.");

        var step = await _db.DocumentApprovalSteps
            .FirstOrDefaultAsync(s =>
                s.DocumentId == doc.Id
                && s.ApproverUserId == approverUserId
                && s.Status == ApprovalStepStatuses.Pending, cancellationToken);
        if (step != null)
        {
            step.Status = ApprovalStepStatuses.Approved;
            step.Comment = comment;
            step.ActedAt = DateTime.UtcNow;
        }

        var next = await _db.DocumentApprovalSteps
            .Where(s => s.DocumentId == doc.Id && s.Status == ApprovalStepStatuses.Pending)
            .OrderBy(s => s.StepOrder)
            .FirstOrDefaultAsync(cancellationToken);

        if (next != null)
        {
            doc.CurrentApproverUserId = next.ApproverUserId;
            doc.WorkflowComment = comment;
            AddHistory(doc.Id, approverUserId, WorkflowActions.StepApproved, comment);
            await _notifications.NotifyUserAsync(
                next.ApproverUserId,
                "Следующий этап согласования",
                $"Документ «{OfficeDtoMapper.DisplayTitle(doc)}» ожидает вашего согласования.",
                doc.Id,
                cancellationToken);
            if (doc.OwnerId.HasValue)
            {
                await _notifications.NotifyUserAsync(
                    doc.OwnerId.Value,
                    "Этап согласования пройден",
                    $"Документ «{OfficeDtoMapper.DisplayTitle(doc)}» согласован на промежуточном этапе.",
                    doc.Id,
                    cancellationToken);
            }
            return;
        }

        doc.WorkflowStatus = DocumentWorkflowStatuses.Approved;
        doc.CurrentApproverUserId = null;
        doc.WorkflowComment = comment;
        doc.WorkflowCompletedAt = DateTime.UtcNow;
        AddHistory(doc.Id, approverUserId, WorkflowActions.Approved, comment);
        if (doc.OwnerId.HasValue)
        {
            await _notifications.NotifyUserAsync(
                doc.OwnerId.Value,
                "Документ согласован",
                $"Документ «{OfficeDtoMapper.DisplayTitle(doc)}» полностью согласован. Можно подписать.",
                doc.Id,
                cancellationToken);
        }
    }

    public async Task RejectAsync(ParsedDocument doc, int approverUserId, string comment, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(comment))
            throw new InvalidOperationException("Укажите комментарий при возврате на доработку.");

        if (doc.WorkflowStatus != DocumentWorkflowStatuses.OnApproval)
            throw new InvalidOperationException("Документ не на согласовании.");
        if (doc.CurrentApproverUserId != approverUserId)
            throw new InvalidOperationException("Вы не назначены согласующим по этому документу.");

        var step = await _db.DocumentApprovalSteps
            .FirstOrDefaultAsync(s =>
                s.DocumentId == doc.Id
                && s.ApproverUserId == approverUserId
                && s.Status == ApprovalStepStatuses.Pending, cancellationToken);
        if (step != null)
        {
            step.Status = ApprovalStepStatuses.Rejected;
            step.Comment = comment.Trim();
            step.ActedAt = DateTime.UtcNow;
        }

        doc.WorkflowStatus = DocumentWorkflowStatuses.Rejected;
        doc.CurrentApproverUserId = null;
        doc.WorkflowComment = comment.Trim();
        doc.WorkflowCompletedAt = DateTime.UtcNow;

        AddHistory(doc.Id, approverUserId, WorkflowActions.Rejected, comment.Trim());
        if (doc.OwnerId.HasValue)
        {
            await _notifications.NotifyUserAsync(
                doc.OwnerId.Value,
                "Документ возвращён на доработку",
                $"Документ «{OfficeDtoMapper.DisplayTitle(doc)}»: {comment.Trim()}",
                doc.Id,
                cancellationToken);
        }
    }

    public async Task ReturnToDraftAsync(ParsedDocument doc, int userId, CancellationToken cancellationToken = default)
    {
        if (doc.WorkflowStatus == DocumentWorkflowStatuses.Signed)
            throw new InvalidOperationException("Подписанный документ нельзя вернуть в черновик без отмены подписи.");

        if (doc.WorkflowStatus is not (DocumentWorkflowStatuses.Rejected or DocumentWorkflowStatuses.Approved))
            throw new InvalidOperationException("Вернуть в черновик можно отклонённый или согласованный документ.");

        var steps = await _db.DocumentApprovalSteps.Where(s => s.DocumentId == doc.Id).ToListAsync(cancellationToken);
        _db.DocumentApprovalSteps.RemoveRange(steps);

        doc.WorkflowStatus = DocumentWorkflowStatuses.Draft;
        doc.CurrentApproverUserId = null;
        doc.SubmittedAt = null;
        doc.WorkflowCompletedAt = null;
        doc.ApprovalDueAt = null;

        AddHistory(doc.Id, userId, WorkflowActions.ReturnedToDraft, null);
    }

    public Task ArchiveAsync(ParsedDocument doc, int userId, CancellationToken cancellationToken = default)
    {
        if (doc.WorkflowStatus != DocumentWorkflowStatuses.Signed)
            throw new InvalidOperationException("В архив можно отправить только подписанный документ.");

        doc.WorkflowStatus = DocumentWorkflowStatuses.Archived;
        AddHistory(doc.Id, userId, WorkflowActions.Archived, null);
        return Task.CompletedTask;
    }

    public async Task<int> BulkArchiveAsync(IReadOnlyList<int> documentIds, int userId, CancellationToken cancellationToken = default)
    {
        var count = 0;
        foreach (var id in documentIds.Distinct())
        {
            var doc = await _db.ParsedDocuments.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
            if (doc == null || doc.WorkflowStatus != DocumentWorkflowStatuses.Signed) continue;
            doc.WorkflowStatus = DocumentWorkflowStatuses.Archived;
            AddHistory(doc.Id, userId, WorkflowActions.Archived, "bulk");
            count++;
        }
        return count;
    }

    private void AddHistory(int documentId, int? userId, string action, string? comment)
    {
        _db.DocumentWorkflowHistory.Add(new DocumentWorkflowHistory
        {
            DocumentId = documentId,
            UserId = userId,
            Action = action,
            Comment = comment,
            CreatedAt = DateTime.UtcNow
        });
    }
}

public static class OfficeDtoMapper
{
    public static string DisplayTitle(ParsedDocument d) =>
        string.IsNullOrWhiteSpace(d.Title) ? d.FileName : d.Title!.Trim();

    public static DocumentRegistryItemResponse ToRegistryItem(ParsedDocument d) => new()
    {
        Id = d.Id,
        FileName = d.FileName,
        Title = DisplayTitle(d),
        DocumentType = d.DocumentType,
        WorkflowStatus = d.WorkflowStatus,
        DataClassification = d.DataClassification,
        UploadedAt = d.UploadedAt,
        OwnerId = d.OwnerId,
        OwnerEmail = d.Owner?.Email,
        DepartmentId = d.DepartmentId,
        DepartmentName = d.Department?.Name,
        ResponsibleUserId = d.ResponsibleUserId,
        ResponsibleUserEmail = d.ResponsibleUser?.Email,
        CurrentApproverUserId = d.CurrentApproverUserId,
        CurrentApproverEmail = d.CurrentApprover?.Email,
        Tags = d.Tags,
        AiSummaryPreview = d.AiSummary?.Length > 120 ? d.AiSummary[..120] + "…" : d.AiSummary
    };
}
