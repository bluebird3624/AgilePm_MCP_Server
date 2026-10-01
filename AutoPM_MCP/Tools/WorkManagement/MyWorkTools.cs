using AutoPM_MCP.Helpers;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace AutoPM_MCP.Tools.WorkManagement;

/// <summary>
/// Personal work tools for the Agile PM WorkManagement controller.
/// </summary>
[McpServerToolType]
internal class MyWorkTools(AgilePmClient client)
{
    [McpServerTool(ReadOnly = true)]
    [Description("Lists the tasks assigned to the current user across all their projects, with status, due date and hours. " +
                 "The best starting point for 'what am I working on?'.")]
    public async Task<string> GetMyWork(
        [Description("Include completed tasks")] bool includeCompleted = false,
        CancellationToken cancellationToken = default)
    {
        var query = AgilePmQuery.Create().Add("IncludeCompleted", includeCompleted);
        return ToolResponse.Data(await client.GetAsync("WorkManagement/GetMyWork", query, cancellationToken));
    }
}
