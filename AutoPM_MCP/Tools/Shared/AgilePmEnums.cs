namespace AutoPM_MCP.Tools.Shared;

// Mirrors of Agile PM's models.constants enums. The API sends and expects their integer values;
// declaring them here lets MCP clients see names instead of bare numbers.
// Member order matters: it must match the API's definitions exactly.

/// <summary>Workflow stage of a project task or sub-task.</summary>
public enum GeneralStagesEnum
{
    Planned,
    InProgress,
    Returned,
    Completed,
    Closed,
    Cancelled,
}

/// <summary>Workflow stage of a user story.</summary>
public enum UserStoryStagesEnum
{
    Planned,
    In_Progress,
    Completed,
    Closed,
}

/// <summary>Priority (and complexity) of user stories, tasks and sub-tasks.</summary>
public enum PriorityEnum
{
    Low,
    Medium,
    High,
}
