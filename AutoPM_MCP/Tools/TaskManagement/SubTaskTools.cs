using AutoPM_MCP.Helpers;
using AutoPM_MCP.Tools.Shared;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace AutoPM_MCP.Tools.TaskManagement;

/// <summary>
/// Sub-task tools for the Agile PM TaskManagement controller.
/// </summary>
/// <remarks>
/// There is deliberately no update tool: the sub-task response omits fields that UpdateSubTask requires
/// (skill, complexity, blocker flag), so a read-modify-write would silently clear them.
/// Stage changes go through move_task_status with a subTaskId.
/// </remarks>
[McpServerToolType]
internal class SubTaskTools(AgilePmClient client)
{
    [McpServerTool(ReadOnly = true)]
    [Description("Lists the sub-tasks of a task.")]
    public async Task<string> ListSubTasks(
        [Description("Parent task ID")] Guid taskId,
        [Description("Page number, starting at 1")] int page = 1,
        [Description("Sub-tasks per page (max 50)")] int pageSize = AgilePmQuery.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var query = AgilePmQuery.Paged(page, pageSize).Add("ParentTaskId", taskId);
        return ToolResponse.List(await client.GetAsync("TaskManagement/getSubTasks", query, cancellationToken));
    }

    [McpServerTool(Destructive = false)]
    [Description("Creates a sub-task under a task.")]
    public async Task<string> CreateSubTask(
        [Description("Parent task ID")] Guid taskId,
        [Description("Sub-task title")] string title,
        [Description("Sub-task description")] string? description = null,
        [Description("User ID to assign the sub-task to")] Guid? assignTo = null,
        [Description("Planned effort in hours")] double plannedHours = 0,
        [Description("Due date (ISO 8601)")] DateTime? endDate = null,
        [Description("Priority")] PriorityEnum priority = PriorityEnum.Medium,
        [Description("Complexity")] PriorityEnum complexity = PriorityEnum.Medium,
        [Description("Initial stage")] GeneralStagesEnum status = GeneralStagesEnum.Planned,
        [Description("Whether this sub-task blocks its parent task")] bool isBlocker = false,
        [Description("Optional skill ID")] Guid? skillId = null,
        CancellationToken cancellationToken = default)
    {
        var command = new CreateSubTaskCommand(
            taskId.ToString(), title, description, assignTo?.ToString(), status, priority, complexity,
            plannedHours, endDate, isBlocker, skillId?.ToString());

        var response = await client.PostAsync("TaskManagement/AddSubTask", command, cancellationToken);
        return ToolResponse.Success($"Sub-task '{title}' created.", response);
    }
}
