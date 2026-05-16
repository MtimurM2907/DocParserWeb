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
[Route("api/[controller]")]
[Authorize]
public sealed class OfficeController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IDocumentAccessService _access;
    private readonly IDocumentWorkflowService _workflow;
    private readonly IDocumentVersionService _versions;
    private readonly IAuditService _audit;
    private readonly IDocumentSignatureService _signatures;

    public OfficeController(
        AppDbContext db,
        IDocumentAccessService access,
        IDocumentWorkflowService workflow,
        IDocumentVersionService versions,
        IAuditService audit,
        IDocumentSignatureService signatures)
    {
        _db = db;
        _access = access;
        _workflow = workflow;
        _versions = versions;
        _audit = audit;
        _signatures = signatures;
    }

    [HttpGet("departments")]
    [ProducesResponseType(typeof(List<DepartmentResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<DepartmentResponse>>> GetDepartments(CancellationToken cancellationToken)
    {
        var list = await _db.Departments.OrderBy(d => d.Name)
            .Select(d => new DepartmentResponse { Id = d.Id, Name = d.Name })
            .ToListAsync(cancellationToken);
        return Ok(list);
    }

    [HttpGet("users")]
    [ProducesResponseType(typeof(List<UserBriefResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<UserBriefResponse>>> GetUsers(CancellationToken cancellationToken)
    {
        var list = await _db.Users
            .Include(u => u.Department)
            .OrderBy(u => u.Email)
            .Select(u => new UserBriefResponse
            {
                Id = u.Id,
                Email = u.Email,
                DisplayName = u.DisplayName,
                Role = u.Role,
                DepartmentId = u.DepartmentId,
                DepartmentName = u.Department != null ? u.Department.Name : null
            })
            .ToListAsync(cancellationToken);
        return Ok(list);
    }

    [HttpGet("registry")]
    [ProducesResponseType(typeof(DocumentRegistryPageResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<DocumentRegistryPageResponse>> GetRegistry(
        [FromQuery] DocumentRegistryQuery query,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var user = await _access.GetUserAsync(userId, cancellationToken);
        if (user == null)
            return Unauthorized();

        var q = _access.AccessibleDocumentsQuery(userId, user);

        if (query.MineOnly)
            q = q.Where(d => d.OwnerId == userId || d.ResponsibleUserId == userId);

        if (!string.IsNullOrWhiteSpace(query.Status))
            q = q.Where(d => d.WorkflowStatus == query.Status);

        if (!string.IsNullOrWhiteSpace(query.DocumentType))
            q = q.Where(d => d.DocumentType == query.DocumentType);

        if (query.DepartmentId is int depId)
            q = q.Where(d => d.DepartmentId == depId);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim().ToLower();
            q = q.Where(d =>
                d.FileName.ToLower().Contains(s)
                || (d.Title != null && d.Title.ToLower().Contains(s))
                || (d.Tags != null && d.Tags.ToLower().Contains(s)));
        }

        var take = Math.Clamp(query.Take, 1, 200);
        var skip = Math.Max(0, query.Skip);

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderByDescending(d => d.UploadedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return Ok(new DocumentRegistryPageResponse
        {
            Total = total,
            Items = items.Select(OfficeDtoMapper.ToRegistryItem).ToList()
        });
    }

    [HttpGet("my-tasks")]
    [ProducesResponseType(typeof(List<ApprovalTaskResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ApprovalTaskResponse>>> GetMyTasks(CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var tasks = await _db.ParsedDocuments
            .Include(d => d.Owner)
            .Where(d =>
                d.WorkflowStatus == DocumentWorkflowStatuses.OnApproval
                && d.CurrentApproverUserId == userId)
            .OrderByDescending(d => d.SubmittedAt)
            .Select(d => new ApprovalTaskResponse
            {
                DocumentId = d.Id,
                Title = string.IsNullOrWhiteSpace(d.Title) ? d.FileName : d.Title!,
                FileName = d.FileName,
                WorkflowStatus = d.WorkflowStatus,
                SubmittedAt = d.SubmittedAt,
                OwnerEmail = d.Owner != null ? d.Owner.Email : null,
                WorkflowComment = d.WorkflowComment
            })
            .ToListAsync(cancellationToken);

        return Ok(tasks);
    }

    [HttpPatch("documents/{id:int}/metadata")]
    [ProducesResponseType(typeof(ParsedDocumentResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ParsedDocumentResponse>> UpdateMetadata(
        int id,
        [FromBody] UpdateDocumentMetadataRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var doc = await LoadDocumentAsync(id, cancellationToken);
        if (doc == null)
            return NotFound();

        if (!await _access.CanEditMetadataAsync(userId, doc, cancellationToken))
            return Forbid();

        if (request.Title != null)
            doc.Title = string.IsNullOrWhiteSpace(request.Title) ? null : request.Title.Trim();
        if (request.DocumentType != null)
            doc.DocumentType = request.DocumentType.Trim();
        if (request.DepartmentId.HasValue)
            doc.DepartmentId = request.DepartmentId.Value <= 0 ? null : request.DepartmentId;
        if (request.ResponsibleUserId.HasValue)
            doc.ResponsibleUserId = request.ResponsibleUserId.Value <= 0 ? null : request.ResponsibleUserId;
        if (request.Tags != null)
            doc.Tags = string.IsNullOrWhiteSpace(request.Tags) ? null : request.Tags.Trim();
        if (request.DataClassification != null)
            doc.DataClassification = request.DataClassification.Trim();

        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("office.metadata", $"document:{id}", null, cancellationToken);

        return Ok(await ToDocumentResponseAsync(doc, cancellationToken));
    }

    [HttpPost("documents/{id:int}/submit")]
    [ProducesResponseType(typeof(ParsedDocumentResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ParsedDocumentResponse>> Submit(
        int id,
        [FromBody] SubmitForApprovalRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();
        if (request == null || request.ApproverUserId <= 0)
            return BadRequest(new ErrorResponse { Message = "Укажите согласующего." });

        var doc = await LoadDocumentAsync(id, cancellationToken);
        if (doc == null)
            return NotFound();

        if (doc.OwnerId != userId && doc.ResponsibleUserId != userId && !User.IsAdmin())
            return Forbid();

        try
        {
            await _workflow.SubmitAsync(doc, userId, request.ApproverUserId, request.Comment, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            await _audit.LogAsync("workflow.submit", $"document:{id}", $"approver={request.ApproverUserId}", cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResponse { Message = ex.Message });
        }

        return Ok(await ToDocumentResponseAsync(doc, cancellationToken));
    }

    [HttpPost("documents/{id:int}/approve")]
    [ProducesResponseType(typeof(ParsedDocumentResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ParsedDocumentResponse>> Approve(
        int id,
        [FromBody] WorkflowDecisionRequest? request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var doc = await LoadDocumentAsync(id, cancellationToken);
        if (doc == null)
            return NotFound();
        if (!await _access.CanApproveAsync(userId, doc, cancellationToken))
            return Forbid();

        try
        {
            await _workflow.ApproveAsync(doc, userId, request?.Comment, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            await _audit.LogAsync("workflow.approve", $"document:{id}", null, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResponse { Message = ex.Message });
        }

        return Ok(await ToDocumentResponseAsync(doc, cancellationToken));
    }

    [HttpPost("documents/{id:int}/reject")]
    [ProducesResponseType(typeof(ParsedDocumentResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ParsedDocumentResponse>> Reject(
        int id,
        [FromBody] WorkflowDecisionRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();
        if (request == null || string.IsNullOrWhiteSpace(request.Comment))
            return BadRequest(new ErrorResponse { Message = "Укажите комментарий при возврате." });

        var doc = await LoadDocumentAsync(id, cancellationToken);
        if (doc == null)
            return NotFound();
        if (!await _access.CanApproveAsync(userId, doc, cancellationToken))
            return Forbid();

        try
        {
            await _workflow.RejectAsync(doc, userId, request.Comment, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            await _audit.LogAsync("workflow.reject", $"document:{id}", request.Comment, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResponse { Message = ex.Message });
        }

        return Ok(await ToDocumentResponseAsync(doc, cancellationToken));
    }

    [HttpPost("documents/{id:int}/return-to-draft")]
    [ProducesResponseType(typeof(ParsedDocumentResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ParsedDocumentResponse>> ReturnToDraft(int id, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var doc = await LoadDocumentAsync(id, cancellationToken);
        if (doc == null)
            return NotFound();
        if (doc.OwnerId != userId && doc.ResponsibleUserId != userId && !User.IsAdmin())
            return Forbid();

        try
        {
            await _workflow.ReturnToDraftAsync(doc, userId, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResponse { Message = ex.Message });
        }

        return Ok(await ToDocumentResponseAsync(doc, cancellationToken));
    }

    [HttpPost("documents/{id:int}/archive")]
    [ProducesResponseType(typeof(ParsedDocumentResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ParsedDocumentResponse>> Archive(int id, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var doc = await LoadDocumentAsync(id, cancellationToken);
        if (doc == null)
            return NotFound();
        if (doc.OwnerId != userId && !User.IsManagerOrAdmin())
            return Forbid();

        try
        {
            await _workflow.ArchiveAsync(doc, userId, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResponse { Message = ex.Message });
        }

        return Ok(await ToDocumentResponseAsync(doc, cancellationToken));
    }

    [HttpPost("documents/{id:int}/sign")]
    [ProducesResponseType(typeof(ParsedDocumentResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ParsedDocumentResponse>> Sign(
        int id,
        [FromBody] SignDocumentRequest? request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var doc = await LoadDocumentAsync(id, cancellationToken);
        if (doc == null)
            return NotFound();
        if (!await _access.CanReadAsync(userId, doc, cancellationToken))
            return NotFound();

        var user = await _access.GetUserAsync(userId, cancellationToken);
        if (user == null)
            return Unauthorized();

        try
        {
            await _signatures.SignAsync(doc, user, request?.Comment, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            await _audit.LogAsync(
                "document.sign",
                $"document:{id}",
                $"hash={_signatures.ComputeTextHash(_signatures.GetCanonicalText(doc))}",
                cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResponse { Message = ex.Message });
        }

        return Ok(await ToDocumentResponseAsync(doc, cancellationToken));
    }

    [HttpGet("documents/{id:int}/signatures")]
    [ProducesResponseType(typeof(List<DocumentSignatureResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<DocumentSignatureResponse>>> GetSignatures(
        int id,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var doc = await LoadDocumentAsync(id, cancellationToken);
        if (doc == null || !await _access.CanReadAsync(userId, doc, cancellationToken))
            return NotFound();

        var list = await _signatures.GetSignaturesAsync(id, cancellationToken);
        return Ok(list.Select(ToSignatureResponse).ToList());
    }

    [HttpGet("documents/{id:int}/signatures/verify")]
    [ProducesResponseType(typeof(SignatureVerificationResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<SignatureVerificationResponse>> VerifySignature(
        int id,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var doc = await LoadDocumentAsync(id, cancellationToken);
        if (doc == null || !await _access.CanReadAsync(userId, doc, cancellationToken))
            return NotFound();

        var result = await _signatures.VerifyAsync(doc, cancellationToken);
        return Ok(new SignatureVerificationResponse
        {
            HasSignatures = result.HasSignatures,
            TextMatchesLastSignature = result.TextMatchesLastSignature,
            CurrentTextHashSha256 = result.CurrentTextHashSha256,
            LastSignatureHashSha256 = result.LastSignatureHashSha256,
            LastSignedAt = result.LastSignedAt,
            LastSignerEmail = result.LastSignerEmail,
        });
    }

    [HttpGet("documents/{id:int}/versions")]
    [ProducesResponseType(typeof(List<DocumentVersionResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<DocumentVersionResponse>>> GetVersions(int id, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var doc = await _db.ParsedDocuments.Include(d => d.Shares).FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (doc == null)
            return NotFound();
        if (!await _access.CanReadAsync(userId, doc, cancellationToken))
            return Forbid();

        var versions = await _db.DocumentVersions
            .Include(v => v.CreatedByUser)
            .Where(v => v.DocumentId == id)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new DocumentVersionResponse
            {
                Id = v.Id,
                VersionNumber = v.VersionNumber,
                ChangeType = v.ChangeType,
                CreatedAt = v.CreatedAt,
                CreatedByUserId = v.CreatedByUserId,
                CreatedByEmail = v.CreatedByUser != null ? v.CreatedByUser.Email : null,
                TextLength = v.Text.Length
            })
            .ToListAsync(cancellationToken);

        return Ok(versions);
    }

    [HttpGet("documents/{id:int}/versions/{versionId:int}")]
    [ProducesResponseType(typeof(DocumentVersionDetailResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<DocumentVersionDetailResponse>> GetVersion(
        int id,
        int versionId,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var doc = await _db.ParsedDocuments.Include(d => d.Shares).FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (doc == null)
            return NotFound();
        if (!await _access.CanReadAsync(userId, doc, cancellationToken))
            return Forbid();

        var v = await _db.DocumentVersions
            .Include(x => x.CreatedByUser)
            .FirstOrDefaultAsync(x => x.Id == versionId && x.DocumentId == id, cancellationToken);
        if (v == null)
            return NotFound();

        return Ok(new DocumentVersionDetailResponse
        {
            Id = v.Id,
            VersionNumber = v.VersionNumber,
            ChangeType = v.ChangeType,
            CreatedAt = v.CreatedAt,
            CreatedByUserId = v.CreatedByUserId,
            CreatedByEmail = v.CreatedByUser?.Email,
            TextLength = v.Text.Length,
            Text = v.Text
        });
    }

    [HttpGet("documents/{id:int}/workflow-history")]
    [ProducesResponseType(typeof(List<WorkflowHistoryItemResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<WorkflowHistoryItemResponse>>> GetWorkflowHistory(
        int id,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var doc = await _db.ParsedDocuments.Include(d => d.Shares).FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (doc == null)
            return NotFound();
        if (!await _access.CanReadAsync(userId, doc, cancellationToken))
            return Forbid();

        var history = await _db.DocumentWorkflowHistory
            .Include(h => h.User)
            .Where(h => h.DocumentId == id)
            .OrderByDescending(h => h.CreatedAt)
            .Select(h => new WorkflowHistoryItemResponse
            {
                Id = h.Id,
                Action = h.Action,
                Comment = h.Comment,
                CreatedAt = h.CreatedAt,
                UserEmail = h.User != null ? h.User.Email : null
            })
            .ToListAsync(cancellationToken);

        return Ok(history);
    }

    [HttpPut("users/{userId:int}/role")]
    [ProducesResponseType(typeof(UserBriefResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<UserBriefResponse>> SetUserRole(
        int userId,
        [FromBody] SetUserRoleRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.IsAdmin())
            return Forbid();

        if (request == null || !UserRoles.All.Contains(request.Role))
            return BadRequest(new ErrorResponse { Message = "Недопустимая роль." });

        var target = await _db.Users.Include(u => u.Department).FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (target == null)
            return NotFound();

        target.Role = request.Role;
        target.DepartmentId = request.DepartmentId is > 0 ? request.DepartmentId : null;
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new UserBriefResponse
        {
            Id = target.Id,
            Email = target.Email,
            DisplayName = target.DisplayName,
            Role = target.Role,
            DepartmentId = target.DepartmentId,
            DepartmentName = target.Department?.Name
        });
    }

    [HttpPatch("profile")]
    [ProducesResponseType(typeof(CurrentUserResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<CurrentUserResponse>> UpdateProfile(
        [FromBody] UpdateUserProfileRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var user = await _db.Users.Include(u => u.Department).FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user == null)
            return NotFound();

        if (request.DisplayName != null)
            user.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim();
        if (request.DepartmentId.HasValue)
            user.DepartmentId = request.DepartmentId.Value <= 0 ? null : request.DepartmentId;

        await _db.SaveChangesAsync(cancellationToken);
        return Ok(ToCurrentUser(user));
    }

    private async Task<ParsedDocument?> LoadDocumentAsync(int id, CancellationToken cancellationToken) =>
        await _db.ParsedDocuments
            .Include(d => d.Owner)
            .Include(d => d.Department)
            .Include(d => d.ResponsibleUser)
            .Include(d => d.CurrentApprover)
            .Include(d => d.Shares)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    private async Task<ParsedDocumentResponse> ToDocumentResponseAsync(ParsedDocument doc, CancellationToken cancellationToken)
    {
        await _db.Entry(doc).Collection(d => d.Shares).LoadAsync(cancellationToken);
        User.TryGetUserId(out var userId);
        var verify = await _signatures.VerifyAsync(doc, cancellationToken);
        var flags = await _signatures.GetUiFlagsAsync(userId, doc, cancellationToken);
        return ParsedDocumentResponse.FromEntity(
            doc,
            doc.Owner?.Email,
            doc.Shares.Count,
            userId,
            doc.Department?.Name,
            doc.ResponsibleUser?.Email,
            doc.CurrentApprover?.Email,
            flags.CanSign,
            flags.SignatureCount,
            verify.TextMatchesLastSignature,
            verify.LastSignedAt,
            verify.LastSignerEmail);
    }

    private static DocumentSignatureResponse ToSignatureResponse(DocumentSignature s) => new()
    {
        Id = s.Id,
        DocumentId = s.DocumentId,
        TextHashSha256 = s.TextHashSha256,
        SignedAt = s.SignedAt,
        SignerEmail = s.SignerEmailSnapshot,
        SignerDisplayName = s.SignerDisplayNameSnapshot,
        SignerRole = s.SignerRoleSnapshot,
        Comment = s.Comment,
        SignatureKind = s.SignatureKind,
    };

    private static CurrentUserResponse ToCurrentUser(AppUser user) => new()
    {
        Id = user.Id,
        Email = user.Email,
        DisplayName = user.DisplayName,
        Role = user.Role,
        DepartmentId = user.DepartmentId,
        DepartmentName = user.Department?.Name
    };
}
