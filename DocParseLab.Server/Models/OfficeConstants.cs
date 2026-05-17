namespace DocParseLab.Server.Models;

public static class UserRoles
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string Employee = "Employee";
    public const string Viewer = "Viewer";

    public static readonly string[] All = { Admin, Manager, Employee, Viewer };
}

public static class DocumentWorkflowStatuses
{
    public const string Draft = "Draft";
    public const string OnApproval = "OnApproval";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Archived = "Archived";
    public const string Signed = "Signed";
}

public static class DocumentTypes
{
    public const string General = "general";
    public const string Letter = "letter";
    public const string Memo = "memo";
    public const string Order = "order";
    public const string Contract = "contract";
}

public static class WorkflowActions
{
    public const string Submitted = "Submitted";
    public const string Approved = "Approved";
    public const string StepApproved = "StepApproved";
    public const string Rejected = "Rejected";
    public const string Archived = "Archived";
    public const string ReturnedToDraft = "ReturnedToDraft";
    public const string Signed = "Signed";
    public const string SignatureRevoked = "SignatureRevoked";
}
