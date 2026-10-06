using AutoPM_MCP.Helpers;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace AutoPM_MCP.Tools.ProjectSetup;

/// <summary>
/// Project tools for the Agile PM ProjectSetup controller.
/// </summary>
[McpServerToolType]
internal class ProjectTools(AgilePmClient client, CurrentUser currentUser)
{
    private const string ProjectsPath = "ProjectSetup/getProjects";

    [McpServerTool(ReadOnly = true)]
    [Description("Lists the Agile PM projects the current user belongs to (as project manager, scrum master or team member). " +
                 "Start here to find project IDs for the other tools.")]
    public async Task<string> ListMyProjects(
        [Description("Optional text to look for in the project name (case-insensitive)")] string? nameContains = null,
        [Description("Page number, starting at 1")] int page = 1,
        [Description("Projects per page (max 50)")] int pageSize = AgilePmQuery.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        // getProjects returns every project the caller can see, not just the ones they are on, so filter here.
        var identity = await currentUser.GetIdentityAsync(cancellationToken);
        var projects = await client.GetAllPagesAsync(ProjectsPath, AgilePmQuery.Create(), cancellationToken);

        var myProjects = projects
            .Select(project => (Project: project, Role: GetRole(project, identity)))
            .Where(p => p.Role is not null)
            .Where(p => string.IsNullOrWhiteSpace(nameContains)
                        || (p.Project.GetStringOrNull("projectName")?.Contains(nameContains, StringComparison.OrdinalIgnoreCase) ?? false))
            .Select(p => Summarize(p.Project, p.Role!))
            .ToList();

        var (normalizedPage, normalizedSize) = AgilePmQuery.NormalizePaging(page, pageSize);
        return ToolResponse.List(myProjects, normalizedPage, normalizedSize);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Gets the full details of one project, including its team members.")]
    public async Task<string> GetProject(
        [Description("Project ID")] Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var project = await client.GetByIdAsync<JsonElement>(ProjectsPath, projectId, "Project", cancellationToken);
        return ToolResponse.Json(project);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Lists the members of a project with their user IDs and roles. Use it to find user IDs for assigning work.")]
    public async Task<string> ListProjectTeamMembers(
        [Description("Project ID")] Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var project = await client.GetByIdAsync<JsonElement>(ProjectsPath, projectId, "Project", cancellationToken);
        var members = project.EnumerateArrayOrEmpty("projectTeamMembers")
            .Select(member => new
            {
                userId = member.GetStringOrNull("applicationUserId"),
                name = member.GetStringOrNull("userName"),
                email = member.GetStringOrNull("email"),
                role = member.GetStringOrNull("userRole"),
                pendingTasks = member.GetPropertyOrNull("numberOfPendingTasks"),
            })
            .ToList();

        return ToolResponse.Json(new
        {
            projectId,
            projectName = project.GetStringOrNull("projectName"),
            projectManager = new { userId = project.GetStringOrNull("projectManagerId"), name = project.GetStringOrNull("projectManagerName") },
            scrumMaster = new { userId = project.GetStringOrNull("scrumMasterId"), name = project.GetStringOrNull("scrumMasterName") },
            members,
        });
    }

    private static string? GetRole(JsonElement project, UserIdentity identity)
    {
        if (identity.IsUser(project.GetStringOrNull("projectManagerId")))
        {
            return "Project manager";
        }

        if (identity.IsUser(project.GetStringOrNull("scrumMasterId")))
        {
            return "Scrum master";
        }

        var membership = project.EnumerateArrayOrEmpty("projectTeamMembers")
            .FirstOrDefault(m => identity.IsUser(m.GetStringOrNull("applicationUserId")) || identity.IsUser(m.GetStringOrNull("email")));

        return membership.ValueKind == JsonValueKind.Object
            ? membership.GetStringOrNull("userRole") ?? "Team member"
            : null;
    }

    private static object Summarize(JsonElement project, string myRole) => new
    {
        id = project.GetStringOrNull("id"),
        projectName = project.GetStringOrNull("projectName"),
        myRole,
        status = project.GetPropertyOrNull("status"),
        priority = project.GetPropertyOrNull("priority"),
        projectManagerName = project.GetStringOrNull("projectManagerName"),
        scrumMasterName = project.GetStringOrNull("scrumMasterName"),
        startDate = project.GetStringOrNull("startDate"),
        endDate = project.GetStringOrNull("endDate"),
        totalNumberOfTasks = project.GetPropertyOrNull("totalNumberOfTasks"),
    };
}
