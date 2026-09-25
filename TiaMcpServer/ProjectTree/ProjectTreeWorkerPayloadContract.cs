using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.Worker;

namespace TiaMcpServer.ProjectTree;

internal sealed record ProjectTreeObservation(
    string ResolvedProjectPath,
    IReadOnlyList<ProjectTreeSelectorSegment>? CanonicalStartSelector,
    int? Depth,
    IReadOnlyList<ProjectTreeNode> Roots,
    IReadOnlyList<string> Warnings);

internal sealed class ProjectTreeProtocolException : Exception
{
    public ProjectTreeProtocolException(string category, string message)
        : base(message)
    {
        Category = category;
    }

    public string Category { get; }
}

/// <summary>
/// The only host-side decoder of a successful project-tree worker snapshot. Rejects payloads that
/// do not exactly match the typed worker contract so stale worker data never reaches pagination.
/// </summary>
internal static class ProjectTreeWorkerPayloadContract
{
    private const string RejectionMessage =
        "The project-tree worker payload did not match its declared result contract and was rejected.";

    internal static ProjectTreeObservation Decode(
        WorkerCallResult workerResult,
        IReadOnlyList<ProjectTreeSelectorSegment>? requestedSelector,
        int? requestedDepth)
    {
        if (!workerResult.Success)
        {
            throw new InvalidOperationException(
                "Only successful worker results may enter the project-tree payload decoder.");
        }

        try
        {
            ValidateRequiredJsonShape(workerResult.Payload);
            var payload = CanonicalJson.Deserialize<ProjectTreeBrowseResultInfo>(workerResult.Payload);
            Validate(payload, requestedSelector, requestedDepth);
            var path = ProjectPathNormalization.Canonicalize(workerResult.ResolvedProjectPath)
                ?? throw new JsonException("The worker did not report a canonical resolved project path.");
            return new ProjectTreeObservation(
                path,
                CopySelector(payload.StartSelector),
                payload.Depth,
                payload.Roots,
                workerResult.Warnings.ToArray());
        }
        catch (Exception exception) when (exception is JsonException or ProjectTreeSelectionException)
        {
            throw new ProjectTreeProtocolException(
                WorkerFailureCategories.ProtocolError,
                RejectionMessage);
        }
    }

    private static void Validate(
        ProjectTreeBrowseResultInfo payload,
        IReadOnlyList<ProjectTreeSelectorSegment>? requestedSelector,
        int? requestedDepth)
    {
        if (payload.Roots is null)
        {
            throw new JsonException("'roots' is declared non-nullable but the payload was null.");
        }

        ProjectTreeNodeTypes.Validate(payload.StartSelector);
        ProjectTreeNodeTypes.Validate(requestedSelector);

        if (payload.Depth != requestedDepth)
        {
            throw new JsonException("The worker-reported depth did not match the requested depth.");
        }

        ValidateEquivalentSelector(payload.StartSelector, requestedSelector);
        foreach (var root in payload.Roots)
        {
            ValidateNode(root);
        }
    }

    private static void ValidateEquivalentSelector(
        IReadOnlyList<ProjectTreeSelectorSegment>? observed,
        IReadOnlyList<ProjectTreeSelectorSegment>? requested)
    {
        if (observed is null || requested is null)
        {
            if (observed is not null || requested is not null)
            {
                throw new JsonException(
                    "The worker-reported selector was not semantically equivalent to the request.");
            }

            return;
        }

        if (observed.Count != requested.Count)
        {
            throw new JsonException(
                "The worker-reported selector was not semantically equivalent to the request.");
        }

        for (var index = 0; index < observed.Count; index++)
        {
            if (!string.Equals(observed[index].NodeType, requested[index].NodeType, StringComparison.Ordinal)
                || !string.Equals(observed[index].Name, requested[index].Name, StringComparison.OrdinalIgnoreCase))
            {
                throw new JsonException(
                    "The worker-reported selector was not semantically equivalent to the request.");
            }
        }
    }

