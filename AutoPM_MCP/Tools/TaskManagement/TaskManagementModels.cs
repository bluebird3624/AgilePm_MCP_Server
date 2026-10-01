using AutoPM_MCP.Tools.Shared;
using System.Text.Json.Serialization;

namespace AutoPM_MCP.Tools.TaskManagement;

// Request bodies and the subset of response fields needed for read-modify-write updates.
// Agile PM's Update* commands replace the whole record, so every update tool reads the current
// record first and only changes the fields the caller supplied.

// ---- Epics ----

internal sealed record EpicRecord(
    string Id,
    string? MilestoneId,
    string? MilestoneGroupId,
    string? ProjectContractId,
    string? PhaseId,
    string? Title,
    string? Description,
    double? EstimatedEffort,
    int? Status,
    int? Priority,
    DateTime? StartDate,
    DateTime? EndDate);

internal sealed record CreateEpicCommand(
    string ProjectId,
    string Title,
    string? Description,
    double EstimatedEffort,
    int Status,
    int Priority,
    DateTime? StartDate,
    DateTime? EndDate,
    string? PhaseId,
    string? MilestoneId);

internal sealed record UpdateEpicCommand(
    string Id,
    string? MilestoneId,
    string? MilestoneGroupId,
    string ProjectId,
    string? ProjectContractId,
    string? PhaseId,
    string? Title,
    string? Description,
    double EstimatedEffort,
    int Status,
    int Priority,
    DateTime? StartDate,
    DateTime? EndDate);

// ---- User stories ----

internal sealed record UserStoryRecord(
    string Id,
    string? Story,
    string? Description,
    string? AcceptanceCriteria,
    string? EpicId,
    PriorityEnum? Priority,
    string? WorkItemCategoryId,
    IReadOnlyList<UserStoryAssignmentRecord>? Assignments);

internal sealed record UserStoryAssignmentRecord(string? ApplicationUserId);

internal sealed record CreateUserStoryCommand(
    string EpicId,
    string Story,
    string? Description,
    string? AcceptanceCriteria,
    PriorityEnum Priority,
    UserStoryStagesEnum Status,
    IReadOnlyList<string>? AssignedTo,
    string? WorkItemCategoryId);

internal sealed record UpdateUserStoryCommand(
    string Id,
    string? Story,
    string? Description,
    string? AcceptanceCriteria,
    IReadOnlyList<string>? AssignedTo,
    string? EpicId,
    PriorityEnum Priority,
    string? WorkItemCategoryId);

internal sealed record MoveUserStoryStatusCommand(
    // The API's property name is misspelled; keep it as-is on the wire.
    [property: JsonPropertyName("userStorId")] string UserStoryId,
    UserStoryStagesEnum Status);

internal sealed record ReAssignUserStoryCommand(string UserStoryId, string AssignedToId, string AssignedFromId);

// ---- Project tasks ----

internal sealed record ProjectTaskRecord(
    string Id,
    string? Title,
    string? Description,
    GeneralStagesEnum? Status,
    PriorityEnum? Priority,
    PriorityEnum? Complexity,
    string? SkillId,
    string? AssignTo,
    double? PlannedHours,
    DateTime? EndDate,
    string? EpicId,
    string? UserStoryId);

internal sealed record CreateProjectTaskCommand(
    string ProjectId,
    string Title,
    string? Description,
    GeneralStagesEnum Status,
    PriorityEnum Priority,
    PriorityEnum Complexity,
    string? AssignTo,
    double PlannedHours,
    DateTime? EndDate,
    string? EpicId,
    string? UserStoryId,
    string? SkillId,
    string? WorkBucketId);

internal sealed record UpdateProjectTaskCommand(
    string Id,
    string? Title,
    string? Description,
    GeneralStagesEnum Status,
    PriorityEnum Priority,
    PriorityEnum Complexity,
    string? SkillId,
    string? AssignTo,
    double PlannedHours,
    DateTime? EndDate,
    string? EpicId,
    string? UserStoryId);

internal sealed record MoveTaskStatusCommand(string ProjectTaskId, string? SubTaskId, GeneralStagesEnum Status, double ManHours);

internal sealed record ReAssignProjectTaskCommand(string ProjectTaskId, string AssignedToId);

internal sealed record TaskWorkActionRequest(string ProjectTaskId);

// ---- Sub-tasks ----

internal sealed record CreateSubTaskCommand(
    string TaskId,
    string Title,
    string? Description,
    string? AssignTo,
    GeneralStagesEnum Status,
    PriorityEnum Priority,
    PriorityEnum Complexity,
    double PlannedHours,
    DateTime? EndDate,
    bool IsBlocker,
    string? SkillId);

// ---- Comments ----

internal sealed record CreateCommentCommand(
    string Remark,
    string? ProjectTaskId,
    string? SubTaskId,
    string? UserStoryId,
    IReadOnlyList<string>? TaggedMembers);
