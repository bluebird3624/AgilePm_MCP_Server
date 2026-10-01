using AutoPM_MCP.Helpers;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace AutoPM_MCP.Tools.TaskManagement;

/// <summary>
/// Comment tools for the Agile PM TaskManagement controller. A comment belongs to exactly one task, sub-task or user story.
/// </summary>
[McpServerToolType]
internal class CommentTools(AgilePmClient client)
{
    [McpServerTool(ReadOnly = true)]
    [Description("Lists the comments on a task, sub-task or user story. Pass exactly one of the IDs.")]
    public async Task<string> ListComments(
        [Description("Task ID")] Guid? taskId = null,
        [Description("Sub-task ID")] Guid? subTaskId = null,
        [Description("User story ID")] Guid? userStoryId = null,
        [Description("Page number, starting at 1")] int page = 1,
        [Description("Comments per page (max 50)")] int pageSize = AgilePmQuery.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        EnsureSingleTarget(taskId, subTaskId, userStoryId);

        var query = AgilePmQuery.Paged(page, pageSize)
            .Add("ProjectTaskId", taskId)
            .Add("SubTaskId", subTaskId)
            .Add("UserStoryId", userStoryId);

        return ToolResponse.List(await client.GetAsync("TaskManagement/getComments", query, cancellationToken));
    }

    [McpServerTool(Destructive = false)]
    [Description("Adds a comment to a task, sub-task or user story. Pass exactly one of the IDs.")]
    public async Task<string> AddComment(
        [Description("Comment text")] string remark,
        [Description("Task ID")] Guid? taskId = null,
        [Description("Sub-task ID")] Guid? subTaskId = null,
        [Description("User story ID")] Guid? userStoryId = null,
        [Description("User IDs to tag (notify) in the comment")] Guid[]? taggedUserIds = null,
        CancellationToken cancellationToken = default)
    {
        EnsureSingleTarget(taskId, subTaskId, userStoryId);

        var command = new CreateCommentCommand(
            remark, taskId?.ToString(), subTaskId?.ToString(), userStoryId?.ToString(),
            taggedUserIds?.Select(id => id.ToString()).ToList());

        var response = await client.PostAsync("TaskManagement/AddComment", command, cancellationToken);
        return ToolResponse.Success("Comment added.", response);
    }

    private static void EnsureSingleTarget(Guid? taskId, Guid? subTaskId, Guid? userStoryId)
    {
        var targets = new[] { taskId, subTaskId, userStoryId }.Count(id => id is not null);
        if (targets != 1)
        {
            throw new McpException("Pass exactly one of taskId, subTaskId or userStoryId.");
        }
    }
}
