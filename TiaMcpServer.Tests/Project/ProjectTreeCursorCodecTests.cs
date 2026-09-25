using System.Security.Cryptography;
using System.Text;
using TiaMcpServer.Contracts;
using TiaMcpServer.Cursors;
using TiaMcpServer.ProjectTree;
using Xunit;

namespace TiaMcpServer.Tests.Project;

public class ProjectTreeCursorCodecTests
{
    private const string QueryHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly byte[] TestKey = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
    private static readonly byte[] OtherKey = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();

    [Fact]
    public void EncodeDecode_RoundTripsTheExactThreeFieldState()
    {
        using var protector = new AuthenticatedCursorProtector(TestKey, "process-a");
        var codec = new ProjectTreeCursorCodec(protector);
        var state = new ProjectTreeCursorState("snapshot-1", QueryHash, 10);

        var decoded = codec.Decode(codec.Encode(state));

        Assert.Equal(state.SnapshotId, decoded.SnapshotId);
        Assert.Equal(state.QueryHash, decoded.QueryHash);
        Assert.Equal(state.Offset, decoded.Offset);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("snapshot", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", 0)]
    [InlineData("snapshot", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 0)]
    [InlineData("snapshot", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", -1)]
    public void Encode_RejectsInvalidDomainState(params object[] values)
    {
        using var protector = new AuthenticatedCursorProtector(TestKey, "process-a");
        var codec = new ProjectTreeCursorCodec(protector);
        var snapshotId = (string)values[0];
        var queryHash = values.Length > 1 ? (string)values[1] : QueryHash;
        var offset = values.Length > 2 ? (int)values[2] : 0;

        AssertCategory(WorkerFailureCategories.InvalidCursor, () =>
            codec.Encode(new ProjectTreeCursorState(snapshotId, queryHash, offset)));
    }

    [Fact]
    public void Decode_WrongPurposeAndTamperingMapToInvalidCursor()
    {
        using var protector = new AuthenticatedCursorProtector(TestKey, "process-a");
        var codec = new ProjectTreeCursorCodec(protector);
        var wrongPurpose = protector.Protect("hardware-page", new ProjectTreeCursorState("snapshot-1", QueryHash, 0));
        var parts = codec.Encode(new ProjectTreeCursorState("snapshot-1", QueryHash, 0)).Split('.');

        AssertCategory(WorkerFailureCategories.InvalidCursor, () => codec.Decode(wrongPurpose));
        AssertCategory(WorkerFailureCategories.InvalidCursor, () =>
            codec.Decode($"{ChangeByte(parts[0])}.{parts[1]}"));
    }

    [Fact]
    public void Decode_RejectsCursorOverTheProtectorLimitAndCurrentProcessInvalidPayload()
    {
        using var protector = new AuthenticatedCursorProtector(TestKey, "process-a");
        var codec = new ProjectTreeCursorCodec(protector);
        var oversized = new string('x', 4_097);
        var invalidPayload = Sign(
            "{\"formatVersion\":1,\"processInstanceId\":\"process-a\",\"purpose\":\"project-tree\",\"state\":{\"snapshotId\":\"snapshot-1\",\"queryHash\":\"" + QueryHash + "\",\"offset\":-1}}",
            TestKey);

        AssertCategory(WorkerFailureCategories.InvalidCursor, () => codec.Decode(oversized));
        AssertCategory(WorkerFailureCategories.InvalidCursor, () => codec.Decode(invalidPayload));
    }

    [Fact]
    public void Decode_ForeignProcessMapsToSnapshotUnavailable()
    {
        using var issuer = new AuthenticatedCursorProtector(TestKey, "process-a");
        using var reader = new AuthenticatedCursorProtector(OtherKey, "process-b");
        var cursor = new ProjectTreeCursorCodec(issuer).Encode(
            new ProjectTreeCursorState("snapshot-1", QueryHash, 10));

        var error = Assert.Throws<ProjectTreeCursorException>(() =>
            new ProjectTreeCursorCodec(reader).Decode(cursor));

        Assert.Equal(WorkerFailureCategories.SnapshotUnavailable, error.Category);
    }

    [Fact]
    public void Encode_RejectsQueryHashWithTrailingNewline()
    {
        using var protector = new AuthenticatedCursorProtector(TestKey, "process-a");

        AssertCategory(WorkerFailureCategories.InvalidCursor, () =>
            new ProjectTreeCursorCodec(protector).Encode(
                new ProjectTreeCursorState("snapshot-1", QueryHash + "\n", 0)));
    }

    [Fact]
    public void Decode_RejectsQueryHashWithTrailingNewline()
    {
        using var protector = new AuthenticatedCursorProtector(TestKey, "process-a");
        var cursor = protector.Protect(
            "project-tree",
            new ProjectTreeCursorState("snapshot-1", QueryHash + "\n", 0));

        AssertCategory(WorkerFailureCategories.InvalidCursor, () =>
            new ProjectTreeCursorCodec(protector).Decode(cursor));
    }

    private static void AssertCategory(string category, Action action)
    {
        var error = Assert.Throws<ProjectTreeCursorException>(action);
        Assert.Equal(category, error.Category);
    }

    private static string ChangeByte(string value)
        => (value[0] == 'A' ? "B" : "A") + value[1..];

    private static string Sign(string payload, byte[] key)
    {
        using var hmac = new HMACSHA256(key);
        var bytes = Encoding.UTF8.GetBytes(payload);
        return $"{Base64Url(bytes)}.{Base64Url(hmac.ComputeHash(bytes))}";
    }

    private static string Base64Url(byte[] value)
        => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
