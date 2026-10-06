using AutoPM_MCP.Helpers;
using AutoPM_MCP.Tools.Shared;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace AutoPM_MCP.Tools.TaskManagement;

/// <summary>
/// User story tools for the Agile PM TaskManagement controller.
/// </summary>
[McpServerToolType]
internal class UserStoryTools(AgilePmClient client, CurrentUser currentUser)
{
    private const string UserStoriesPath = "TaskManagement/getUserStorys";

    [McpServerTool(ReadOnly = true)]
    [Description("Lists user stories, filtered by project, epic and/or assignee. Pass at least one filter.")]
    public async Task<string> ListUserStories(
        [Description("Only stories in this project")] Guid? projectId = null,
        [Description("Only stories in this epic")] Guid? epicId = null,
        [Description("Only stories assigned to the current user")] bool assignedToMe = false,
        [Description("Only stories assigned to this user ID (ignored when assignedToMe is true)")] Guid? assignedToUserId = null,
        [Description("Page number, starting at 1")] int page = 1,
        [Description("Stories per page (max 50)")] int pageSize = AgilePmQuery.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var userId = assignedToMe
            ? (await currentUser.GetIdentityAsync(cancellationToken)).UserId ?? throw new McpException("Could not find a user ID in your access token.")
            : assignedToUserId;

        if (projectId is null && epicId is null && userId is null)
        {
            throw new McpException("Pass projectId, epicId, assignedToMe or assignedToUserId to narrow the list.");
        }

        var query = AgilePmQuery.Paged(page, pageSize)
            .Add("ProjectId", projectId)
            .Add("EpicId", epicId)
            .Add("UserId", userId);

        return ToolResponse.List(await client.GetAsync(UserStoriesPath, query, cancellationToken));
    }

    [McpServerTool(ReadOnly = true)]
    [Description("Gets one user story by ID, including its assignments and tasks.")]
    public async Task<string> GetUserStory(
        [Description("User story ID")] Guid userStoryId,
        CancellationToken cancellationToken = default)
    {
        var response = await client.GetAsync(UserStoriesPath, AgilePmQuery.Create().Add("id", userStoryId), cancellationToken);
        return ToolResponse.Data(response);
    }

    [McpServerTool(Destructive = false)]
    [Description("Creates a user story under an epic.")]
    public async Task<string> CreateUserStory(
        [Description("Epic ID the story belongs to")] Guid epicId,
        [Description("The story, e.g. 'As a <role> I want <goal> so that <benefit>'")] string story,
        [Description("Story description")] string? description = null,
        [Description("Acceptance criteria")] string? acceptanceCriteria = null,
        [Description("Priority")] PriorityEnum priority = PriorityEnum.Medium,
        [Description("Initial stage")] UserStoryStagesEnum status = UserStoryStagesEnum.Planned,
        [Description("User IDs to assign the story to")] Guid[]? assignedTo = null,
        [Description("Optional work item category ID")] Guid? workItemCategoryId = null,
        CancellationToken cancellationToken = default)
    {
        var command = new CreateUserStoryCommand(
            epicId.ToString(), story, description, acceptanceCriteria, priority, status,
            assignedTo?.Select(id => id.ToString()).ToList(), workItemCategoryId?.ToString());

        var response = await client.PostAsync("TaskManagement/AddUserStory", command, cancellationToken);
        return ToolResponse.Success("User story created.", response);
    }

    [McpServerTool(Destructive = false, Idempotent = true)]
    [Description("Updates a user story's content. Only the fields you pass are changed. " +
                 "Use move_user_story_status to change its stage and reassign_user_story to hand it to someone else.")]
    public async Task<string> UpdateUserStory(
        [Description("User story ID")] Guid userStoryId,
        [Description("New story text")] string? story = null,
        [Description("New description")] string? description = null,
        [Description("New acceptance criteria")] string? acceptanceCriteria = null,
        [Description("New priority")] PriorityEnum? priority = null,
        [Description("Move the story to this epic")] Guid? epicId = null,
        [Description("Replace the assignees with these user IDs")] Guid[]? assignedTo = null,
        CancellationToken cancellationToken = default)
    {
        var current = await client.GetByIdAsync<UserStoryRecord>(UserStoriesPath, userStoryId, "User story", cancellationToken);

        var assignees = assignedTo?.Select(id => id.ToString()).ToList()
            ?? current.Assignments?.Select(a => a.ApplicationUserId).OfType<string>().ToList();

        var command = new UpdateUserStoryCommand(
            current.Id,
            story ?? current.Story,
            description ?? current.Description,
            acceptanceCriteria ?? current.AcceptanceCriteria,
            assignees,
            epicId?.ToString() ?? current.EpicId,
            priority ?? current.Priority ?? PriorityEnum.Medium,
            current.WorkItemCategoryId);

        var response = await client.PostAsync("TaskManagement/UpdateUserStory", command, cancellationToken);
        return ToolResponse.Success("User story updated.", response);
    }

    [McpServerTool(Destructive = false, Idempotent = true)]
    [Description("Function depricated, user stories can not be moved. They move once tasks associted to them are moved")]
    public async Task<string> MoveUserStoryStatus(
        [Description("User story ID")] Guid userStoryId,
        [Description("Target stage")] UserStoryStagesEnum status,
        CancellationToken cancellationToken = default)
    {
        var command = new MoveUserStoryStatusCommand(userStoryId.ToString(), status);
        var response = await client.PostAsync("TaskManagement/MoveUserStoryStatus", command, cancellationToken);
        return ToolResponse.Success($"User story moved to {status}.", response);
    }

    [McpServerTool(Destructive = false)]
    [Description("Reassigns a user story from one user to another.")]
    public async Task<string> ReassignUserStory(
        [Description("User story ID")] Guid userStoryId,
        [Description("User ID of the current assignee")] Guid fromUserId,
        [Description("User ID of the new assignee")] Guid toUserId,
        CancellationToken cancellationToken = default)
    {
        var command = new ReAssignUserStoryCommand(userStoryId.ToString(), toUserId.ToString(), fromUserId.ToString());
        var response = await client.PostAsync("TaskManagement/ReAssignUserStory", command, cancellationToken);
        return ToolResponse.Success("User story reassigned.", response);
    }
}
