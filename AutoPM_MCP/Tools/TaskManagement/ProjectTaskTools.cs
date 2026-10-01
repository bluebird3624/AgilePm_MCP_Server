using AutoPM_MCP.Helpers;
using AutoPM_MCP.Tools.Shared;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace AutoPM_MCP.Tools.TaskManagement;

/// <summary>
/// Project task tools for the Agile PM TaskManagement controller.
/// </summary>
[McpServerToolType]
internal class ProjectTaskTools(AgilePmClient client)
{
    private const string ProjectTasksPath = "TaskManagement/getProjectTasks";

    [McpServerTool(ReadOnly = true)]
    [Description("Lists tasks in a project and/or under a user story. Pass at least one filter. " +
                 "For the current user's own work across projects use get_my_work.")]
    public async Task<string> ListProjectTasks(
        [Description("Only tasks in this project")] Guid? projectId = null,
        [Description("Only tasks under this user story")] Guid? userStoryId = null,
        [Description("Page number, starting at 1")] int page = 1,
        [Description("Tasks per page (max 50)")] int pageSize = AgilePmQuery.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        if (projectId is null && userStoryId is null)
        {
            throw new McpException("Pass projectId or userStoryId to narrow the list.");
        }

        var query = AgilePmQuery.Paged(page, pageSize)
            .Add("ProjectId", projectId)
            .Add("UserStoryId", userStoryId);

        return ToolResponse.List(await client.GetAsync(ProjectTasksPath, query, cancellationToken));
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Gets one task by ID, including its sub-tasks and comments.")]
    public async Task<string> GetProjectTask(
        [Description("Task ID")] Guid taskId,
        CancellationToken cancellationToken = default)
    {
        var response = await client.GetAsync(ProjectTasksPath, AgilePmQuery.Create().Add("id", taskId), cancellationToken);
        return ToolResponse.Data(response);
    }

    [McpServerTool(Destructive = false)]
    [Description("Creates a task in a project, optionally under a user story or epic.")]
    public async Task<string> CreateProjectTask(
        [Description("Project ID")] Guid projectId,
        [Description("Task title")] string title,
        [Description("Task description")] string? description = null,
        [Description("User story the task belongs to")] Guid? userStoryId = null,
        [Description("Epic the task belongs to")] Guid? epicId = null,
        [Description("User ID to assign the task to")] Guid? assignTo = null,
        [Description("Planned effort in hours")] double plannedHours = 0,
        [Description("Due date (ISO 8601)")] DateTime? endDate = null,
        [Description("Priority")] PriorityEnum priority = PriorityEnum.Medium,
        [Description("Complexity")] PriorityEnum complexity = PriorityEnum.Medium,
        [Description("Initial stage")] GeneralStagesEnum status = GeneralStagesEnum.Planned,
        [Description("Optional skill ID")] Guid? skillId = null,
        [Description("Optional work bucket ID")] Guid? workBucketId = null,
        CancellationToken cancellationToken = default)
    {
        var command = new CreateProjectTaskCommand(
            projectId.ToString(), title, description, status, priority, complexity, assignTo?.ToString(),
            plannedHours, endDate, epicId?.ToString(), userStoryId?.ToString(), skillId?.ToString(), workBucketId?.ToString());

        var response = await client.PostAsync("TaskManagement/AddProjectTask", command, cancellationToken);
        return ToolResponse.Success($"Task '{title}' created.", response);
    }

    [McpServerTool(Destructive = false, Idempotent = true)]
    [Description("Updates a task. Only the fields you pass are changed. " +
                 "Use move_task_status to change its stage (it also records hours worked) and reassign_project_task to hand it over.")]
    public async Task<string> UpdateProjectTask(
        [Description("Task ID")] Guid taskId,
        [Description("New title")] string? title = null,
        [Description("New description")] string? description = null,
        [Description("New planned effort in hours")] double? plannedHours = null,
        [Description("New due date (ISO 8601)")] DateTime? endDate = null,
        [Description("New priority")] PriorityEnum? priority = null,
        [Description("New complexity")] PriorityEnum? complexity = null,
        [Description("Move the task under this user story")] Guid? userStoryId = null,
        [Description("Move the task under this epic")] Guid? epicId = null,
        CancellationToken cancellationToken = default)
    {
        var current = await client.GetByIdAsync<ProjectTaskRecord>(ProjectTasksPath, taskId, "Task", cancellationToken);

        var command = new UpdateProjectTaskCommand(
            current.Id,
            title ?? current.Title,
            description ?? current.Description,
            current.Status ?? GeneralStagesEnum.Planned,
            priority ?? current.Priority ?? PriorityEnum.Medium,
            complexity ?? current.Complexity ?? PriorityEnum.Medium,
            current.SkillId,
            current.AssignTo,
            plannedHours ?? current.PlannedHours ?? 0,
            endDate ?? current.EndDate,
            epicId?.ToString() ?? current.EpicId,
            userStoryId?.ToString() ?? current.UserStoryId);

        var response = await client.PostAsync("TaskManagement/UpdateProjectTask", command, cancellationToken);
        return ToolResponse.Success($"Task '{command.Title}' updated.", response);
    }

    [McpServerTool(Destructive = false)]
    [Description("Moves a task (or one of its sub-tasks) to another stage and records the hours worked.Note - a task can not move from planned to stage completed  directly. It must first go through stage in progress")]
    public async Task<string> MoveTaskStatus(
        [Description("Task ID")] Guid taskId,
        [Description("Target stage")] GeneralStagesEnum status,
        [Description("Hours worked to log with this move")] double manHours = 0,
        [Description("Move this sub-task of the task instead of the task itself")] Guid? subTaskId = null,
        CancellationToken cancellationToken = default)
    {
        var command = new MoveTaskStatusCommand(taskId.ToString(), subTaskId?.ToString(), status, manHours);
        var response = await client.PostAsync("TaskManagement/MoveTaskStatus", command, cancellationToken);
        return ToolResponse.Success($"{(subTaskId is null ? "Task" : "Sub-task")} moved to {status}.", response);
    }

    [McpServerTool(Destructive = false)]
    [Description("Reassigns a task to another user.")]
    public async Task<string> ReassignProjectTask(
        [Description("Task ID")] Guid taskId,
        [Description("User ID of the new assignee")] Guid assignToUserId,
        CancellationToken cancellationToken = default)
    {
        var command = new ReAssignProjectTaskCommand(taskId.ToString(), assignToUserId.ToString());
        var response = await client.PostAsync("TaskManagement/ReAssignProjectTask", command, cancellationToken);
        return ToolResponse.Success("Task reassigned.", response);
    }

    [McpServerTool(Destructive = false)]
    [Description("Pauses the work timer on a task the current user is working on.")]
    public async Task<string> PauseTaskWork(
        [Description("Task ID")] Guid taskId,
        CancellationToken cancellationToken = default)
    {
        var response = await client.PostAsync("TaskManagement/PauseTaskWork", new TaskWorkActionRequest(taskId.ToString()), cancellationToken);
        return ToolResponse.Success("Work on the task paused.", response);
    }

    [McpServerTool(Destructive = false)]
    [Description("Resumes the work timer on a paused task.")]
    public async Task<string> ResumeTaskWork(
        [Description("Task ID")] Guid taskId,
        CancellationToken cancellationToken = default)
    {
        var response = await client.PostAsync("TaskManagement/ResumeTaskWork", new TaskWorkActionRequest(taskId.ToString()), cancellationToken);
        return ToolResponse.Success("Work on the task resumed.", response);
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Gets the current user's Agile PM dashboard summary (task counts and progress).")]
    public async Task<string> GetDashboardSummary(CancellationToken cancellationToken = default)
    {
        return ToolResponse.Data(await client.GetAsync("TaskManagement/getDashboardSummary", cancellationToken: cancellationToken));
    }
}
