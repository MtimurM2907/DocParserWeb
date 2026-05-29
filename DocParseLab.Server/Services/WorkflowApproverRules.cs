using DocParseLab.Server.Models;

namespace DocParseLab.Server.Services;

/// <summary>Правила выбора согласующих: коллеги того же подразделения, роли Manager/Employee.</summary>
public static class WorkflowApproverRules
{
    public static bool CanBeApprover(AppUser candidate, AppUser submitter)
    {
        if (candidate.Id == submitter.Id)
            return false;

        if (candidate.Role is UserRoles.Admin or UserRoles.Viewer)
            return false;

        if (candidate.Role is not (UserRoles.Manager or UserRoles.Employee))
            return false;

        if (submitter.DepartmentId == null || candidate.DepartmentId != submitter.DepartmentId)
            return false;

        return true;
    }
}
