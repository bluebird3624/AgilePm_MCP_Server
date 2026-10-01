using AutoPM_MCP.Helpers;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace AutoPM_MCP.Tools.TaskManagement;

/// <summary>
/// Epic tools for the Agile PM TaskManagement controller.
/// </summary>
[McpServerToolType]
internal class EpicTools(AgilePmClient client)
{
    private const string EpicsPath = "TaskManagement/getEpics";

    // TODO: replace with named enums once the EpicStatus / EpicPriority definitions are confirmed.
    private const string StatusDescription = "Epic status code (0-3, as defined by Agile PM's EpicStatus)";
    private const string PriorityDescription = "Epic priority code (0-3, as defined by Agile PM's EpicPriority)";

    [McpServerTool(ReadOnly = true)]
    [Description("Lists the epics in a project.")]
    public async Task<string> ListEpics(
        [Description("Project ID")] Guid projectId,
        [Description("Page number, starting at 1")] int page = 1,
        [Description("Epics per page (max 50)")] int pageSize = AgilePmQuery.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var query = AgilePmQuery.Paged(page, pageSize).Add("ProjectId", projectId);
        return ToolResponse.List(await client.GetAsync(EpicsPath, query, cancellationToken));
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Gets one epic by ID.")]
    public async Task<string> GetEpic(
        [Description("Epic ID")] Guid epicId,
        CancellationToken cancellationToken = default)
    {
        var response = await client.GetAsync(EpicsPath, AgilePmQuery.Create().Add("id", epicId), cancellationToken);
        return ToolResponse.Data(response);
    }

    [McpServerTool(Destructive = false)]
    [Description("Creates an epic in a project.")]
    public async Task<string> CreateEpic(
        [Description("Project ID")] Guid projectId,
        [Description("Epic title")] string title,
        [Description("Epic description")] string? description = null,
        [Description("Estimated effort (hours)")] double estimatedEffort = 0,
        [Description(StatusDescription)] int status = 0,
        [Description(PriorityDescription)] int priority = 0,
        [Description("Start date (ISO 8601)")] DateTime? startDate = null,
        [Description("End date (ISO 8601)")] DateTime? endDate = null,
        [Description("Optional phase ID")] Guid? phaseId = null,
        [Description("Optional milestone ID")] Guid? milestoneId = null,
        CancellationToken cancellationToken = default)
    {
        EnsureEpicCode(status, nameof(status));
        EnsureEpicCode(priority, nameof(priority));

        var command = new CreateEpicCommand(
            projectId.ToString(), title, description, estimatedEffort, status, priority,
            startDate, endDate, phaseId?.ToString(), milestoneId?.ToString());

        var response = await client.PostAsync("TaskManagement/AddEpic", command, cancellationToken);
        return ToolResponse.Success($"Epic '{title}' created.", response);
    }

    [McpServerTool(Destructive = false, Idempotent = true)]
    [Description("Updates an epic. Only the fields you pass are changed; everything else is kept as it is.")]
    public async Task<string> UpdateEpic(
        [Description("Epic ID")] Guid epicId,
        [Description("ID of the project the epic belongs to")] Guid projectId,
        [Description("New title")] string? title = null,
        [Description("New description")] string? description = null,
        [Description("New estimated effort (hours)")] double? estimatedEffort = null,
        [Description(StatusDescription)] int? status = null,
        [Description(PriorityDescription)] int? priority = null,
        [Description("New start date (ISO 8601)")] DateTime? startDate = null,
        [Description("New end date (ISO 8601)")] DateTime? endDate = null,
        CancellationToken cancellationToken = default)
    {
        if (status is { } s) EnsureEpicCode(s, nameof(status));
        if (priority is { } p) EnsureEpicCode(p, nameof(priority));

        var current = await client.GetByIdAsync<EpicRecord>(EpicsPath, epicId, "Epic", cancellationToken);

        // The epic response has no projectId, so the caller supplies it to avoid the update clearing it.
        var command = new UpdateEpicCommand(
            current.Id,
            current.MilestoneId,
            current.MilestoneGroupId,
            projectId.ToString(),
            current.ProjectContractId,
            current.PhaseId,
            title ?? current.Title,
            description ?? current.Description,
            estimatedEffort ?? current.EstimatedEffort ?? 0,
            status ?? current.Status ?? 0,
            priority ?? current.Priority ?? 0,
            startDate ?? current.StartDate,
            endDate ?? current.EndDate);

        var response = await client.PostAsync("TaskManagement/UpdateEpic", command, cancellationToken);
        return ToolResponse.Success($"Epic '{command.Title}' updated.", response);
    }

    private static void EnsureEpicCode(int value, string name)
    {
        if (value is < 0 or > 3)
        {
            throw new McpException($"{name} must be between 0 and 3.");
        }
    }
}
