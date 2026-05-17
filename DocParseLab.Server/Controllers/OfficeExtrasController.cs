using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DocParseLab.Server.Data;
using DocParseLab.Server.DTOs;
using DocParseLab.Server.Extensions;
using DocParseLab.Server.Models;
using DocParseLab.Server.Services;

namespace DocParseLab.Server.Controllers;

[ApiController]
[Route("api/office")]
[Authorize]
public sealed class OfficeExtrasController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IDocumentAccessService _access;
    private readonly IDocumentWorkflowService _workflow;
    private readonly IDocumentSignatureService _signatures;
    private readonly IDocumentAccessLogService _accessLog;
    private readonly ITextDiffService _diff;
    private readonly IAuditService _audit;
    private readonly IDocumentEditLockService _editLock;
    private readonly IExternalSignatureService _externalSign;

    public OfficeExtrasController(
        AppDbContext db,
        IDocumentAccessService access,
        IDocumentWorkflowService workflow,
        IDocumentSignatureService signatures,
        IDocumentAccessLogService accessLog,
        ITextDiffService diff,
        IAuditService audit,
        IDocumentEditLockService editLock,
        IExternalSignatureService externalSign)
    {
        _db = db;
        _access = access;
        _workflow = workflow;
        _signatures = signatures;
        _accessLog = accessLog;
        _diff = diff;
        _audit = audit;
        _editLock = editLock;
        _externalSign = externalSign;
    }

    [HttpPost("departments")]
    public async Task<ActionResult<DepartmentResponse>> CreateDepartment(
        [FromBody] CreateDepartmentRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.IsAdmin())
            return Forbid();
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            return BadRequest(new ErrorResponse { Message = "Укажите название подразделения." });
        if (await _db.Departments.AnyAsync(d => d.Name == name, cancellationToken))
            return Conflict(new ErrorResponse { Message = "Подразделение уже существует." });

        var dep = new Department { Name = name };
        _db.Departments.Add(dep);
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new DepartmentResponse { Id = dep.Id, Name = dep.Name });
    }

    [HttpPatch("departments/{id:int}")]
    public async Task<ActionResult<DepartmentResponse>> UpdateDepartment(
        int id,
        [FromBody] CreateDepartmentRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.IsAdmin())
            return Forbid();
        var dep = await _db.Departments.FindAsync(new object[] { id }, cancellationToken);
        if (dep == null)
            return NotFound();
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            return BadRequest(new ErrorResponse { Message = "Укажите название." });
        dep.Name = name;
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new DepartmentResponse { Id = dep.Id, Name = dep.Name });
    }

    [HttpDelete("departments/{id:int}")]
    public async Task<IActionResult> DeleteDepartment(int id, CancellationToken cancellationToken)
    {
        if (!User.IsAdmin())
            return Forbid();
        var dep = await _db.Departments.FindAsync(new object[] { id }, cancellationToken);
        if (dep == null)
            return NotFound();
        var inUse = await _db.Users.AnyAsync(u => u.DepartmentId == id, cancellationToken)
            || await _db.ParsedDocuments.AnyAsync(d => d.DepartmentId == id, cancellationToken);
        if (inUse)
            return BadRequest(new ErrorResponse { Message = "Подразделение используется и не может быть удалено." });
        _db.Departments.Remove(dep);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("notifications")]
    public async Task<ActionResult<List<UserNotificationResponse>>> GetNotifications(
        [FromQuery] bool unreadOnly = false,
        CancellationToken cancellationToken = default)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var q = _db.UserNotifications.Where(n => n.UserId == userId);
        if (unreadOnly)
            q = q.Where(n => !n.IsRead);

        var list = await q.OrderByDescending(n => n.CreatedAt).Take(100)
            .Select(n => new UserNotificationResponse
            {
                Id = n.Id,
                Title = n.Title,
                Body = n.Body,
                DocumentId = n.DocumentId,
                IsRead = n.IsRead,
                CreatedAt = n.CreatedAt,
            })
            .ToListAsync(cancellationToken);
        return Ok(list);
    }

    [HttpPost("notifications/{id:int}/read")]
    public async Task<IActionResult> MarkNotificationRead(int id, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();
        var n = await _db.UserNotifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, cancellationToken);
        if (n == null)
            return NotFound();
        n.IsRead = true;
        await _db.SaveChangesAsync(cancellationToken);
        return Ok();
    }

    [HttpPost("notifications/read-all")]
    public async Task<IActionResult> MarkAllNotificationsRead(CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();
        await _db.UserNotifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), cancellationToken);
        return Ok();
    }

    [HttpGet("documents/{id:int}/comments")]
    public async Task<ActionResult<List<DocumentCommentResponse>>> GetComments(int id, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();
        var doc = await _db.ParsedDocuments.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (doc == null)
            return NotFound();
        if (!await _access.CanReadAsync(userId, doc, cancellationToken))
            return Forbid();

        var items = await _db.DocumentComments
            .Include(c => c.User)
            .Where(c => c.DocumentId == id)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new DocumentCommentResponse
            {
                Id = c.Id,
                Text = c.Text,
                CreatedAt = c.CreatedAt,
                UserEmail = c.User.Email,
                UserDisplayName = c.User.DisplayName,
            })
            .ToListAsync(cancellationToken);
        return Ok(items);
    }

    [HttpPost("documents/{id:int}/comments")]
    public async Task<ActionResult<DocumentCommentResponse>> AddComment(
        int id,
        [FromBody] AddDocumentCommentRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();
        var doc = await _db.ParsedDocuments.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (doc == null)
            return NotFound();
        if (!await _access.CanReadAsync(userId, doc, cancellationToken))
            return Forbid();
        var text = request.Text?.Trim();
        if (string.IsNullOrEmpty(text))
            return BadRequest(new ErrorResponse { Message = "Комментарий не может быть пустым." });

        var user = await _access.GetUserAsync(userId, cancellationToken);
        var comment = new DocumentComment
        {
            DocumentId = id,
            UserId = userId,
            Text = text,
            CreatedAt = DateTime.UtcNow,
        };
        _db.DocumentComments.Add(comment);
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new DocumentCommentResponse
        {
            Id = comment.Id,
            Text = comment.Text,
            CreatedAt = comment.CreatedAt,
            UserEmail = user?.Email,
            UserDisplayName = user?.DisplayName,
        });
    }

    [HttpGet("documents/{id:int}/access-log")]
    public async Task<ActionResult<List<DocumentAccessLogResponse>>> GetAccessLog(int id, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();
        var doc = await _db.ParsedDocuments.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (doc == null)
            return NotFound();
        if (!await _access.CanReadAsync(userId, doc, cancellationToken))
            return Forbid();

        var items = await _db.DocumentAccessLogs
            .Include(l => l.User)
            .Where(l => l.DocumentId == id)
            .OrderByDescending(l => l.CreatedAt)
            .Take(200)
            .Select(l => new DocumentAccessLogResponse
            {
                Id = l.Id,
                Action = l.Action,
                CreatedAt = l.CreatedAt,
                UserEmail = l.User != null ? l.User.Email : null,
                IpAddress = l.IpAddress,
            })
            .ToListAsync(cancellationToken);
        return Ok(items);
    }

    [HttpGet("documents/{id:int}/approval-steps")]
    public async Task<ActionResult<List<ApprovalStepResponse>>> GetApprovalSteps(int id, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();
        var doc = await _db.ParsedDocuments.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (doc == null)
            return NotFound();
        if (!await _access.CanReadAsync(userId, doc, cancellationToken))
            return Forbid();

        var steps = await _db.DocumentApprovalSteps
            .Include(s => s.Approver)
            .Where(s => s.DocumentId == id)
            .OrderBy(s => s.StepOrder)
            .Select(s => new ApprovalStepResponse
            {
                StepOrder = s.StepOrder,
                ApproverUserId = s.ApproverUserId,
                ApproverEmail = s.Approver.Email,
                Status = s.Status,
                Comment = s.Comment,
                ActedAt = s.ActedAt,
            })
            .ToListAsync(cancellationToken);
        return Ok(steps);
    }

    [HttpGet("documents/{id:int}/versions/diff")]
    public async Task<ActionResult<VersionDiffResponse>> GetVersionDiff(
        int id,
        [FromQuery] int fromVersionId,
        [FromQuery] int toVersionId,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();
        var doc = await _db.ParsedDocuments.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (doc == null)
            return NotFound();
        if (!await _access.CanReadAsync(userId, doc, cancellationToken))
            return Forbid();

        var fromV = await _db.DocumentVersions.FirstOrDefaultAsync(v => v.Id == fromVersionId && v.DocumentId == id, cancellationToken);
        var toV = await _db.DocumentVersions.FirstOrDefaultAsync(v => v.Id == toVersionId && v.DocumentId == id, cancellationToken);
        if (fromV == null || toV == null)
            return NotFound();

        var lines = _diff.Diff(fromV.Text, toV.Text)
            .Select(l => new TextDiffLineResponse { Kind = l.Kind, Text = l.Text })
            .ToList();
        return Ok(new VersionDiffResponse
        {
            FromVersionId = fromVersionId,
            ToVersionId = toVersionId,
            Lines = lines,
        });
    }

    [HttpPost("documents/bulk-archive")]
    public async Task<ActionResult<BulkArchiveResponse>> BulkArchive(
        [FromBody] BulkArchiveRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();
        var count = await _workflow.BulkArchiveAsync(request.DocumentIds, userId, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new BulkArchiveResponse { ArchivedCount = count });
    }

    [HttpPost("documents/{id:int}/signatures/revoke")]
    public async Task<ActionResult<ParsedDocumentResponse>> RevokeSignature(int id, CancellationToken cancellationToken)
    {
        if (!User.IsAdmin())
            return Forbid();
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var doc = await _db.ParsedDocuments
            .Include(d => d.Owner)
            .Include(d => d.Department)
            .Include(d => d.ResponsibleUser)
            .Include(d => d.CurrentApprover)
            .Include(d => d.Shares)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (doc == null)
            return NotFound();

        try
        {
            await _signatures.RevokeLastAsync(doc, userId, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResponse { Message = ex.Message });
        }

        return Ok(ParsedDocumentResponse.FromEntity(doc, doc.Owner?.Email, doc.Shares.Count, userId));
    }

    [HttpGet("documents/{id:int}/edit-lock")]
    public async Task<ActionResult<DocumentEditLockStatusResponse>> GetEditLock(int id, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();
        var doc = await _db.ParsedDocuments.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (doc == null)
            return NotFound();
        if (!await _access.CanReadAsync(userId, doc, cancellationToken))
            return Forbid();
        var status = await _editLock.GetStatusAsync(doc, userId, cancellationToken);
        if (status.LockedByUserId is int lid)
        {
            var u = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == lid, cancellationToken);
            return Ok(MapLock(status, u?.Email));
        }
        return Ok(MapLock(status, null));
    }

    [HttpPost("documents/{id:int}/edit-lock")]
    public async Task<ActionResult<DocumentEditLockStatusResponse>> AcquireEditLock(int id, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();
        var doc = await _db.ParsedDocuments.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (doc == null)
            return NotFound();
        if (!await _access.CanEditContentAsync(userId, doc, cancellationToken))
            return Forbid();
        try
        {
            var status = await _editLock.AcquireAsync(doc, userId, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            var email = (await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken))?.Email;
            return Ok(MapLock(status, email));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ErrorResponse { Message = ex.Message });
        }
    }

    [HttpDelete("documents/{id:int}/edit-lock")]
    public async Task<IActionResult> ReleaseEditLock(int id, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();
        var doc = await _db.ParsedDocuments.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (doc == null)
            return NotFound();
        await _editLock.ReleaseAsync(doc, userId, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("documents/{id:int}/sign/external")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<ActionResult<ParsedDocumentResponse>> SignExternal(
        int id,
        [FromForm] ExternalSignRequest request,
        IFormFile? signatureFile,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();
        var doc = await _db.ParsedDocuments
            .Include(d => d.Owner)
            .Include(d => d.Department)
            .Include(d => d.ResponsibleUser)
            .Include(d => d.CurrentApprover)
            .Include(d => d.Shares)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (doc == null)
            return NotFound();
        var user = await _access.GetUserAsync(userId, cancellationToken);
        if (user == null)
            return Unauthorized();
        try
        {
            await _externalSign.RegisterExternalAsync(
                doc,
                user,
                signatureFile,
                request.CertificateSubject,
                request.CertificateThumbprint,
                request.Comment,
                cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResponse { Message = ex.Message });
        }
        return Ok(ParsedDocumentResponse.FromEntity(doc, doc.Owner?.Email, doc.Shares.Count, userId));
    }

    private static DocumentEditLockStatusResponse MapLock(DocumentEditLockStatus s, string? email) => new()
    {
        IsLocked = s.IsLocked,
        CanEdit = s.CanEdit,
        LockedByUserId = s.LockedByUserId,
        LockedByEmail = email,
        ExpiresAt = s.ExpiresAt,
    };
}
