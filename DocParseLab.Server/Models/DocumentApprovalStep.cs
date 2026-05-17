namespace DocParseLab.Server.Models;

public class DocumentApprovalStep
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public ParsedDocument Document { get; set; } = null!;
    public int StepOrder { get; set; }
    public int ApproverUserId { get; set; }
    public AppUser Approver { get; set; } = null!;
    public string Status { get; set; } = ApprovalStepStatuses.Pending;
    public string? Comment { get; set; }
    public DateTime? ActedAt { get; set; }
}

public static class ApprovalStepStatuses
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}
