using ModelContextProtocol.Protocol;
using TiaMcpServer.Tools;
using Xunit;

namespace TiaMcpServer.Tests.Tools;

public sealed class StructuredToolResultTests
{
    [Fact]
    public void CreateCanonical_UsesTheAcceptedTextForBothMcpRepresentations()
    {
        const string canonical = "{\"contractVersion\":\"3.0\",\"failure\":null,\"result\":null,\"status\":\"succeeded\",\"warnings\":[]}";

        var result = StructuredToolResult.CreateCanonical(canonical, isError: false);

        Assert.Equal(canonical, Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        Assert.Equal(canonical, result.StructuredContent!.Value.GetRawText());
        Assert.False(result.IsError);
    }
}
