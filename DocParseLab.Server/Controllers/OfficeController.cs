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

    /// <summary>Коллеги текущего пользователя, которых можно назначить согласующими.</summary>
    [HttpGet("approval-candidates")]
    [ProducesResponseType(typeof(List<UserBriefResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<UserBriefResponse>>> GetApprovalCandidates(CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var submitter = await _db.Users.AsNoTracking()
            .Include(u => u.Department)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (submitter == null)
            return Unauthorized();

        if (submitter.DepartmentId == null)
            return Ok(new List<UserBriefResponse>());

        var list = await _db.Users
            .AsNoTracking()
            .Include(u => u.Department)
            .Where(u =>
                u.Id != submitter.Id
                && u.DepartmentId == submitter.DepartmentId
                && (u.Role == UserRoles.Manager || u.Role == UserRoles.Employee))
            .OrderBy(u => u.DisplayName ?? u.Email)
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
                || (d.Tags != null && d.Tags.ToLower().Contains(s))
                || d.FullText.ToLower().Contains(s)
                || (d.EditedText != null && d.EditedText.ToLower().Contains(s)));
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
            doc.DataClassification = DocumentDataClassifications.Normalize(request.DataClassification);

        await _audit.LogAsync("office.metadata", $"document:{id}", null, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

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

        const int maxAttempts = 2;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var doc = await LoadDocumentAsync(id, cancellationToken);
            if (doc == null)
                return NotFound();

            if (doc.OwnerId != userId && doc.ResponsibleUserId != userId && !User.IsAdmin())
                return Forbid();

            try
            {
                if (doc.WorkflowStatus == DocumentWorkflowStatuses.Rejected)
                {
                    await _workflow.ResubmitAfterRevisionAsync(doc, userId, request?.Comment, cancellationToken);
                    await _audit.LogAsync("workflow.resubmit", $"document:{id}", null, cancellationToken);
                }
                else
                {
                    var approverIds = request?.ApproverUserIds?.Where(x => x > 0).Distinct().ToList()
                        ?? (request?.ApproverUserId > 0 ? new List<int> { request.ApproverUserId } : new List<int>());
                    if (approverIds.Count == 0)
                        return BadRequest(new ErrorResponse { Message = "Укажите хотя бы одного согласующего." });

                    await _workflow.SubmitAsync(doc, userId, approverIds, request?.Comment, request?.ApprovalDueAt, cancellationToken);
                    await _audit.LogAsync("workflow.submit", $"document:{id}", $"approver={request.ApproverUserId}", cancellationToken);
                }

                await _db.SaveChangesAsync(cancellationToken);
                return Ok(await ToDocumentResponseAsync(doc, cancellationToken));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
            catch (DbUpdateConcurrencyException) when (attempt < maxAttempts - 1)
            {
                _db.ChangeTracker.Clear();
            }
        }

        return Conflict(new ErrorResponse { Message = "Документ изменён другим пользователем. Обновите страницу и повторите действие." });
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

        const int maxAttempts = 2;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var doc = await LoadDocumentAsync(id, cancellationToken);
            if (doc == null)
                return NotFound();
            if (!await _access.CanApproveAsync(userId, doc, cancellationToken))
                return Forbid();

            try
            {
                await _workflow.ApproveAsync(doc, userId, request?.Comment, cancellationToken);
                await _audit.LogAsync("workflow.approve", $"document:{id}", null, cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);
                return Ok(await ToDocumentResponseAsync(doc, cancellationToken));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
            catch (DbUpdateConcurrencyException) when (attempt < maxAttempts - 1)
            {
                _db.ChangeTracker.Clear();
            }
        }

        return Conflict(new ErrorResponse { Message = "Документ изменён другим пользователем. Обновите страницу и повторите действие." });
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

        const int maxAttempts = 2;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var doc = await LoadDocumentAsync(id, cancellationToken);
            if (doc == null)
                return NotFound();
            if (!await _access.CanApproveAsync(userId, doc, cancellationToken))
                return Forbid();

            try
            {
                await _workflow.RejectAsync(doc, userId, request.Comment, cancellationToken);
                await _audit.LogAsync("workflow.reject", $"document:{id}", request.Comment, cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);
                return Ok(await ToDocumentResponseAsync(doc, cancellationToken));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new ErrorResponse { Message = ex.Message });
            }
            catch (DbUpdateConcurrencyException) when (attempt < maxAttempts - 1)
            {
                _db.ChangeTracker.Clear();
            }
        }

        return Conflict(new ErrorResponse { Message = "Документ изменён другим пользователем. Обновите страницу и повторите действие." });
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
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new ErrorResponse { Message = "Документ изменён другим пользователем. Обновите страницу и повторите действие." });
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
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new ErrorResponse { Message = "Документ изменён другим пользователем. Обновите страницу и повторите действие." });
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
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new ErrorResponse { Message = "Документ изменён другим пользователем. Обновите страницу и повторите действие." });
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

    [HttpGet("documents/{id:int}/signing-payload")]
    [ProducesResponseType(typeof(DocumentSigningPayloadResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<DocumentSigningPayloadResponse>> GetSigningPayload(
        int id,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
            return Unauthorized();

        var doc = await LoadDocumentAsync(id, cancellationToken);
        if (doc == null || !await _access.CanReadAsync(userId, doc, cancellationToken))
            return NotFound();

        if (doc.WorkflowStatus != DocumentWorkflowStatuses.Approved
            && doc.WorkflowStatus != DocumentWorkflowStatuses.Signed)
        {
            return BadRequest(new ErrorResponse { Message = "УКЭП доступна для согласованных или подписанных документов." });
        }

        if (!await _signatures.CanSignAsync(userId, doc, cancellationToken))
            return Forbid();

        var canonical = _signatures.GetCanonicalText(doc);
        if (string.IsNullOrWhiteSpace(canonical))
            return BadRequest(new ErrorResponse { Message = "В документе нет текста для подписи." });

        var bytes = System.Text.Encoding.UTF8.GetBytes(canonical);
        return Ok(new DocumentSigningPayloadResponse
        {
            TextHashSha256 = _signatures.ComputeTextHash(canonical),
            ContentBase64 = Convert.ToBase64String(bytes),
            ContentByteLength = bytes.Length,
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

    [HttpPut("users/{userId:int}")]
    [ProducesResponseType(typeof(UserBriefResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<UserBriefResponse>> UpdateUser(
        int userId,
        [FromBody] UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.IsAdmin())
            return Forbid();

        if (request == null || !UserRoles.All.Contains(request.Role))
            return BadRequest(new ErrorResponse { Message = "Недопустимая роль." });

        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.DisplayName))
            return BadRequest(new ErrorResponse { Message = "Укажите email и ФИО (логин)." });

        if (request.DepartmentId <= 0)
            return BadRequest(new ErrorResponse { Message = "Выберите подразделение." });

        var depExists = await _db.Departments.AnyAsync(d => d.Id == request.DepartmentId, cancellationToken);
        if (!depExists)
            return BadRequest(new ErrorResponse { Message = "Подразделение не найдено." });

        var target = await _db.Users.Include(u => u.Department).FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (target == null)
            return NotFound();

        var email = request.Email.Trim().ToLowerInvariant();
        if (await _db.Users.AnyAsync(u => u.Id != userId && u.Email.ToLower() == email, cancellationToken))
            return Conflict(new ErrorResponse { Message = "Пользователь с таким email уже существует." });

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            if (request.Password.Length < 6)
                return BadRequest(new ErrorResponse { Message = "Пароль должен быть не короче 6 символов." });
            var (hash, salt) = PasswordHasher.HashPassword(request.Password);
            target.PasswordHash = hash;
            target.PasswordSalt = salt;
        }

        target.Email = email;
        target.DisplayName = request.DisplayName.Trim();
        target.Role = request.Role;
        target.DepartmentId = request.DepartmentId;
        await _db.SaveChangesAsync(cancellationToken);
        await _db.Entry(target).Reference(u => u.Department).LoadAsync(cancellationToken);

        return Ok(new UserBriefResponse
        {
            Id = target.Id,
            Email = target.Email,
            DisplayName = target.DisplayName,
            Role = target.Role,
            DepartmentId = target.DepartmentId,
            DepartmentName = target.Department?.Name,
        });
    }

    [HttpDelete("users/{userId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteUser(int userId, CancellationToken cancellationToken)
    {
        if (!User.IsAdmin())
            return Forbid();

        if (!User.TryGetUserId(out var currentUserId))
            return Unauthorized();

        if (userId == currentUserId)
            return BadRequest(new ErrorResponse { Message = "Нельзя удалить свою учётную запись." });

        var target = await _db.Users.FindAsync(new object[] { userId }, cancellationToken);
        if (target == null)
            return NotFound();

        if (string.Equals(target.Role, UserRoles.Admin, StringComparison.OrdinalIgnoreCase))
        {
            var adminCount = await _db.Users.CountAsync(
                u => u.Role == UserRoles.Admin,
                cancellationToken);
            if (adminCount <= 1)
                return BadRequest(new ErrorResponse { Message = "Нельзя удалить последнего администратора." });
        }

        var shares = await _db.DocumentShares
            .Where(s => s.FromUserId == userId || s.ToUserId == userId)
            .ToListAsync(cancellationToken);
        _db.DocumentShares.RemoveRange(shares);

        var signatures = await _db.DocumentSignatures
            .Where(s => s.SignedByUserId == userId)
            .ToListAsync(cancellationToken);
        _db.DocumentSignatures.RemoveRange(signatures);

        var approvalSteps = await _db.DocumentApprovalSteps
            .Where(s => s.ApproverUserId == userId)
            .ToListAsync(cancellationToken);
        _db.DocumentApprovalSteps.RemoveRange(approvalSteps);

        var comments = await _db.DocumentComments
            .Where(c => c.UserId == userId)
            .ToListAsync(cancellationToken);
        _db.DocumentComments.RemoveRange(comments);

        var ownedDocs = await _db.ParsedDocuments.Where(d => d.OwnerId == userId).ToListAsync(cancellationToken);
        foreach (var doc in ownedDocs)
        {
            doc.OwnerId = null;
            if (doc.ResponsibleUserId == userId)
                doc.ResponsibleUserId = null;
            if (doc.CurrentApproverUserId == userId)
            {
                doc.CurrentApproverUserId = null;
                if (doc.WorkflowStatus == DocumentWorkflowStatuses.OnApproval)
                    doc.WorkflowStatus = DocumentWorkflowStatuses.Draft;
            }
            if (doc.EditLockedByUserId == userId)
            {
                doc.EditLockedByUserId = null;
                doc.EditLockExpiresAt = null;
            }
        }

        var relatedDocs = await _db.ParsedDocuments
            .Where(d =>
                d.OwnerId != userId &&
                (d.ResponsibleUserId == userId ||
                 d.CurrentApproverUserId == userId ||
                 d.EditLockedByUserId == userId))
            .ToListAsync(cancellationToken);
        foreach (var doc in relatedDocs)
        {
            if (doc.ResponsibleUserId == userId)
                doc.ResponsibleUserId = null;
            if (doc.CurrentApproverUserId == userId)
            {
                doc.CurrentApproverUserId = null;
                if (doc.WorkflowStatus == DocumentWorkflowStatuses.OnApproval)
                    doc.WorkflowStatus = DocumentWorkflowStatuses.Draft;
            }
            if (doc.EditLockedByUserId == userId)
            {
                doc.EditLockedByUserId = null;
                doc.EditLockExpiresAt = null;
            }
        }

        _db.Users.Remove(target);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("admin.user.delete", $"user:{userId}", target.Email, cancellationToken);

        return NoContent();
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
        CertificateSubject = s.CertificateSubject,
        CertificateThumbprint = s.CertificateThumbprint,
        ExternalCryptoVerified = s.ExternalCryptoVerified,
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
