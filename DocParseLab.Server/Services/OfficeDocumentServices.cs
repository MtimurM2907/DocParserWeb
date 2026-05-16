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
    Task SubmitAsync(ParsedDocument doc, int authorUserId, int approverUserId, string? comment, CancellationToken cancellationToken = default);
    Task ApproveAsync(ParsedDocument doc, int approverUserId, string? comment, CancellationToken cancellationToken = default);
    Task RejectAsync(ParsedDocument doc, int approverUserId, string comment, CancellationToken cancellationToken = default);
    Task ReturnToDraftAsync(ParsedDocument doc, int userId, CancellationToken cancellationToken = default);
    Task ArchiveAsync(ParsedDocument doc, int userId, CancellationToken cancellationToken = default);
}

public sealed class DocumentWorkflowService : IDocumentWorkflowService
{
    private readonly AppDbContext _db;

    public DocumentWorkflowService(AppDbContext db) => _db = db;

    public async Task SubmitAsync(
        ParsedDocument doc,
        int authorUserId,
        int approverUserId,
        string? comment,
        CancellationToken cancellationToken = default)
    {
        if (doc.WorkflowStatus is not (DocumentWorkflowStatuses.Draft or DocumentWorkflowStatuses.Rejected))
            throw new InvalidOperationException("Отправить на согласование можно только черновик или отклонённый документ.");

        var approver = await _db.Users.FindAsync(new object[] { approverUserId }, cancellationToken);
        if (approver == null)
            throw new InvalidOperationException("Согласующий не найден.");

        doc.WorkflowStatus = DocumentWorkflowStatuses.OnApproval;
        doc.CurrentApproverUserId = approverUserId;
        doc.WorkflowComment = comment;
        doc.SubmittedAt = DateTime.UtcNow;
        doc.WorkflowCompletedAt = null;

        AddHistory(doc.Id, authorUserId, WorkflowActions.Submitted, comment);
    }

    public Task ApproveAsync(ParsedDocument doc, int approverUserId, string? comment, CancellationToken cancellationToken = default)
    {
        if (doc.WorkflowStatus != DocumentWorkflowStatuses.OnApproval)
            throw new InvalidOperationException("Документ не на согласовании.");
        if (doc.CurrentApproverUserId != approverUserId)
            throw new InvalidOperationException("Вы не назначены согласующим по этому документу.");

        doc.WorkflowStatus = DocumentWorkflowStatuses.Approved;
        doc.CurrentApproverUserId = null;
        doc.WorkflowComment = comment;
        doc.WorkflowCompletedAt = DateTime.UtcNow;

        AddHistory(doc.Id, approverUserId, WorkflowActions.Approved, comment);
        return Task.CompletedTask;
    }

    public Task RejectAsync(ParsedDocument doc, int approverUserId, string comment, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(comment))
            throw new InvalidOperationException("Укажите комментарий при возврате на доработку.");

        if (doc.WorkflowStatus != DocumentWorkflowStatuses.OnApproval)
            throw new InvalidOperationException("Документ не на согласовании.");
        if (doc.CurrentApproverUserId != approverUserId)
            throw new InvalidOperationException("Вы не назначены согласующим по этому документу.");

        doc.WorkflowStatus = DocumentWorkflowStatuses.Rejected;
        doc.CurrentApproverUserId = null;
        doc.WorkflowComment = comment.Trim();
        doc.WorkflowCompletedAt = DateTime.UtcNow;

        AddHistory(doc.Id, approverUserId, WorkflowActions.Rejected, comment.Trim());
        return Task.CompletedTask;
    }

    public Task ReturnToDraftAsync(ParsedDocument doc, int userId, CancellationToken cancellationToken = default)
    {
        if (doc.WorkflowStatus == DocumentWorkflowStatuses.Signed)
            throw new InvalidOperationException("Подписанный документ нельзя вернуть в черновик без отмены подписи (обратитесь к администратору).");

        if (doc.WorkflowStatus is not (DocumentWorkflowStatuses.Rejected or DocumentWorkflowStatuses.Approved))
            throw new InvalidOperationException("Вернуть в черновик можно отклонённый или согласованный документ.");

        doc.WorkflowStatus = DocumentWorkflowStatuses.Draft;
        doc.CurrentApproverUserId = null;
        doc.SubmittedAt = null;
        doc.WorkflowCompletedAt = null;

        AddHistory(doc.Id, userId, WorkflowActions.ReturnedToDraft, null);
        return Task.CompletedTask;
    }

    public Task ArchiveAsync(ParsedDocument doc, int userId, CancellationToken cancellationToken = default)
    {
        if (doc.WorkflowStatus != DocumentWorkflowStatuses.Signed)
            throw new InvalidOperationException("В архив можно отправить только подписанный документ.");

        doc.WorkflowStatus = DocumentWorkflowStatuses.Archived;
        AddHistory(doc.Id, userId, WorkflowActions.Archived, null);
        return Task.CompletedTask;
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