    private static void ValidateNode(ProjectTreeNode? node)
    {
        if (node is null)
        {
            throw new JsonException("The project-tree payload contained a null node.");
        }

        if (string.IsNullOrWhiteSpace(node.Name) || !IsKnownNodeType(node.NodeType))
        {
            throw new JsonException("The project-tree payload contained an invalid node.");
        }

        if (node.Details is not null)
        {
            foreach (var detail in node.Details)
            {
                if (string.Equals(detail.Key, "Path", StringComparison.OrdinalIgnoreCase))
                {
                    throw new JsonException("The project-tree payload contained a removed Path detail.");
                }

                if (detail.Value is null)
                {
                    throw new JsonException("The project-tree payload contained a non-string detail value.");
                }
            }
        }

        if (node.Children is null)
        {
            throw new JsonException("The project-tree payload contained null children.");
        }

        foreach (var child in node.Children)
        {
            ValidateNode(child);
        }
    }

    private static bool IsKnownNodeType(string nodeType)
    {
        foreach (var candidate in ProjectTreeNodeTypes.All)
        {
            if (string.Equals(candidate, nodeType, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<ProjectTreeSelectorSegment>? CopySelector(
        IReadOnlyList<ProjectTreeSelectorSegment>? selector)
        => selector?.Select(segment => new ProjectTreeSelectorSegment
        {
            NodeType = segment.NodeType,
            Name = segment.Name
        }).ToArray();

    private static void ValidateRequiredJsonShape(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        RequireObject(root, "project-tree result");
        RequireMembers(root, "project-tree result", "startSelector", "depth", "roots");

        var selector = root.GetProperty("startSelector");
        if (selector.ValueKind != JsonValueKind.Null)
        {
            RequireArray(selector, "startSelector");
            foreach (var segment in selector.EnumerateArray())
            {
                RequireObject(segment, "startSelector[]");
                RequireMembers(segment, "startSelector[]", "nodeType", "name");
                RequireString(segment.GetProperty("nodeType"), "startSelector[].nodeType");
                RequireString(segment.GetProperty("name"), "startSelector[].name");
            }
        }

        var roots = root.GetProperty("roots");
        RequireArray(roots, "roots");
        foreach (var node in roots.EnumerateArray())
        {
            ValidateNodeJson(node, "roots[]");
        }
    }

    private static void ValidateNodeJson(JsonElement node, string path)
    {
        RequireObject(node, path);
        RequireMembers(node, path, "name", "nodeType", "details", "children");
        RequireString(node.GetProperty("name"), $"{path}.name");
        RequireString(node.GetProperty("nodeType"), $"{path}.nodeType");

        var details = node.GetProperty("details");
        if (details.ValueKind != JsonValueKind.Null)
        {
            RequireObject(details, $"{path}.details");
            foreach (var detail in details.EnumerateObject())
            {
                if (string.Equals(detail.Name, "Path", StringComparison.OrdinalIgnoreCase))
                {
                    throw new JsonException($"'{path}.details' contains a removed Path member.");
                }

                RequireString(detail.Value, $"{path}.details.{detail.Name}");
            }
        }

        var children = node.GetProperty("children");
        RequireArray(children, $"{path}.children");
        foreach (var child in children.EnumerateArray())
        {
            ValidateNodeJson(child, $"{path}.children[]");
        }
    }

    private static void RequireMembers(JsonElement value, string path, params string[] members)
    {
        foreach (var member in members)
        {
            if (!value.TryGetProperty(member, out _))
            {
                throw new JsonException($"'{path}.{member}' is required.");
            }
        }
    }

    private static void RequireObject(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"'{path}' must be an object.");
        }
    }

    private static void RequireArray(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException($"'{path}' must be an array.");
        }
    }

    private static void RequireString(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"'{path}' must be a string.");
        }
    }
}
