using Agent.Memory.MemoryGraph;

namespace Agent.Endpoints;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/dashboard");

        group.MapGet("/memories", () => Array.Empty<object>());
        group.MapGet("/events", () => Array.Empty<object>());
        group.MapGet("/graph", () => new
        {
            Nodes = Array.Empty<MemoryGraphNode>(),
            Edges = Array.Empty<MemoryGraphEdge>()
        });

        return endpoints;
    }
}
