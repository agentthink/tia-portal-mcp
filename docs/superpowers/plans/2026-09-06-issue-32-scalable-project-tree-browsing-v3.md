# Issue #32 Scalable Project-Tree Browsing v3 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the legacy `browse_project_tree` array and `startPath` contract with one typed v3 response that scopes work by an unambiguous typed selector, performs one worker observation per snapshot, and serves bounded continuation pages from a host-owned cache.

**Architecture:** The net48 worker validates the selector, resolves its first `Device` segment across the complete recursive device enumeration, walks either all devices or the one selected device, resolves deeper segments against the materialized DTO tree, and applies `depth`. The net8 host strictly decodes that typed worker payload, flattens it into stable pre-order nodes, measures and caches an immutable point-in-time snapshot, and projects exact canonical pages no larger than 60,000 characters. A shared process-scoped HMAC protector authenticates both existing hardware cursors and the new project-tree cursors, while domain codecs retain their own validation and failure mapping. The work lands as five cumulative, CI-green pull requests; PRs 1-4 add tested internal foundations while the public v2 tool remains intact, and PR 5 performs the one atomic public v3 cutover for the single `v3.0.0` release.

**Tech Stack:** C#; .NET 8 host; .NET Framework 4.8 Openness worker; `netstandard2.0` shared contracts; Model Context Protocol C# SDK; `System.Text.Json`; `System.Security.Cryptography`; xUnit; the existing persistent newline-delimited worker IPC, `CanonicalJson`, `StructuredToolResult`, FakeWorker, and PowerShell package-verification patterns.

**Spec:** [`docs/superpowers/specs/2026-09-06-issue-32-scalable-project-tree-browsing-v3-design.md`](../specs/2026-09-06-issue-32-scalable-project-tree-browsing-v3-design.md)

## Global Constraints

- This is the intentional `v3.0.0` breaking replacement for `browse_project_tree`. Do not retain a legacy response mode, `startPath`, or add `deviceName`/`plcName` beside the typed selector.
- The public request accepts only `projectPath`, `startSelector`, `depth`, `pageSize`, and `cursor`. Reject unknown members at the MCP boundary; each selector segment accepts exactly `nodeType` and `name`.
- The response contract version is exactly `"3.0"`. Every success and failure uses the same declared envelope with `contractVersion`, `status`, `result`, `failure`, and `warnings`.
- One final `CanonicalJson` text instance must supply both the MCP text block and `structuredContent`. Candidate page sizing may serialize several prospective responses, but the accepted serialization must be reused rather than generated again.
- Keep every Siemens type and object in the net48 worker. The host receives only shared DTOs, resolved query evidence, warnings, and immutable public node data.
- Resolve the first `Device` segment across all direct and recursively grouped devices before calling `PlcSoftwareLocator.FindInDevice`. For this issue, walk the complete selected device before resolving deeper selector segments and `depth`; do not add partial direct-resolution fallbacks.
- Node-type matching is ordinal and case-sensitive. Name matching is exact and ordinal-ignore-case. Never select the first of multiple matches.
- The stable node vocabulary is exactly `Device`, `PlcSoftware`, `SoftwareUnit`, `BlockFolder`, `SystemBlockFolder`, `OB`, `FB`, `FC`, `GlobalDB`, `InstanceDB`, `ArrayDB`, `Block`, `TagTableFolder`, `TagTable`, `TypeFolder`, and `Type`.
- Preserve direct/grouped device completeness, flat public `Device` roots, `SystemBlockFolder`, functional block node types, and hierarchy-only `details.IsSystemBlock`.
- Remove legacy `details.Path`. Public flat nodes contain exactly `nodeId`, `parentNodeId`, `sequence`, `name`, `nodeType`, and `details`; use an empty details object when the worker DTO has no details.
- Snapshot metadata contains exactly `snapshotId`, `createdAt`, `idleExpiresAt`, and `totalNodes`; both timestamps are UTC ISO-8601 values and `idleExpiresAt` is not an eviction guarantee.
- Assign IDs and contiguous zero-based pre-order sequences only after selector and depth filtering. Parent nodes always precede descendants; a selected subtree root has `parentNodeId: null`.
- A cursor-free request always performs a fresh worker observation. Do not coalesce identical initial requests.
- A continuation never calls the worker and never re-observes TIA. Expiry, eviction, host restart, or a foreign process cursor returns `snapshot_unavailable` and requires a new first page.
- Default `pageSize` is 100; accepted values are `1..200`; continuation may change it. It is an upper bound because the 60,000-character response budget may reduce the returned count.
- Snapshot limits are 4,000,000 canonical serialized characters per snapshot, four cached snapshots, 16,000,000 aggregate measured characters, and a 10-minute sliding TTL.
- Measure snapshot query plus all filtered flat nodes outside the cache lock. Perform expiry cleanup, lookup, query/range checks, exact page projection, successful-access renewal, insertion, and eviction under one private lock.
- A successful page is the largest complete prefix that fits. Make at most 200 exact canonical serialization attempts; never estimate sizes, split nodes, truncate JSON, skip a node, or return an empty success while unreturned nodes remain.
- A one-page result is not retained. A cached multi-page snapshot remains after its final page so earlier cursors can be replayed until expiry or eviction.
- LRU ties are deterministic: oldest successful access, then creation time, then ordinal `snapshotId`. Failed cursor, filter, range, or projection attempts do not renew TTL or LRU position.
- Use the closed failure vocabulary exactly: new `invalid_selector`, `snapshot_too_large`, `snapshot_unavailable`, `result_item_too_large`, and `result_metadata_too_large`; existing `target_not_found`, `target_ambiguous`, `invalid_cursor`, `cursor_filter_mismatch`, `cursor_out_of_range`, `worker_timeout`, `worker_crashed`, and `protocol_error` categories retain their specified meanings.
- Migrate host hardware pagination to the shared 32-byte-key HMAC-SHA256 protector without changing its hardware-specific validation. Leave the net48 `NetworkObjectCursorCodec` separate.
- Select timeout/crash guidance only from `OperationPolicyCatalog.GetCapability(request.Method)`. Unknown methods fail closed to state-affecting guidance. Keep categories, binding invalidation, and no-retry behavior unchanged.
- Follow behavioral TDD: add the focused test first, observe a relevant RED, make the smallest production change, and rerun to GREEN. A compile failure caused only by a newly referenced type is acceptable when that missing type is the behavior under test; an unrelated inherited failure is not.
- Do not commit merely because a task reaches its commit boundary. Every commit requires explicit user authorization.
- Do not run a live TIA Portal harness without separate explicit authorization. Stub, offline, and FakeWorker results are not live Siemens Openness acceptance.

---

## Stacked Pull-Request Delivery

This remains one implementation plan because the five pull requests form one dependency-ordered release rather than independent subprojects. Each branch contains its predecessor's complete history, must build, and must pass the applicable CI suite before review. Merge or retarget the stack from the bottom upward; do not release an intermediate branch.

| PR | Branch | Base | Owned tasks | Public `browse_project_tree` state |
| --- | --- | --- | --- | --- |
| 1 — Tree domain model and resolver | `feature/tree-domain` | `main` | 1-2 | Existing v2 behavior unchanged |
| 2 — Worker observation boundary | `feature/worker-observation` | `feature/tree-domain` | 3-5 | Existing v2 behavior unchanged; internal v3 worker operation added |
| 3 — Authenticated cursor foundation | `feature/cursor-foundation` | `feature/worker-observation` | 6 | Existing v2 behavior unchanged |
| 4 — Bounded project-tree pagination | `feature/tree-pagination` | `feature/cursor-foundation` | 7-8 | Existing v2 behavior unchanged; pagination remains internal |
| 5 — Public v3 cutover and release integration | `feature/v3-cutover` | `feature/tree-pagination` | 9-11 | Atomic replacement with the sole public v3 contract |

Task 12 is not another implementation PR. It is the separately authorized, read-only live TIA Portal V21 acceptance gate for the complete five-PR stack.

### Per-PR CI invariant

At the end of every PR, run its focused RED/GREEN tests and the repository's complete applicable CI workflow against that cumulative branch. At minimum, reproduce the standard offline gate locally:

```powershell
dotnet restore TiaMcpServer.sln
dotnet build TiaMcpServer.sln -c Release -m:1 --no-restore --disable-build-servers /p:UseTiaPortalReferenceStubs=true
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Release --no-build -m:1 --disable-build-servers
```

If repository CI contains additional jobs, those jobs must also pass. A later PR may strengthen the suite, but it may not defer a known build or test failure from an earlier PR. Packaging and release-level contract verification are repeated comprehensively in Task 11. Commits, pushes, PR creation, and merges still require the user's separate authorization.

---

## File Structure

### Shared contracts and worker

- Create `TiaMcpServer.Contracts/ProjectTreeNodeTypes.cs` for the stable node vocabulary and transition table.
- Create `TiaMcpServer.Contracts/ProjectTreeSelectorSegment.cs` for strict typed selector input/IPC data.
- Create `TiaMcpServer.Contracts/ProjectTreeSelectionException.cs` for category-preserving pure resolver failures.
- Create `TiaMcpServer.Contracts/ProjectTreeSelectionResult.cs` for filtered roots and canonical observed selector spelling.
- Create `TiaMcpServer.Contracts/ProjectTreeBrowseResultInfo.cs` for the one declared worker success payload.
- Create `TiaMcpServer.Contracts/ProjectTreeDeviceSelector.cs` for generic pre-walk device selection.
- Modify `TiaMcpServer.Contracts/ProjectTreeFilter.cs` to resolve typed direct-child segments and apply exact depth pruning without mutation.
- Modify `TiaMcpServer.Contracts/ProjectTreeNode.cs`, `WorkerRequest.cs`, `WorkerFailureCategories.cs`, and `WorkerProtocol.cs` for the v3 seam.
- Create `TiaMcpServer.OpennessWorker/Openness/ProjectTreeSnapshotWalker.cs` for scoped, path-free v3 observation while the existing v2 walker remains intact through PR 4.
- Modify `TiaMcpServer.OpennessWorker/Program.cs` to validate the request and return `ProjectTreeBrowseResultInfo`.

### Host cursor, snapshot, and tool pipeline

- Create `TiaMcpServer/Cursors/AuthenticatedCursorProtector.cs` for strict process-scoped authenticated framing and purpose separation.
- Modify `TiaMcpServer/Network/HardwarePageCursorCodec.cs` and `NetworkReadTools.cs` to consume that shared protector while retaining the existing compatibility executor path.
- Create `TiaMcpServer/ProjectTree/ProjectTreeBrowseRequest.cs` for host validation and continuation query matching.
- Create `TiaMcpServer/ProjectTree/ProjectTreeToolResponses.cs` for the public v3 input-independent output schema.
- Create `TiaMcpServer/ProjectTree/ProjectTreeWorkerPayloadContract.cs` for strict success decoding and fail-closed protocol projection.
- Create `TiaMcpServer/ProjectTree/ProjectTreeFlattener.cs` for post-filter pre-order IDs and parent relationships.
- Create `TiaMcpServer/ProjectTree/ProjectTreeCursorCodec.cs` for the exact `snapshotId`/`queryHash`/`offset` state.
- Create `TiaMcpServer/ProjectTree/ProjectTreeSnapshotStore.cs` for TTL, LRU, size bounds, replay, disposal, and lock ownership.
- Create `TiaMcpServer/ProjectTree/ProjectTreePageProjector.cs` for exact 60,000-character page selection.
- Create `TiaMcpServer/ProjectTree/ProjectTreeBrowseCoordinator.cs` for initial-worker and cached-continuation orchestration.
- Modify `TiaMcpServer/Tools/StructuredToolResult.cs` to accept an already-canonical final document.
- Modify `TiaMcpServer/Tools/ProjectReadTools.cs`, `TiaMcpServer/Worker/OpennessWorkerClient.cs`, and `TiaMcpServer/Program.cs` to expose and register the new pipeline.
- Modify `TiaMcpServer.FakeWorker/Program.cs` with typed, one-shot, malformed, oversized, and restart-safe browse scenarios.

### Tests, maintained docs, and acceptance evidence

- Modify `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj` to link the new host cursor and project-tree source files.
- Replace legacy path tests in `TiaMcpServer.Tests/Project/ProjectTreeFilterTests.cs` and extend the existing project/worker/schema tests.
- Create focused test files under `TiaMcpServer.Tests/Cursors/` and `TiaMcpServer.Tests/Project/` matching the components above.
- Create `scripts/live-test-project-tree-v3.ps1` as a read-only, separately gated live evidence harness; do not execute it during offline implementation.
- Update `README.md`, `docs/ARCHITECTURE.md`, `docs/SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md`, `docs/development/local-mcp-testing.md`, and `docs/IMPROVEMENT_LOG.md`.
- After an authorized live run, create `docs/superpowers/acceptance/reports/2026-09-06-issue-32-project-tree-v3-live.md` and index it in both documentation indexes.

---

## PR 1 — Tree Domain Model and Resolver

**Branch:** `feature/tree-domain`

**Base:** `main`

**Exit condition:** Tasks 1-2 are complete, the public v2 tool is behaviorally unchanged, and the cumulative branch passes the per-PR CI invariant.

### Task 1: Define the Dormant v3 Domain and Response Contracts

**Files:**

- Create: `TiaMcpServer.Contracts/ProjectTreeNodeTypes.cs`
- Create: `TiaMcpServer.Contracts/ProjectTreeSelectorSegment.cs`
- Create: `TiaMcpServer.Contracts/ProjectTreeSelectionException.cs`
- Create: `TiaMcpServer.Contracts/ProjectTreeSelectionResult.cs`
- Create: `TiaMcpServer.Contracts/ProjectTreeBrowseResultInfo.cs`
- Create: `TiaMcpServer/ProjectTree/ProjectTreeToolResponses.cs`
- Modify: `TiaMcpServer.Contracts/WorkerRequest.cs`
- Modify: `TiaMcpServer.Contracts/WorkerFailureCategories.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`
- Create: `TiaMcpServer.Tests/Project/ProjectTreeV3ContractTests.cs`
- Modify: `TiaMcpServer.Tests/Worker/WorkerResponseJsonTests.cs`

**Interfaces:**

```csharp
public sealed class ProjectTreeSelectorSegment
{
    public string NodeType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class ProjectTreeBrowseResultInfo
{
    public List<ProjectTreeSelectorSegment>? StartSelector { get; set; }
    public int? Depth { get; set; }
    public List<ProjectTreeNode> Roots { get; set; } = new();
}

public sealed record BrowseProjectTreeResponse(
    string ContractVersion,
    string Status,
    BrowseProjectTreeResult? Result,
    BrowseProjectTreeFailure? Failure,
    IReadOnlyList<string> Warnings);
```

- [ ] **Step 1: Add failing exact-shape and failure-vocabulary tests**

  Cover explicit nulls in the envelope, exact flat-node fields, `details: {}` for an empty dictionary, selector segment unknown-member rejection, and every new failure category being accepted by `WorkerCallResult.Fail`.

  ```csharp
  [Fact]
  public void SuccessEnvelope_UsesTheExactV3Shape()
  {
      var response = new BrowseProjectTreeResponse(
          ProjectTreeContract.Version,
          ProjectTreeStatuses.Succeeded,
          new BrowseProjectTreeResult(
              new ProjectTreeSnapshotMetadata("snapshot-1", CreatedAt, ExpiresAt, 1),
              new ProjectTreeQuery(@"C:\Projects\Plant.ap21", null, null),
              new ProjectTreePagination(0, 100, 1, null),
              new[] { new ProjectTreeFlatNode("n0", null, 0, "PLC_1", "Device", new Dictionary<string, string>()) }),
          Failure: null,
          Warnings: Array.Empty<string>());

      using var document = JsonDocument.Parse(CanonicalJson.Serialize(response));
      Assert.Equal("3.0", document.RootElement.GetProperty("contractVersion").GetString());
      Assert.Equal("succeeded", document.RootElement.GetProperty("status").GetString());
      Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("failure").ValueKind);
      Assert.Equal(6, document.RootElement.GetProperty("result").GetProperty("nodes")[0].EnumerateObject().Count());
  }

  [Theory]
  [InlineData(WorkerFailureCategories.InvalidSelector)]
  [InlineData(WorkerFailureCategories.SnapshotTooLarge)]
  [InlineData(WorkerFailureCategories.SnapshotUnavailable)]
  [InlineData(WorkerFailureCategories.ResultItemTooLarge)]
  [InlineData(WorkerFailureCategories.ResultMetadataTooLarge)]
  public void ProjectTreeFailureCategory_IsKnown(string category)
      => Assert.True(WorkerFailureCategories.IsKnown(category));
  ```

- [ ] **Step 2: Run the focused tests and confirm RED**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~ProjectTreeV3ContractTests|FullyQualifiedName~WorkerResponseJsonTests"
  ```

  Expected RED: the v3 types and category constants do not exist, and `WorkerRequest` has no typed selector member.

- [ ] **Step 3: Add the exact shared selector and worker-payload types**

  Apply strict unmapped-member handling to the selector segment and add `StartSelector` to the wire request for the internal v3 worker operation introduced in PR 2. Retain the existing `StartPath` member and its existing v2 callers through PRs 1-4; do not add any new caller of it.

  ```csharp
  [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
  public sealed class ProjectTreeSelectorSegment
  {
      public string NodeType { get; set; } = string.Empty;
      public string Name { get; set; } = string.Empty;
  }

  // WorkerRequest.cs; remove the legacy StartPath member during Task 9's public cutover.
  public List<ProjectTreeSelectorSegment>? StartSelector { get; set; }
  ```

  Define `ProjectTreeNodeTypes.All` with exactly:

  ```csharp
  public static readonly IReadOnlyCollection<string> All = new HashSet<string>(StringComparer.Ordinal)
  {
      Device, PlcSoftware, SoftwareUnit, BlockFolder, SystemBlockFolder,
      Ob, Fb, Fc, GlobalDb, InstanceDb, ArrayDb, Block,
      TagTableFolder, TagTable, TypeFolder, Type
  };
  ```

- [ ] **Step 4: Add the public output records and closed response constants**

  ```csharp
  public static class ProjectTreeContract
  {
      public const string Version = "3.0";
      public const int DefaultPageSize = 100;
      public const int MinimumPageSize = 1;
      public const int MaximumPageSize = 200;
      public const int MaximumResponseChars = 60_000;
  }

  public sealed record ProjectTreeSnapshotMetadata(
      string SnapshotId,
      DateTimeOffset CreatedAt,
      DateTimeOffset IdleExpiresAt,
      int TotalNodes);

  public sealed record ProjectTreeQuery(
      string ProjectPath,
      IReadOnlyList<ProjectTreeSelectorSegment>? StartSelector,
      int? Depth);

  public sealed record ProjectTreePagination(
      int Offset,
      int RequestedPageSize,
      int ReturnedCount,
      string? NextCursor);

  public sealed record ProjectTreeFlatNode(
      string NodeId,
      string? ParentNodeId,
      int Sequence,
      string Name,
      string NodeType,
      IReadOnlyDictionary<string, string> Details);
  ```

  Keep every envelope member non-optional in the CLR type so `CanonicalJson` emits explicit nulls for the inapplicable branch.

- [ ] **Step 5: Extend the closed failure vocabulary**

  ```csharp
  public const string InvalidSelector = "invalid_selector";
  public const string SnapshotTooLarge = "snapshot_too_large";
  public const string SnapshotUnavailable = "snapshot_unavailable";
  public const string ResultItemTooLarge = "result_item_too_large";
  public const string ResultMetadataTooLarge = "result_metadata_too_large";
  ```

  Add all five values to `WorkerFailureCategories.Known` and make the existing timeout/crash XML comments capability-neutral; do not change their string values.

- [ ] **Step 6: Link the new host folders into the test project and confirm GREEN**

  ```xml
  <Compile Include="..\TiaMcpServer\Cursors\*.cs" Link="Host\Cursors\%(Filename)%(Extension)" />
  <Compile Include="..\TiaMcpServer\ProjectTree\*.cs" Link="Host\ProjectTree\%(Filename)%(Extension)" />
  ```

  Run the Step 2 command, then:

  ```powershell
  dotnet build TiaMcpServer.Contracts/TiaMcpServer.Contracts.csproj --no-restore -m:1 --disable-build-servers
  ```

- [ ] **Step 7: Review the contract diff and stop at the commit boundary**

  Confirm there is one intended v3 response shape, selector segments have only two fields, and every response null is intentional. These new types remain dormant in PR 1. `WorkerRequest.StartPath` and the existing public v2 path remain unchanged until Task 9.

  ```powershell
  git diff --check
  git diff -- TiaMcpServer.Contracts TiaMcpServer/ProjectTree TiaMcpServer.Tests/TiaMcpServer.Tests.csproj
  ```

  Do not commit without explicit authorization. When authorized, use:

  ```powershell
  git add TiaMcpServer.Contracts TiaMcpServer/ProjectTree/ProjectTreeToolResponses.cs TiaMcpServer.Tests/TiaMcpServer.Tests.csproj TiaMcpServer.Tests/Project/ProjectTreeV3ContractTests.cs TiaMcpServer.Tests/Worker/WorkerResponseJsonTests.cs
  git commit -m "feat(project): define project tree v3 contracts"
  ```

---

### Task 2: Resolve Typed Selectors and Exact Depth in Pure DTO Code

**Files:**

- Create: `TiaMcpServer.Contracts/ProjectTreeDeviceSelector.cs`
- Modify: `TiaMcpServer.Contracts/ProjectTreeNodeTypes.cs`
- Modify: `TiaMcpServer.Contracts/ProjectTreeFilter.cs`
- Modify: `TiaMcpServer.Tests/Project/ProjectTreeFilterTests.cs`
- Create: `TiaMcpServer.Tests/Project/ProjectTreeDeviceSelectorTests.cs`

**Interfaces:**

```csharp
public static ProjectTreeSelectionResult Apply(
    List<ProjectTreeNode> roots,
    IReadOnlyList<ProjectTreeSelectorSegment>? startSelector,
    int? depth);

public static TDevice Select<TDevice>(
    IReadOnlyList<TDevice> devices,
    Func<TDevice, string> getName,
    ProjectTreeSelectorSegment firstSegment);
```

- [ ] **Step 1: Replace legacy path tests with failing typed-selector tests**

  Build the fixture with real v3 node types and table-drive every allowed transition plus invalid root, unknown type, blank name, empty selector, impossible transition, leaf continuation, missing target, and duplicate direct-child target.

  ```csharp
  public static IEnumerable<object[]> ValidTransitions()
  {
      yield return new object[] { "Device", "PlcSoftware" };
      yield return new object[] { "PlcSoftware", "SoftwareUnit" };
      yield return new object[] { "PlcSoftware", "BlockFolder" };
      yield return new object[] { "PlcSoftware", "TagTableFolder" };
      yield return new object[] { "PlcSoftware", "TypeFolder" };
      yield return new object[] { "SoftwareUnit", "BlockFolder" };
      yield return new object[] { "SoftwareUnit", "TagTableFolder" };
      yield return new object[] { "SoftwareUnit", "TypeFolder" };
      yield return new object[] { "BlockFolder", "BlockFolder" };
      yield return new object[] { "BlockFolder", "SystemBlockFolder" };
      foreach (var leaf in ProjectTreeNodeTypes.BlockLeaves)
      {
          yield return new object[] { "BlockFolder", leaf };
          yield return new object[] { "SystemBlockFolder", leaf };
      }
      yield return new object[] { "SystemBlockFolder", "SystemBlockFolder" };
      yield return new object[] { "TagTableFolder", "TagTableFolder" };
      yield return new object[] { "TagTableFolder", "TagTable" };
      yield return new object[] { "TypeFolder", "TypeFolder" };
      yield return new object[] { "TypeFolder", "Type" };
  }

  [Fact]
  public void DuplicateDirectChild_IsTargetAmbiguousRatherThanFirstMatch()
  {
      var tree = TreeWithDuplicateMotorFolders();
      var error = Assert.Throws<ProjectTreeSelectionException>(() =>
          ProjectTreeFilter.Apply(tree, Selector("Device", "PLC_1", "PlcSoftware", "PLC_1", "BlockFolder", "Motors"), null));

      Assert.Equal(WorkerFailureCategories.TargetAmbiguous, error.Category);
  }
  ```

- [ ] **Step 2: Add failing depth, immutability, and differential-equivalence tests**

  ```csharp
  [Fact]
  public void DepthOne_PromotesSelectedRootAndReportsExactChildrenOmitted()
  {
      var source = SampleTree();
      var selected = ProjectTreeFilter.Apply(source, MotorsSelector(), depth: 1);

      var root = Assert.Single(selected.Roots);
      Assert.Empty(root.Children!);
      Assert.Equal("2", root.Details!["ChildrenOmitted"]);
      Assert.Equal(2, FindMotors(source).Children!.Count);
      Assert.False(FindMotors(source).Details!.ContainsKey("ChildrenOmitted"));
  }

  [Fact]
  public void TypedSelection_EqualsIndependentlyClonedSubtreeFromCompleteTree()
  {
      var full = SampleTree();
      var actual = ProjectTreeFilter.Apply(full, MotorsSelector(), depth: 2).Roots;
      var expected = IndependentlyCloneAndPrune(FindMotors(full), depth: 2);
      Assert.Equal(CanonicalJson.Serialize(new[] { expected }), CanonicalJson.Serialize(actual));
  }
  ```

- [ ] **Step 3: Run the resolver tests and confirm RED**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~ProjectTreeFilterTests|FullyQualifiedName~ProjectTreeDeviceSelectorTests"
  ```

  Expected RED: the filter still accepts a slash path, scans globally, and reports `InvalidOperationException` rather than category-preserving typed failures.

- [ ] **Step 4: Implement the complete transition table and structural validation**

  ```csharp
  public static void Validate(IReadOnlyList<ProjectTreeSelectorSegment>? selector)
  {
      if (selector is null)
      {
          return;
      }

      if (selector.Count == 0)
      {
          throw ProjectTreeSelectionException.Invalid("startSelector must be omitted or contain at least one segment.");
      }

      for (var index = 0; index < selector.Count; index++)
      {
          var segment = selector[index];
          if (segment is null || !All.Contains(segment.NodeType) || string.IsNullOrWhiteSpace(segment.Name))
          {
              throw ProjectTreeSelectionException.Invalid("startSelector contains an invalid segment.");
          }

          var parentType = index == 0 ? null : selector[index - 1].NodeType;
          if (!CanFollow(parentType, segment.NodeType))
          {
              throw ProjectTreeSelectionException.Invalid("startSelector contains an impossible parent-child transition.");
          }
      }
  }
  ```

  `CanFollow(null, child)` accepts only `Device`; every leaf type maps to an empty child set.

- [ ] **Step 5: Implement direct-child resolution with canonical observed spelling**

  ```csharp
  private static ProjectTreeNode ResolveOne(
      IEnumerable<ProjectTreeNode> candidates,
      ProjectTreeSelectorSegment requested,
      List<ProjectTreeSelectorSegment> canonical)
  {
      var matches = candidates.Where(node =>
          string.Equals(node.NodeType, requested.NodeType, StringComparison.Ordinal)
          && string.Equals(node.Name, requested.Name, StringComparison.OrdinalIgnoreCase)).ToList();

      if (matches.Count == 0)
      {
          throw new ProjectTreeSelectionException(WorkerFailureCategories.TargetNotFound, "The typed project-tree selector did not resolve to a target.");
      }

      if (matches.Count != 1)
      {
          throw new ProjectTreeSelectionException(WorkerFailureCategories.TargetAmbiguous, "The typed project-tree selector resolved to multiple targets.");
      }

      canonical.Add(new ProjectTreeSelectorSegment { NodeType = matches[0].NodeType, Name = matches[0].Name });
      return matches[0];
  }
  ```

  Resolve the first segment only against roots and each later segment only against the previous match's direct children. Clone before returning; apply `depth` after selection; add exact `ChildrenOmitted` only when observed children were pruned.

- [ ] **Step 6: Implement generic device preselection**

  Reuse the same exact name and multiplicity semantics before any device walker callback can run.

  ```csharp
  public static TDevice Select<TDevice>(
      IReadOnlyList<TDevice> devices,
      Func<TDevice, string> getName,
      ProjectTreeSelectorSegment firstSegment)
  {
      if (!string.Equals(firstSegment.NodeType, ProjectTreeNodeTypes.Device, StringComparison.Ordinal))
      {
          throw ProjectTreeSelectionException.Invalid("The first startSelector segment must be Device.");
      }

      var matches = devices.Where(device =>
          string.Equals(getName(device), firstSegment.Name, StringComparison.OrdinalIgnoreCase)).ToList();
      return matches.Count switch
      {
          1 => matches[0],
          0 => throw new ProjectTreeSelectionException(WorkerFailureCategories.TargetNotFound, "The selected device was not found."),
          _ => throw new ProjectTreeSelectionException(WorkerFailureCategories.TargetAmbiguous, "The selected device name is ambiguous.")
      };
  }
  ```

- [ ] **Step 7: Run focused tests, review, and stop at the commit boundary**

  Run the Step 3 command again and confirm GREEN. Then verify the old path mechanism is gone from the pure selector surface:

  ```powershell
  rg -n "startPath|FindByPath|Details\[\"Path\"\]" TiaMcpServer.Contracts TiaMcpServer.Tests/Project/ProjectTreeFilterTests.cs
  git diff --check
  ```

  Expected search result: no matches in the files under review.

  Do not commit without explicit authorization. When authorized, use:

  ```powershell
  git add TiaMcpServer.Contracts/ProjectTreeNodeTypes.cs TiaMcpServer.Contracts/ProjectTreeDeviceSelector.cs TiaMcpServer.Contracts/ProjectTreeFilter.cs TiaMcpServer.Tests/Project/ProjectTreeFilterTests.cs TiaMcpServer.Tests/Project/ProjectTreeDeviceSelectorTests.cs
  git commit -m "feat(project): resolve typed tree selectors"
  ```

---

## PR 2 — Worker Observation Boundary

**Branch:** `feature/worker-observation`

**Base:** `feature/tree-domain`

**Exit condition:** Tasks 3-5 are complete, the internal v3 worker seam is typed and fail-closed, the public v2 tool remains behaviorally unchanged, and the cumulative branch passes the per-PR CI invariant.

### Task 3: Add a Scoped Internal v3 Worker Observation

**Files:**

- Create: `TiaMcpServer.OpennessWorker/Openness/ProjectTreeSnapshotWalker.cs`
- Modify: `TiaMcpServer.OpennessWorker/Program.cs`
- Modify: `TiaMcpServer/Worker/OpennessWorkerClient.cs`
- Modify: `TiaMcpServer.Contracts/OperationPolicyCatalog.cs`
- Modify: `TiaMcpServer.Contracts/WorkerProtocol.cs`
- Modify: `TiaMcpServer.FakeWorker/Program.cs`
- Modify: `TiaMcpServer.Tests/Project/ProjectTraversalSourceContractTests.cs`
- Modify: `TiaMcpServer.Tests/Project/ProjectStandaloneToolTests.cs`
- Modify: `TiaMcpServer.Tests/Worker/WorkerProtocolHandshakeTests.cs`
- Create: `TiaMcpServer.Tests/Project/ProjectTreeWorkerProtocolTests.cs`

**Interfaces:**

```csharp
public ProjectTreeSelectionResult WalkSnapshot(
    Project project,
    IReadOnlyList<ProjectTreeSelectorSegment>? startSelector,
    int? depth);

public Task<WorkerCallResult> BrowseProjectTreeV3SnapshotAsync(
    string? projectPath,
    IReadOnlyList<ProjectTreeSelectorSegment>? startSelector,
    int? depth);
```

- [ ] **Step 1: Add failing source-order, payload, and handshake tests**

  Assert that the selected-device path materializes `ProjectDeviceEnumerator.Enumerate(project)` before selecting, calls `WalkDevice` only for the selected device, and reaches `PlcSoftwareLocator.FindInDevice` only inside `WalkDevice`. Also assert that no emitted details dictionary writes `Path`.

  ```csharp
  [Fact]
  public void SelectedDevice_IsResolvedBeforeAnyPlcSoftwareDiscovery()
  {
      var source = File.ReadAllText(WorkerSource("Openness", "ProjectTreeSnapshotWalker.cs"));
      var enumerate = source.IndexOf("ProjectDeviceEnumerator.Enumerate(project)", StringComparison.Ordinal);
      var select = source.IndexOf("ProjectTreeDeviceSelector.Select", StringComparison.Ordinal);
      var walk = source.IndexOf("WalkDevice(selectedDevice)", StringComparison.Ordinal);

      Assert.True(enumerate >= 0 && select > enumerate && walk > select);
      Assert.DoesNotContain("[\"Path\"]", source, StringComparison.Ordinal);
      Assert.DoesNotContain("CombinePath", source, StringComparison.Ordinal);
  }

  [Fact]
  public void ProtocolRequiresTypedProjectTreeCapability()
  {
      Assert.Equal("project-tree-v3", WorkerProtocol.Version);
      Assert.Contains("typed-project-tree-selector", WorkerProtocol.RequiredCapabilities);
  }
  ```

- [ ] **Step 2: Run focused worker seam tests and confirm RED**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~ProjectTraversalSourceContractTests|FullyQualifiedName~ProjectTreeWorkerProtocolTests|FullyQualifiedName~WorkerProtocolHandshakeTests|FullyQualifiedName~ProjectStandaloneToolTests"
  ```

  Expected RED: there is no isolated typed v3 worker operation, the only walker traverses every device before filtering and emits `details.Path`, and the worker still advertises `project-binding-v1` without the typed-tree capability.

- [ ] **Step 3: Implement selected-device scoping and retain full-walk behavior below it**

  ```csharp
  public ProjectTreeSelectionResult WalkSnapshot(
      Project project,
      IReadOnlyList<ProjectTreeSelectorSegment>? startSelector,
      int? depth)
  {
      ProjectTreeNodeTypes.Validate(startSelector);
      var devices = ProjectDeviceEnumerator.Enumerate(project).Cast<Device>().ToList();

      if (startSelector is null)
      {
          var roots = devices.Select(WalkDevice).ToList();
          return ProjectTreeFilter.Apply(roots, startSelector: null, depth);
      }

      var selectedDevice = ProjectTreeDeviceSelector.Select(devices, device => device.Name, startSelector[0]);
      var selectedRoot = WalkDevice(selectedDevice);
      return ProjectTreeFilter.Apply(new List<ProjectTreeNode> { selectedRoot }, startSelector, depth);
  }
  ```

  Implement this in `ProjectTreeSnapshotWalker`. Keep its `WalkDevice` as the only v3 path into `PlcSoftwareLocator.FindInDevice`; do not teach the worker to navigate deeper Siemens groups directly in this issue. Do not modify the legacy `ProjectTreeWalker` behavior in PR 2.

- [ ] **Step 4: Remove presentation paths from worker nodes**

  Folder, software, tag-table, and type nodes with no remaining details should use `Details = null`. Block details retain only functional metadata.

  ```csharp
  var details = new Dictionary<string, string>
  {
      ["Number"] = block.Number.ToString(),
      ["ProgrammingLanguage"] = block.ProgrammingLanguage.ToString()
  };

  if (softwareUnitName is not null)
  {
      details["SoftwareUnit"] = softwareUnitName;
  }

  if (isSystemBlock)
  {
      details["IsSystemBlock"] = "true";
  }
  ```

- [ ] **Step 5: Return one typed worker result and preserve selector failures**

  ```csharp
  private static WorkerResponse BrowseProjectTreeV3Snapshot(WorkerRequest request)
  {
      try
      {
          ProjectTreeNodeTypes.Validate(request.StartSelector);
          return WithProject(request, project =>
          {
              var selected = new ProjectTreeSnapshotWalker().WalkSnapshot(project, request.StartSelector, request.Depth);
              return Success(new ProjectTreeBrowseResultInfo
              {
                  StartSelector = selected.CanonicalStartSelector,
                  Depth = request.Depth,
                  Roots = selected.Roots
              });
          });
      }
      catch (ProjectTreeSelectionException exception)
      {
          throw new WorkerOperationException(exception.Category, exception.Message);
      }
  }
  ```

  Dispatch this as a separate internal worker method named `browse_project_tree_v3_snapshot`, and register that exact method as `OperationCapability.Observe` in `OperationPolicyCatalog`. Add a matching client method that sets `StartSelector` and never sets `StartPath`:

  ```csharp
  public Task<WorkerCallResult> BrowseProjectTreeV3SnapshotAsync(
      string? projectPath = null,
      IReadOnlyList<ProjectTreeSelectorSegment>? startSelector = null,
      int? depth = null)
  {
      ProjectTreeNodeTypes.Validate(startSelector);
      return SendAsync(new WorkerRequest
      {
          Method = "browse_project_tree_v3_snapshot",
          ProjectPath = projectPath,
          StartSelector = startSelector?.ToList(),
          Depth = depth
      });
  }
  ```

  Retain the existing `browse_project_tree` worker dispatch, `BrowseProjectTreeAsync(... startPath ...)` client method, `WorkerRequest.StartPath`, and string-returning `ProjectReadTools.BrowseProjectTree` unchanged through PR 4. Add a regression test that invokes the public v2 tool and compares its request and response shape with the pre-PR fixture.

- [ ] **Step 6: Bump the exact worker handshake**

  ```csharp
  public const string Version = "project-tree-v3";

  public static readonly string[] RequiredCapabilities =
  {
      "expected-session-identity",
      "response-session-identity",
      "deterministic-project-selection",
      "typed-project-tree-selector"
  };
  ```

  Update FakeWorker hello behavior through the shared constants. Add a typed `browse_project_tree_v3_snapshot` fixture that returns `ProjectTreeBrowseResultInfo`; a stale worker must fail during hello before it can silently ignore `StartSelector`.

- [ ] **Step 7: Run worker tests, stub-build the seam, and stop at the commit boundary**

  Run the Step 2 command again and confirm GREEN, then:

  ```powershell
  dotnet build TiaMcpServer.sln -c Debug -m:1 --no-restore --disable-build-servers /p:UseTiaPortalReferenceStubs=true
  rg -n "browse_project_tree_v3_snapshot|BrowseProjectTreeV3Snapshot|startPath|StartPath|StartSelector|\[\"Path\"\]|CombinePath" TiaMcpServer.OpennessWorker TiaMcpServer/Worker/OpennessWorkerClient.cs TiaMcpServer.Contracts/WorkerRequest.cs
  git diff --check
  ```

  Expected search result: the new v3 dispatch and client path use only `StartSelector` and the path-free snapshot walker; `StartPath` and path construction remain confined to the unchanged legacy v2 path that Task 9 removes.

  Do not commit without explicit authorization. When authorized, use:

  ```powershell
  git add TiaMcpServer.OpennessWorker TiaMcpServer/Worker/OpennessWorkerClient.cs TiaMcpServer.Contracts/WorkerRequest.cs TiaMcpServer.Contracts/OperationPolicyCatalog.cs TiaMcpServer.Contracts/WorkerProtocol.cs TiaMcpServer.FakeWorker/Program.cs TiaMcpServer.Tests/Project TiaMcpServer.Tests/Worker/WorkerProtocolHandshakeTests.cs
  git commit -m "feat(project): scope worker project tree traversal"
  ```

---
### Task 4: Strictly Decode and Flatten Worker Snapshots

**Files:**

- Create: `TiaMcpServer/ProjectTree/ProjectTreeWorkerPayloadContract.cs`
- Create: `TiaMcpServer/ProjectTree/ProjectTreeFlattener.cs`
- Create: `TiaMcpServer.Tests/Project/ProjectTreeWorkerPayloadContractTests.cs`
- Create: `TiaMcpServer.Tests/Project/ProjectTreeFlattenerTests.cs`

**Interfaces:**

```csharp
internal sealed record ProjectTreeObservation(
    string ResolvedProjectPath,
    IReadOnlyList<ProjectTreeSelectorSegment>? CanonicalStartSelector,
    int? Depth,
    IReadOnlyList<ProjectTreeNode> Roots,
    IReadOnlyList<string> Warnings);

internal static ProjectTreeObservation Decode(
    WorkerCallResult workerResult,
    IReadOnlyList<ProjectTreeSelectorSegment>? requestedSelector,
    int? requestedDepth);

internal static IReadOnlyList<ProjectTreeFlatNode> Flatten(
    IReadOnlyList<ProjectTreeNode> roots);
```

- [ ] **Step 1: Write failing strict-payload tests**

  Accept one canonical `ProjectTreeBrowseResultInfo` payload and reject a bare legacy array, extra/missing/case-changed members, explicit-null roots, null nodes, unknown node types, blank names, `details.Path`, mismatched depth, and a canonical selector that is not semantically equivalent to the requested selector. Assert that failures contain no raw payload excerpt.

  ```csharp
  [Fact]
  public void Decode_LegacyBareArrayFailsClosedWithoutEchoingPayload()
  {
      var worker = WorkerCallResult.Ok("[{\"name\":\"secret-marker\",\"nodeType\":\"Device\"}]") with
      {
          ResolvedProjectPath = @"C:\Projects\Plant.ap21"
      };

      var error = Assert.Throws<ProjectTreeProtocolException>(() =>
          ProjectTreeWorkerPayloadContract.Decode(worker, requestedSelector: null, requestedDepth: null));

      Assert.Equal(WorkerFailureCategories.ProtocolError, error.Category);
      Assert.DoesNotContain("secret-marker", error.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void Decode_PreservesObservedSelectorCasing()
  {
      var requested = Selector(("Device", "plc_1"));
      var payload = Payload(Selector(("Device", "PLC_1")), depth: null, Node("PLC_1", "Device"));
      var observed = ProjectTreeWorkerPayloadContract.Decode(
          SuccessfulWorker(payload, @"C:\Projects\Plant.ap21"), requested, requestedDepth: null);

      Assert.Equal("PLC_1", Assert.Single(observed.CanonicalStartSelector!).Name);
  }
  ```

- [ ] **Step 2: Write failing flattening and selector-reconstruction tests**

  ```csharp
  [Fact]
  public void Flatten_AssignsContiguousPreorderAndParentFirstOpaqueIds()
  {
      var nodes = ProjectTreeFlattener.Flatten(SampleSelectedTree());

      Assert.Equal(Enumerable.Range(0, nodes.Count), nodes.Select(node => node.Sequence));
      Assert.Equal(nodes.Select(node => $"n{node.Sequence}"), nodes.Select(node => node.NodeId));
      Assert.All(nodes.Skip(1), node =>
          Assert.True(nodes.Single(parent => parent.NodeId == node.ParentNodeId).Sequence < node.Sequence));
  }

  [Fact]
  public void PagedFlatNodes_ReconstructTheOriginalTypedSelector()
  {
      var nodes = ProjectTreeFlattener.Flatten(SampleSelectedTree());
      var target = nodes.Single(node => node.Name == "Motor_1");
      var selector = ReconstructParentFirstSelector(nodes, target);

      Assert.Equal(
          new[] { "Device:PLC_1", "PlcSoftware:PLC_1", "BlockFolder:Program blocks", "FB:Motor_1" },
          selector.Select(segment => $"{segment.NodeType}:{segment.Name}"));
  }
  ```

  Also cover multiple roots, selected-root promotion to `parentNodeId: null`, empty details, copied dictionaries, empty trees, and a subtree whose parent and child land on different page slices.

- [ ] **Step 3: Run the host projection tests and confirm RED**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~ProjectTreeWorkerPayloadContractTests|FullyQualifiedName~ProjectTreeFlattenerTests"
  ```

  Expected RED: neither the strict decoder nor flat-node projector exists.

- [ ] **Step 4: Implement strict typed decoding**

  ```csharp
  internal static ProjectTreeObservation Decode(
      WorkerCallResult workerResult,
      IReadOnlyList<ProjectTreeSelectorSegment>? requestedSelector,
      int? requestedDepth)
  {
      if (!workerResult.Success)
      {
          throw new InvalidOperationException("Only successful worker results may enter the project-tree payload decoder.");
      }

      try
      {
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
              "The project-tree worker payload did not match its declared result contract and was rejected.");
      }
  }
  ```

  Validation recursively requires non-null nodes, known exact node types, nonblank names, string-valued details, and non-null child elements. It rejects a `Path` key with ordinal-ignore-case matching so a stale worker cannot leak the removed contract through casing changes.

- [ ] **Step 5: Implement pre-order flattening after filtering**

  ```csharp
  private static void Append(
      ProjectTreeNode source,
      string? parentNodeId,
      List<ProjectTreeFlatNode> destination)
  {
      var sequence = destination.Count;
      var nodeId = $"n{sequence}";
      destination.Add(new ProjectTreeFlatNode(
          nodeId,
          parentNodeId,
          sequence,
          source.Name,
          source.NodeType,
          new Dictionary<string, string>(source.Details ?? new Dictionary<string, string>())));

      foreach (var child in source.Children ?? Enumerable.Empty<ProjectTreeNode>())
      {
          Append(child, nodeId, destination);
      }
  }
  ```

  IDs remain opaque in the public documentation even though this first implementation uses `n` plus sequence internally.

- [ ] **Step 6: Run focused tests, inspect trust boundaries, and stop at the commit boundary**

  Run the Step 3 command again and confirm GREEN. Then verify only the strict gate reads the browse success payload:

  ```powershell
  rg -n "Deserialize<ProjectTreeBrowseResultInfo>|ProjectTreeBrowseResultInfo" TiaMcpServer TiaMcpServer.Tests/Project
  git diff --check
  ```

  The production deserialization match should be in `ProjectTreeWorkerPayloadContract.cs`; other production references should construct or pass typed values, not deserialize separately.

  Do not commit without explicit authorization. When authorized, use:

  ```powershell
  git add TiaMcpServer/ProjectTree/ProjectTreeWorkerPayloadContract.cs TiaMcpServer/ProjectTree/ProjectTreeFlattener.cs TiaMcpServer.Tests/Project/ProjectTreeWorkerPayloadContractTests.cs TiaMcpServer.Tests/Project/ProjectTreeFlattenerTests.cs
  git commit -m "feat(project): flatten typed project tree snapshots"
  ```

---

### Task 5: Make Transport-Failure Guidance Capability-Aware

**Files:**

- Modify: `TiaMcpServer/Worker/OpennessWorkerClient.cs`
- Modify: `TiaMcpServer.Tests/Worker/OpennessWorkerClientIntegrationTests.cs`
- Create: `TiaMcpServer.Tests/Worker/WorkerTransportFailureGuidanceTests.cs`

**Interfaces:**

```csharp
internal static string TimeoutGuidance(string? method);
internal static string CrashGuidance(string? method);
internal static bool IsSafeRead(string? method);
```

- [ ] **Step 1: Write failing category-matrix tests**

  Table-drive one operation from every `OperationCapability` and an unknown method. `Observe`, `TemporaryExport`, and `SafetyRead` use safe-read text; `Compile`, `ProjectLifecycle`, `ProjectMutation`, `OnlineControl`, null/blank, and unknown use state-affecting text. Include both the legacy public worker method and the internal v3 snapshot method as `Observe` operations while they coexist.

  ```csharp
  [Theory]
  [InlineData("browse_project_tree", true)]
  [InlineData("browse_project_tree_v3_snapshot", true)]
  [InlineData("get_block_content", true)]
  [InlineData("read_update_tag_safety_snapshot", true)]
  [InlineData("compile_check", false)]
  [InlineData("save_project", false)]
  [InlineData("update_tag", false)]
  [InlineData("start_plc", false)]
  [InlineData("unknown_method", false)]
  [InlineData("", false)]
  [InlineData(null, false)]
  public void SafetyClassificationComesFromOperationPolicy(string? method, bool expectedSafe)
      => Assert.Equal(expectedSafe, WorkerTransportFailureGuidance.IsSafeRead(method));
  ```

- [ ] **Step 2: Add failing FakeWorker timeout/crash integration tests**

  Assert the exact four approved messages, unchanged categories, invalidated verified binding, and no automatic second request. Cover one existing safe-read operation, `browse_project_tree_v3_snapshot`, one state-affecting operation, and an unknown method. Cached-continuation behavior is not available in PR 2 and remains covered by Task 9 after the pagination pipeline exists.

  ```csharp
  public const string SafeReadTimeout =
      "The TIA Openness worker did not complete the read before the timeout. No project or PLC runtime mutation was requested. The worker session was discarded; retrying the read is safe.";
  public const string SafeReadCrash =
      "The TIA Openness worker stopped before returning the read result. No project or PLC runtime mutation was requested. The worker will restart on the next worker call; retrying the read is safe.";
  public const string StateAffectingTimeout =
      "The TIA Openness worker timed out before completion was confirmed. The project or PLC runtime state may have changed. Inspect current state before retrying.";
  public const string StateAffectingCrash =
      "The TIA Openness worker stopped before completion was confirmed. The project or PLC runtime state may have changed. Inspect current state before retrying.";
  ```

- [ ] **Step 3: Run guidance tests and confirm RED**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~WorkerTransportFailureGuidanceTests|FullyQualifiedName~OpennessWorkerClientIntegrationTests"
  ```

  Expected RED: all transport failures still use the write-specific `InspectStateBeforeRetryGuidance` text.

- [ ] **Step 4: Implement the central capability mapping**

  ```csharp
  internal static bool IsSafeRead(string? method)
  {
      if (string.IsNullOrWhiteSpace(method))
      {
          return false;
      }

      return OperationPolicyCatalog.GetCapability(method) is
          OperationCapability.Observe or
          OperationCapability.TemporaryExport or
          OperationCapability.SafetyRead;
  }

  internal static string TimeoutGuidance(string? method)
      => IsSafeRead(method) ? SafeReadTimeout : StateAffectingTimeout;

  internal static string CrashGuidance(string? method)
      => IsSafeRead(method) ? SafeReadCrash : StateAffectingCrash;
  ```

  Use `request.Method` as the only input. A missing/blank/unknown method receives state-affecting guidance because `GetCapability` does not return a safe-read capability.

- [ ] **Step 5: Apply guidance without changing transport mechanics**

  ```csharp
  catch (TimeoutException)
  {
      InvalidateVerifiedBinding("the Openness worker timed out and its session outcome is unknown");
      return WorkerCallResult.Fail(
          WorkerFailureCategories.WorkerTimeout,
          WorkerTransportFailureGuidance.TimeoutGuidance(request.Method));
  }

  catch (Exception exception) when (exception is IOException or InvalidOperationException or JsonException)
  {
      InvalidateVerifiedBinding("the Openness worker crashed or its protocol stream was lost");
      return WorkerCallResult.Fail(
          WorkerFailureCategories.WorkerCrashed,
          WorkerTransportFailureGuidance.CrashGuidance(request.Method));
  }
  ```

  Preserve protocol-mismatch handling, worker disposal/restart behavior, and the absence of an automatic retry.

- [ ] **Step 6: Run focused tests, inspect all catch sites, and stop at the PR 2 boundary**

  Run the Step 3 command again and confirm GREEN. Then:

  ```powershell
  rg -n "WorkerTimeout|WorkerCrashed|TimeoutGuidance|CrashGuidance|InspectStateBeforeRetryGuidance" TiaMcpServer/Worker/OpennessWorkerClient.cs TiaMcpServer.Tests/Worker
  git diff --check
  ```

  Expected result: the old write-only guidance constant is removed and each transport category has one capability-aware message selection point. Run the per-PR CI invariant and confirm the public v2 regression test still passes.

  Do not commit without explicit authorization. When authorized, use:

  ```powershell
  git add TiaMcpServer/Worker/OpennessWorkerClient.cs TiaMcpServer.Tests/Worker/OpennessWorkerClientIntegrationTests.cs TiaMcpServer.Tests/Worker/WorkerTransportFailureGuidanceTests.cs
  git commit -m "fix(worker): clarify transport failure retry guidance"
  ```

---

## PR 3 — Authenticated Cursor Foundation

**Branch:** `feature/cursor-foundation`

**Base:** `feature/worker-observation`

**Exit condition:** Task 6 is complete, hardware cursor semantics remain covered, the public project-tree tool remains v2, and the cumulative branch passes the per-PR CI invariant.

### Task 6: Extract Shared Authenticated Cursor Protection

**Files:**

- Create: `TiaMcpServer/Cursors/AuthenticatedCursorProtector.cs`
- Modify: `TiaMcpServer/Network/HardwarePageCursorCodec.cs`
- Modify: `TiaMcpServer/Network/NetworkReadTools.cs`
- Modify: `TiaMcpServer/Program.cs`
- Create: `TiaMcpServer.Tests/Cursors/AuthenticatedCursorProtectorTests.cs`
- Modify: `TiaMcpServer.Tests/Network/HardwarePageCursorCodecTests.cs`
- Modify: `TiaMcpServer.Tests/Network/HardwarePaginationCoordinatorTests.cs`

**Interfaces:**

```csharp
internal enum AuthenticatedCursorStatus
{
    Success,
    ForeignProcess
}

internal sealed record AuthenticatedCursorResult<TState>(
    AuthenticatedCursorStatus Status,
    TState? State)
    where TState : class;

internal sealed class AuthenticatedCursorProtector : IDisposable
{
    internal static AuthenticatedCursorProtector CreateProcessScoped();
    internal AuthenticatedCursorProtector(byte[] key, string processInstanceId);
    internal string Protect<TState>(string purpose, TState state) where TState : class;
    internal AuthenticatedCursorResult<TState> Unprotect<TState>(string purpose, string cursor) where TState : class;
}
```

- [ ] **Step 1: Write failing generic-protector tests**

  Cover deterministic round-trip with a fixed 32-byte key and instance ID; payload and signature changes; incorrect purpose; strict unpadded Base64URL; noncanonical pad bits, member order, whitespace, escapes, duplicate/extra/missing members; invalid UTF-8; a 4,097-character input; current-process bad signature; foreign process classification; zeroing and rejection after disposal.

  ```csharp
  [Fact]
  public void PurposeSeparation_RejectsAValidCursorFromAnotherDomain()
  {
      using var protector = new AuthenticatedCursorProtector(TestKey, "process-a");
      var cursor = protector.Protect("hardware-page", new TestState("value"));

      Assert.Throws<AuthenticatedCursorException>(() =>
          protector.Unprotect<TestState>("project-tree", cursor));
  }

  [Fact]
  public void ForeignProcess_IsClassifiedBeforeDomainStateIsAccepted()
  {
      using var issuer = new AuthenticatedCursorProtector(TestKey, "process-a");
      using var reader = new AuthenticatedCursorProtector(OtherKey, "process-b");
      var cursor = issuer.Protect("project-tree", new TestState("value"));

      var result = reader.Unprotect<TestState>("project-tree", cursor);
      Assert.Equal(AuthenticatedCursorStatus.ForeignProcess, result.Status);
      Assert.Null(result.State);
  }
  ```

- [ ] **Step 2: Add failing hardware-regression tests against the shared protector**

  Preserve every existing hardware state validation and public category. A foreign process and a wrong purpose must both remain `invalid_cursor` for hardware pagination.

  ```csharp
  [Fact]
  public void Decode_ForeignProcessStillMapsToHardwareInvalidCursor()
  {
      using var issuer = new AuthenticatedCursorProtector(TestKey, "process-a");
      using var reader = new AuthenticatedCursorProtector(OtherKey, "process-b");
      var cursor = new HardwarePageCursorCodec(issuer).Encode(ValidState());

      var error = Assert.Throws<HardwarePageCursorException>(() =>
          new HardwarePageCursorCodec(reader).Decode(cursor));
      Assert.Equal(WorkerFailureCategories.InvalidCursor, error.Category);
  }
  ```

- [ ] **Step 3: Run cursor tests and confirm RED**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~AuthenticatedCursorProtectorTests|FullyQualifiedName~HardwarePageCursorCodecTests|FullyQualifiedName~HardwarePaginationCoordinatorTests"
  ```

  Expected RED: authentication, framing, and process identity are still embedded solely in `HardwarePageCursorCodec`.

- [ ] **Step 4: Implement the strict generic envelope and signing lifecycle**

  The canonical signed payload has exactly `formatVersion`, `processInstanceId`, `purpose`, and `state`; the protection format version is `1`.

  ```csharp
  private sealed record Envelope<TState>(
      int FormatVersion,
      string ProcessInstanceId,
      string Purpose,
      TState State);

  internal string Protect<TState>(string purpose, TState state) where TState : class
  {
      ThrowIfDisposed();
      ValidatePurpose(purpose);
      var payload = Encoding.UTF8.GetBytes(CanonicalJson.Serialize(
          new Envelope<TState>(1, _processInstanceId, purpose, state)));
      var signature = ComputeSignature(payload);
      return $"{EncodeBase64Url(payload)}.{EncodeBase64Url(signature)}";
  }
  ```

  `Unprotect` enforces the total input limit before splitting, validates exact canonical framing, and inspects the canonical process ID. A different well-formed process ID returns `ForeignProcess` without exposing state; a cursor claiming the current process must pass fixed-time HMAC comparison and exact purpose validation. `Dispose` uses `CryptographicOperations.ZeroMemory(_key)` and all later calls throw `ObjectDisposedException`.

- [ ] **Step 5: Reduce `HardwarePageCursorCodec` to domain validation**

  ```csharp
  private const string Purpose = "hardware-page";
  private readonly AuthenticatedCursorProtector _protector;

  internal string Encode(HardwarePageCursorState state)
  {
      ValidateState(state);
      return _protector.Protect(Purpose, state);
  }

  internal HardwarePageCursorState Decode(string cursor)
  {
      try
      {
          var result = _protector.Unprotect<HardwarePageCursorState>(Purpose, cursor);
          if (result.Status != AuthenticatedCursorStatus.Success || result.State is null)
          {
              throw new HardwarePageCursorException();
          }

          ValidateState(result.State);
          return result.State;
      }
      catch (Exception exception) when (exception is AuthenticatedCursorException or JsonException or ArgumentException)
      {
          throw new HardwarePageCursorException();
      }
  }
  ```

  Retain all existing project/binding/query/snapshot/offset checks and their tests. Delete only the duplicated key, HMAC, Base64URL, and canonical-envelope mechanics.

- [ ] **Step 6: Register one production protector and preserve the compatibility path**

  ```csharp
  builder.Services.AddSingleton(_ => AuthenticatedCursorProtector.CreateProcessScoped());
  builder.Services.AddSingleton(sp => new HardwarePageCursorCodec(
      sp.GetRequiredService<AuthenticatedCursorProtector>()));
  ```

  Keep `NetworkReadTools.CompatibilityExecutors` because direct static tool tests still rely on it, but do not use its static compatibility codec in production registration and do not add another `ConditionalWeakTable` or static cache.

- [ ] **Step 7: Run cursor tests, inspect the migration, and stop at the commit boundary**

  Run the Step 3 command again and confirm GREEN. Then:

  ```powershell
  rg -n "HMACSHA256|FixedTimeEquals|RandomNumberGenerator.Fill|EncodeBase64Url|DecodeBase64Url" TiaMcpServer
  git diff --check
  ```

  Host HMAC/framing implementation matches should be confined to `AuthenticatedCursorProtector.cs`; `HardwarePageCursorCodec.cs` retains only domain checks and calls into the protector.

  Do not commit without explicit authorization. When authorized, use:

  ```powershell
  git add TiaMcpServer/Cursors TiaMcpServer/Network/HardwarePageCursorCodec.cs TiaMcpServer/Network/NetworkReadTools.cs TiaMcpServer/Program.cs TiaMcpServer.Tests/Cursors TiaMcpServer.Tests/Network/HardwarePageCursorCodecTests.cs TiaMcpServer.Tests/Network/HardwarePaginationCoordinatorTests.cs
  git commit -m "refactor(cursors): share authenticated cursor protection"
  ```

---

## PR 4 — Bounded Project-Tree Pagination

**Branch:** `feature/tree-pagination`

**Base:** `feature/cursor-foundation`

**Exit condition:** Tasks 7-8 are complete, the cursor/cache/projector pipeline is fully tested without MCP exposure, the public project-tree tool remains v2, and the cumulative branch passes the per-PR CI invariant.

### Task 7: Add the Project-Tree Cursor and Bounded Snapshot Store

**Files:**

- Create: `TiaMcpServer/ProjectTree/ProjectTreeBrowseRequest.cs`
- Create: `TiaMcpServer/ProjectTree/ProjectTreeCursorCodec.cs`
- Create: `TiaMcpServer/ProjectTree/ProjectTreeSnapshotStore.cs`
- Create: `TiaMcpServer.Tests/Project/ProjectTreeBrowseRequestTests.cs`
- Create: `TiaMcpServer.Tests/Project/ProjectTreeCursorCodecTests.cs`
- Create: `TiaMcpServer.Tests/Project/ProjectTreeSnapshotStoreTests.cs`

**Interfaces:**

```csharp
internal sealed record ProjectTreeCursorState(
    string SnapshotId,
    string QueryHash,
    int Offset);

internal sealed record ProjectTreeSnapshotContent(
    ProjectTreeQuery Query,
    IReadOnlyList<ProjectTreeFlatNode> Nodes);

internal sealed record ProjectTreeBrowseRequest(
    string? ProjectPath = null,
    IReadOnlyList<ProjectTreeSelectorSegment>? StartSelector = null,
    int? Depth = null,
    int? PageSize = null,
    string? Cursor = null);

internal sealed record ProjectTreeSnapshotCandidate(
    string SnapshotId,
    DateTimeOffset CreatedAt,
    string QueryHash,
    ProjectTreeSnapshotContent Content,
    IReadOnlyList<string> Warnings,
    int SerializedChars);

internal sealed record ProjectTreeSnapshotAccess<T>(bool Found, T? Value);
```

- [ ] **Step 1: Write failing request and cursor tests**

  Request tests cover page-size default/bounds, `depth >= 1`, empty/blank cursor handling, semantic comparison of repeated project paths and selector names, exact node-type/depth matching, and exclusion of `pageSize` from query identity.

  Cursor tests cover round-trip, exact state members, blank snapshot ID, non-lowercase/non-64-character query hash, negative offset, wrong purpose, tampering, 4,096-character protection limit, current-process invalid classification, and foreign-process `snapshot_unavailable`.

  ```csharp
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
  public void RepeatedSelector_AllowsEquivalentNameCasingButNotNodeTypeCasing()
  {
      var query = new ProjectTreeQuery(ProjectPath, Selector(("Device", "PLC_1")), Depth: 2);
      Assert.Null(new ProjectTreeBrowseRequest(
          ProjectPath.ToLowerInvariant(), Selector(("Device", "plc_1")), Depth: 2).ValidateRepeatedQuery(query));
      Assert.NotNull(new ProjectTreeBrowseRequest(
          StartSelector: Selector(("device", "PLC_1"))).ValidateRepeatedQuery(query));
  }
  ```

- [ ] **Step 2: Write failing TTL, LRU, replay, concurrency, and disposal tests**

  Use a local `ManualTimeProvider : TimeProvider` and an injected deterministic snapshot-ID factory. Configure small limits in tests to cover:

  - expiry removal before lookup and insertion;
  - sliding renewal only after a successful page callback;
  - no renewal for filter, range, item, or metadata failures;
  - successful cursor replay returning the same range and renewing again;
  - one-page initial success not retained;
  - multi-page final access retained;
  - count and aggregate-character eviction;
  - deterministic LRU ties;
  - a blocked page callback preventing concurrent eviction of that entry;
  - disposal clearing entries and rejecting every later method.

  ```csharp
  [Fact]
  public void FailedAccess_DoesNotRenewSlidingExpiry()
  {
      var clock = new ManualTimeProvider(Instant);
      using var store = SmallStore(clock);
      store.ProjectInitial(Candidate("a", chars: 100), _ => MultiPageSuccess(), result => result.HasNextPage);
      clock.Advance(TimeSpan.FromMinutes(9));

      var access = store.Access("a", _ => ProjectionFailure(), result => result.IsSuccess);
      Assert.True(access.Found);
      clock.Advance(TimeSpan.FromMinutes(2));

      Assert.False(store.Access("a", _ => MultiPageSuccess(), result => result.IsSuccess).Found);
  }

  [Fact]
  public async Task EntryCannotBeEvictedWhileItsPageIsProjected()
  {
      using var entered = new ManualResetEventSlim();
      using var release = new ManualResetEventSlim();
      using var store = SmallStore(new ManualTimeProvider(Instant));
      store.ProjectInitial(Candidate("a", 100), _ => MultiPageSuccess(), result => result.HasNextPage);

      var access = Task.Run(() => store.Access("a", _ =>
      {
          entered.Set();
          release.Wait();
          return MultiPageSuccess();
      }, result => result.IsSuccess));
      entered.Wait();
      var insertion = Task.Run(() => store.ProjectInitial(
          Candidate("b", 100), _ => MultiPageSuccess(), result => result.HasNextPage));

      Assert.False(insertion.Wait(TimeSpan.FromMilliseconds(100)));
      release.Set();
      await Task.WhenAll(access, insertion);
  }
  ```

- [ ] **Step 3: Run cursor/store tests and confirm RED**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~ProjectTreeBrowseRequestTests|FullyQualifiedName~ProjectTreeCursorCodecTests|FullyQualifiedName~ProjectTreeSnapshotStoreTests"
  ```

  Expected RED: the project-tree cursor, request identity rules, and snapshot store do not exist.

- [ ] **Step 4: Implement the exact three-field domain cursor**

  ```csharp
  private const string Purpose = "project-tree";

  internal ProjectTreeCursorState Decode(string cursor)
  {
      try
      {
          var result = _protector.Unprotect<ProjectTreeCursorState>(Purpose, cursor);
          if (result.Status == AuthenticatedCursorStatus.ForeignProcess)
          {
              throw new ProjectTreeCursorException(WorkerFailureCategories.SnapshotUnavailable);
          }

          Validate(result.State);
          return result.State!;
      }
      catch (AuthenticatedCursorException)
      {
          throw new ProjectTreeCursorException(WorkerFailureCategories.InvalidCursor);
      }
  }
  ```

  State validation accepts a nonblank bounded snapshot ID, a lowercase 64-character SHA-256 query hash, and a nonnegative offset. Do not add project path, selectors, nodes, page size, timestamps, or worker identity to the cursor.

- [ ] **Step 5: Implement validated request semantics and query hashing**

  ```csharp
  internal int ResolvePageSize()
  {
      var value = PageSize ?? ProjectTreeContract.DefaultPageSize;
      if (value is < ProjectTreeContract.MinimumPageSize or > ProjectTreeContract.MaximumPageSize)
      {
          throw new ProjectTreeRequestException(
              WorkerFailureCategories.ValidationError,
              "pageSize must be between 1 and 200.");
      }

      return value;
  }

  internal static string CreateQueryHash(ProjectTreeQuery query)
  {
      var bytes = Encoding.UTF8.GetBytes(CanonicalJson.Serialize(query));
      return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
  }
  ```

  For continuation matching, compare canonical project paths ordinal-ignore-case, selector node types ordinal, selector names ordinal-ignore-case, and depth exactly when the caller repeats each non-null field. Never include `pageSize` in `ProjectTreeQuery` or its hash.

- [ ] **Step 6: Implement the lock-owned store lifecycle**

  ```csharp
  internal T ProjectInitial<T>(
      ProjectTreeSnapshotCandidate candidate,
      Func<ProjectTreeSnapshotView, T> project,
      Predicate<T> shouldCache)
  {
      lock (_gate)
      {
          ThrowIfDisposed();
          var now = _timeProvider.GetUtcNow();
          RemoveExpired(now);
          var entry = ProjectTreeSnapshotEntry.From(candidate, now + _slidingTtl);
          var result = project(entry.View());
          if (shouldCache(result))
          {
              InsertAndEvict(entry, now);
          }

          return result;
      }
  }

  internal ProjectTreeSnapshotAccess<T> Access<T>(
      string snapshotId,
      Func<ProjectTreeSnapshotView, T> project,
      Predicate<T> renewOnSuccess)
  {
      lock (_gate)
      {
          ThrowIfDisposed();
          var now = _timeProvider.GetUtcNow();
          RemoveExpired(now);
          if (!_entries.TryGetValue(snapshotId, out var entry))
          {
              return new ProjectTreeSnapshotAccess<T>(false, default);
          }

          var prospectiveExpiry = now + _slidingTtl;
          var result = project(entry.View(prospectiveExpiry));
          if (renewOnSuccess(result))
          {
              entry.MarkSuccessfulAccess(now, prospectiveExpiry);
          }

          return new ProjectTreeSnapshotAccess<T>(true, result);
      }
  }
  ```

  Production defaults are `maxSnapshots: 4`, `maxSnapshotChars: 4_000_000`, `maxAggregateChars: 16_000_000`, and `slidingTtl: TimeSpan.FromMinutes(10)`. Snapshot measurement and flattening happen before `ProjectInitial`; the store rejects an internally inconsistent candidate rather than reserializing it under the lock.

- [ ] **Step 7: Implement deterministic eviction and disposal**

  ```csharp
  private ProjectTreeSnapshotEntry OldestEntry()
      => _entries.Values
          .OrderBy(entry => entry.LastSuccessfulAccess)
          .ThenBy(entry => entry.CreatedAt)
          .ThenBy(entry => entry.SnapshotId, StringComparer.Ordinal)
          .First();

  public void Dispose()
  {
      lock (_gate)
      {
          if (_disposed) return;
          _entries.Clear();
          _aggregateChars = 0;
          _disposed = true;
      }
  }
  ```

  Evict expired entries first, then repeatedly remove `OldestEntry()` until both count and aggregate limits permit insertion. Retain a cached entry after its final successful page.

- [ ] **Step 8: Run focused tests, review invariants, and stop at the commit boundary**

  Run the Step 3 command again and confirm GREEN. Then:

  ```powershell
  git diff --check
  rg -n "lock \(_gate\)|4_000_000|16_000_000|FromMinutes\(10\)|maxSnapshots" TiaMcpServer/ProjectTree/ProjectTreeSnapshotStore.cs
  ```

  Confirm worker calls, flattening, snapshot serialization, and full-snapshot hashing are absent from the locked store methods.

  Do not commit without explicit authorization. When authorized, use:

  ```powershell
  git add TiaMcpServer/ProjectTree/ProjectTreeBrowseRequest.cs TiaMcpServer/ProjectTree/ProjectTreeCursorCodec.cs TiaMcpServer/ProjectTree/ProjectTreeSnapshotStore.cs TiaMcpServer.Tests/Project/ProjectTreeBrowseRequestTests.cs TiaMcpServer.Tests/Project/ProjectTreeCursorCodecTests.cs TiaMcpServer.Tests/Project/ProjectTreeSnapshotStoreTests.cs
  git commit -m "feat(project): cache bounded tree snapshots"
  ```

---

### Task 8: Project the Largest Exact Canonical Page

**Files:**

- Create: `TiaMcpServer/ProjectTree/ProjectTreePageProjector.cs`
- Modify: `TiaMcpServer/Tools/StructuredToolResult.cs`
- Create: `TiaMcpServer.Tests/Project/ProjectTreePageProjectorTests.cs`
- Create: `TiaMcpServer.Tests/Tools/StructuredToolResultTests.cs`

**Interfaces:**

```csharp
internal sealed record ProjectTreeRenderedResponse(
    BrowseProjectTreeResponse Response,
    string CanonicalText,
    bool IsSuccess,
    bool HasNextPage);

internal ProjectTreeRenderedResponse Project(
    ProjectTreeSnapshotView snapshot,
    int offset,
    int requestedPageSize);

internal static CallToolResult CreateCanonical(string canonicalText, bool isError);
```

- [ ] **Step 1: Write failing exact-page tests**

  Use small injected response limits to cover full pages, budget-reduced pages, changed continuation page size, empty snapshots, final pages, cursor offset advancing by actual returned count, and parent/child page splits. Assert every successful response is at or below the configured character limit and contains only complete node objects.

  ```csharp
  [Fact]
  public void Project_TrimsOnlyTheTrailingSuffixAndAdvancesByReturnedCount()
  {
      var projector = CreateProjector(maxResponseChars: 850);
      var snapshot = SnapshotWithNodes(10, detailChars: 120);

      var page = projector.Project(snapshot, offset: 2, requestedPageSize: 5);

      Assert.True(page.IsSuccess);
      Assert.InRange(page.CanonicalText.Length, 1, 850);
      Assert.Equal(new[] { 2, 3 }, page.Response.Result!.Nodes.Select(node => node.Sequence));
      Assert.Equal(2, page.Response.Result.Pagination.ReturnedCount);
      Assert.Equal(4, Decode(page.Response.Result.Pagination.NextCursor!).Offset);
  }
  ```

- [ ] **Step 2: Add failing failure-precedence and attempt-bound tests**

  Cover `cursor_out_of_range`; metadata-only overflow; one next node overflow after metadata fits; warnings contributing to metadata size; no node/diagnostic echo in bounded failures; and a maximum of 200 exact serialization attempts. Use an injected serialization observer only to count calls, never to estimate size.

  ```csharp
  [Fact]
  public void OversizedNextNode_ReturnsItemFailureWithoutEchoingTheNode()
  {
      var marker = new string('s', 2_000);
      var projector = CreateProjector(maxResponseChars: 700);

      var page = projector.Project(SnapshotWithSingleNode(marker), 0, 1);

      Assert.False(page.IsSuccess);
      Assert.Equal(WorkerFailureCategories.ResultItemTooLarge, page.Response.Failure!.Category);
      Assert.DoesNotContain(marker, page.CanonicalText, StringComparison.Ordinal);
      Assert.True(page.CanonicalText.Length <= 700);
  }

  [Fact]
  public void ProjectionUsesNoMoreThanTwoHundredExactSerializations()
  {
      var attempts = 0;
      var projector = CreateProjector(
          maxResponseChars: 900,
          observeSerialization: _ => attempts++);

      projector.Project(SnapshotWithNodes(500, detailChars: 300), 0, 200);
      Assert.InRange(attempts, 1, 200);
  }
  ```

- [ ] **Step 3: Add a failing final-serialization reuse test**

  ```csharp
  [Fact]
  public void CreateCanonical_UsesTheAcceptedTextForBothMcpRepresentations()
  {
      const string canonical = "{\"contractVersion\":\"3.0\",\"failure\":null,\"result\":null,\"status\":\"succeeded\",\"warnings\":[]}";
      var result = StructuredToolResult.CreateCanonical(canonical, isError: false);

      Assert.Equal(canonical, Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
      Assert.Equal(canonical, result.StructuredContent!.Value.GetRawText());
  }
  ```

- [ ] **Step 4: Run page tests and confirm RED**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~ProjectTreePageProjectorTests|FullyQualifiedName~StructuredToolResultTests"
  ```

  Expected RED: exact project-tree page projection and an already-canonical result path do not exist.

- [ ] **Step 5: Implement exact prefix search and failure precedence**

  First serialize the largest requested prefix. If it does not fit, serialize a metadata-only continuation candidate to distinguish `result_metadata_too_large`, then search only complete prefixes for the largest fit. All partial candidates carry a cursor; an all-remaining candidate carries `nextCursor: null`.

  ```csharp
  private ProjectTreeRenderedResponse TryPage(
      ProjectTreeSnapshotView snapshot,
      int offset,
      int requestedPageSize,
      int take)
  {
      var end = checked(offset + take);
      var nextCursor = end < snapshot.Content.Nodes.Count
          ? _cursorCodec.Encode(new ProjectTreeCursorState(snapshot.SnapshotId, snapshot.QueryHash, end))
          : null;
      var response = Success(
          snapshot,
          new ProjectTreePagination(offset, requestedPageSize, take, nextCursor),
          snapshot.Content.Nodes.Skip(offset).Take(take).ToArray());
      var text = CanonicalJson.Serialize(response);
      _observeSerialization?.Invoke(text.Length);
      return new ProjectTreeRenderedResponse(response, text, text.Length <= _maxResponseChars, nextCursor is not null);
  }
  ```

  After the largest candidate fails, perform an exact bounded prefix search; every candidate is a prefix and no estimate participates. A binary search among non-final prefixes keeps the total far below 200 while still returning the largest fitting prefix. If metadata fits but prefix size one does not, return `result_item_too_large`. If there are no nodes, return one empty success only when its metadata fits.

- [ ] **Step 6: Return small canonical failures without renewing state**

  ```csharp
  private ProjectTreeRenderedResponse Failure(string category, string message)
  {
      var response = new BrowseProjectTreeResponse(
          ProjectTreeContract.Version,
          ProjectTreeStatuses.Failed,
          Result: null,
          new BrowseProjectTreeFailure(category, message),
          Warnings: Array.Empty<string>());
      var text = CanonicalJson.Serialize(response);
      return new ProjectTreeRenderedResponse(response, text, IsSuccess: false, HasNextPage: false);
  }
  ```

  Failure text is fixed and does not include the oversized node, selector, cursor, payload, or warnings. Treat an offset other than zero for an empty snapshot, or an offset outside `[0, totalNodes - 1]` for a nonempty snapshot, as `cursor_out_of_range`.

- [ ] **Step 7: Reuse the accepted canonical text in the MCP result**

  ```csharp
  internal static CallToolResult CreateCanonical(string canonicalText, bool isError)
  {
      using var document = JsonDocument.Parse(canonicalText);
      var structured = document.RootElement.Clone();
      return new CallToolResult
      {
          Content = new List<ContentBlock> { new TextContentBlock { Text = canonicalText } },
          StructuredContent = structured,
          IsError = isError
      };
  }
  ```

- [ ] **Step 8: Run focused tests, inspect budgets, and stop at the commit boundary**

  Run the Step 4 command again and confirm GREEN. Then:

  ```powershell
  git diff --check
  rg -n "60_000|MaximumResponseChars|200|CanonicalJson.Serialize" TiaMcpServer/ProjectTree/ProjectTreePageProjector.cs TiaMcpServer/Tools/StructuredToolResult.cs
  ```

  Confirm no string truncation, estimated node sizing, JSON fragments, or secondary final response serialization was introduced.

  Do not commit without explicit authorization. When authorized, use:

  ```powershell
  git add TiaMcpServer/ProjectTree/ProjectTreePageProjector.cs TiaMcpServer/Tools/StructuredToolResult.cs TiaMcpServer.Tests/Project/ProjectTreePageProjectorTests.cs TiaMcpServer.Tests/Tools/StructuredToolResultTests.cs
  git commit -m "feat(project): project exact tree pages"
  ```

---

## PR 5 — Public v3 Cutover and Release Integration

**Branch:** `feature/v3-cutover`

**Base:** `feature/tree-pagination`

**Exit condition:** Tasks 9-11 are complete, the legacy public/worker path has been removed atomically, all maintained documentation describes only v3, and the cumulative branch passes every offline, stub, coverage, and package gate.

### Task 9: Wire Initial Observation and Cached Continuation End to End

**Files:**

- Create: `TiaMcpServer/ProjectTree/ProjectTreeBrowseCoordinator.cs`
- Modify: `TiaMcpServer/Tools/ProjectReadTools.cs`
- Modify: `TiaMcpServer/Worker/OpennessWorkerClient.cs`
- Modify: `TiaMcpServer.OpennessWorker/Program.cs`
- Modify: `TiaMcpServer.Contracts/WorkerRequest.cs`
- Delete: `TiaMcpServer.OpennessWorker/Openness/ProjectTreeWalker.cs`
- Modify: `TiaMcpServer/Program.cs`
- Modify: `TiaMcpServer.FakeWorker/Program.cs`
- Modify: `TiaMcpServer.Tests/Project/ProjectStandaloneToolTests.cs`
- Modify: `TiaMcpServer.Tests/Tools/McpToolSchemaTests.cs`
- Create: `TiaMcpServer.Tests/Project/ProjectTreeBrowseCoordinatorTests.cs`
- Create: `TiaMcpServer.Tests/Project/ProjectTreeStructuredProtocolTests.cs`

**Interfaces:**

```csharp
public sealed class ProjectTreeBrowseCoordinator
{
    internal async Task<ProjectTreeRenderedResponse> BrowseAsync(
        ProjectTreeBrowseRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var pageSize = request.ResolvePageSize();
            return request.Cursor is null
                ? await BrowseInitialAsync(request, pageSize).ConfigureAwait(false)
                : BrowseContinuation(request, pageSize);
        }
        catch (ProjectTreeRequestException exception)
        {
            return Failure(exception.Category, exception.Message);
        }
        catch (ProjectTreeSelectionException exception)
        {
            return Failure(exception.Category, exception.Message);
        }
        catch (ProjectTreeCursorException exception)
        {
            return Failure(exception.Category, exception.Message);
        }
        catch (ProjectTreeProtocolException exception)
        {
            return Failure(exception.Category, exception.Message);
        }
    }
}

public static Task<CallToolResult> BrowseProjectTree(
    ProjectTreeBrowseCoordinator coordinator,
    string? projectPath = null,
    ProjectTreeSelectorSegment[]? startSelector = null,
    int? depth = null,
    int? pageSize = null,
    string? cursor = null);
```

- [ ] **Step 1: Write failing MCP schema and protocol tests**

  Assert exact public inputs, `additionalProperties: false`, strict selector item shape, removed `startPath`/`deviceName`, declared output schema, read-only annotations, envelope nullability, `content[0].text == structuredContent.GetRawText()`, and no nested JSON payload string.

  ```csharp
  [Fact]
  public void BrowseProjectTree_SchemaIsTheExactV3Surface()
  {
      var schema = ToolSchema(nameof(ProjectReadTools.BrowseProjectTree));
      Assert.Equal(
          new[] { "cursor", "depth", "pageSize", "projectPath", "startSelector" },
          schema.GetProperty("properties").EnumerateObject().Select(property => property.Name).Order().ToArray());
      Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
      Assert.False(schema.GetProperty("properties").TryGetProperty("startPath", out _));
      Assert.False(schema.GetProperty("properties").TryGetProperty("deviceName", out _));
  }

  [Fact]
  public async Task StructuredResult_UsesOneCanonicalDocument()
  {
      var result = await CallBrowseFixture("project-tree-v3-small");
      var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
      Assert.Equal(text, result.StructuredContent!.Value.GetRawText());
      Assert.Equal(JsonValueKind.Array, result.StructuredContent.Value.GetProperty("result").GetProperty("nodes").ValueKind);
  }
  ```

- [ ] **Step 2: Write failing orchestration and FakeWorker tests**

  Cover:

  - one initial worker call and zero calls across all continuation pages;
  - every cursor-free request observing afresh, including identical concurrent requests;
  - default page size 100 for initial and continuation;
  - changing continuation page size;
  - repeated equivalent query fields accepted and differences rejected before projection;
  - one-page results not retained and multi-page results retained after the final page;
  - cursor replay returning the same sequences;
  - continuation after a worker crash;
  - host restart/foreign process, expiry, eviction, malformed cursor, mismatch, and range failures;
  - `snapshot_too_large`, item, metadata, target, and protocol failures;
  - malformed FakeWorker payloads never echoed.

  ```csharp
  [Fact]
  public async Task ContinuationsUseTheCachedSnapshotAfterWorkerCrash()
  {
      var fixture = CreatePersistentFakeWorkerFixture("project-tree-v3-one-shot");
      var first = await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(PageSize: 2));
      var cursor = first.Response.Result!.Pagination.NextCursor!;
      await fixture.CrashWorkerAsync();

      var second = await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(Cursor: cursor));

      Assert.True(second.IsSuccess);
      Assert.Equal(new[] { 2, 3 }, second.Response.Result!.Nodes.Select(node => node.Sequence));
      Assert.Equal(1, fixture.BrowseWorkerCalls);
  }

  [Fact]
  public async Task TwoCursorFreeCallsAlwaysObserveTwice()
  {
      var fixture = CreatePersistentFakeWorkerFixture("project-tree-v3-counted");
      await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(PageSize: 1));
      await fixture.Coordinator.BrowseAsync(new ProjectTreeBrowseRequest(PageSize: 1));
      Assert.Equal(2, fixture.BrowseWorkerCalls);
  }
  ```

- [ ] **Step 3: Run integration tests and confirm RED**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~ProjectTreeBrowseCoordinatorTests|FullyQualifiedName~ProjectTreeStructuredProtocolTests|FullyQualifiedName~ProjectStandaloneToolTests|FullyQualifiedName~McpToolSchemaTests"
  ```

  Expected RED: the MCP tool still returns `Task<string>` and has no output schema, coordinator, cache, page-size, cursor, or structured-content path.

- [ ] **Step 4: Implement the cursor-free coordinator path**

  ```csharp
  private async Task<ProjectTreeRenderedResponse> BrowseInitialAsync(
      ProjectTreeBrowseRequest request,
      int pageSize)
  {
      ProjectTreeNodeTypes.Validate(request.StartSelector);
      var worker = await _workerClient.BrowseProjectTreeV3SnapshotAsync(
          request.ProjectPath,
          request.StartSelector,
          request.Depth).ConfigureAwait(false);
      if (!worker.Success)
      {
          return Failure(
              worker.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
              worker.Error ?? "The project-tree worker operation failed.",
              worker.Warnings);
      }

      var observation = ProjectTreeWorkerPayloadContract.Decode(
          worker, request.StartSelector, request.Depth);
      var query = new ProjectTreeQuery(
          observation.ResolvedProjectPath,
          observation.CanonicalStartSelector,
          observation.Depth);
      var nodes = ProjectTreeFlattener.Flatten(observation.Roots);
      var content = new ProjectTreeSnapshotContent(query, nodes);
      var chars = CanonicalJson.Serialize(content).Length;
      if (chars > ProjectTreeSnapshotStore.DefaultMaxSnapshotChars)
      {
          return Failure(WorkerFailureCategories.SnapshotTooLarge, "The filtered project-tree snapshot exceeds the 4,000,000-character limit.");
      }

      var candidate = ProjectTreeSnapshotCandidate.Create(query, nodes, observation.Warnings, chars, _timeProvider);
      return _store.ProjectInitial(
          candidate,
          view => _projector.Project(view, 0, pageSize),
          projection => projection.IsSuccess && projection.HasNextPage);
  }
  ```

  Worker IPC, decoding, flattening, query hashing, and full-snapshot measurement all occur before the store lock.

- [ ] **Step 5: Implement the cursor-authoritative continuation path**

  ```csharp
  private ProjectTreeRenderedResponse BrowseContinuation(
      ProjectTreeBrowseRequest request,
      int pageSize)
  {
      var state = _cursorCodec.Decode(request.Cursor!);
      var access = _store.Access(
          state.SnapshotId,
          snapshot =>
          {
              if (!string.Equals(state.QueryHash, snapshot.QueryHash, StringComparison.Ordinal))
              {
                  return Failure(WorkerFailureCategories.CursorFilterMismatch, "The cursor query does not match the cached snapshot.");
              }

              var mismatch = request.ValidateRepeatedQuery(snapshot.Content.Query);
              if (mismatch is not null)
              {
                  return Failure(WorkerFailureCategories.CursorFilterMismatch, mismatch);
              }

              return _projector.Project(snapshot, state.Offset, pageSize);
          },
          projection => projection.IsSuccess);

      return access.Found
          ? access.Value!
          : Failure(WorkerFailureCategories.SnapshotUnavailable, "The project-tree snapshot is no longer available; start again without a cursor.");
  }
  ```

  Catch request, cursor, selector, protocol, and store exceptions once at the coordinator boundary and map each category without including untrusted input. Do not call `_workerClient` anywhere in the continuation branch.

- [ ] **Step 6: Remove the legacy seam, expose the typed MCP tool, and reuse final text**

  Remove the old `browse_project_tree` worker dispatch, the `BrowseProjectTreeAsync(... startPath ...)` client overload, `WorkerRequest.StartPath`, the legacy `ProjectTreeWalker`, and the string-returning public tool in the same change that installs the v3 tool below. Keep `browse_project_tree_v3_snapshot` as the internal worker operation used by the final public tool. Do not leave a compatibility flag, alias, or hidden `startPath` route.

  ```csharp
  [McpServerTool(
      Name = "browse_project_tree",
      ReadOnly = true,
      Destructive = false,
      OpenWorld = false,
      UseStructuredContent = true,
      OutputSchemaType = typeof(BrowseProjectTreeResponse))]
  [Description("Browse a point-in-time TIA project tree through typed, bounded, resumable flat-node pages.")]
  public static async Task<CallToolResult> BrowseProjectTree(
      ProjectTreeBrowseCoordinator coordinator,
      string? projectPath = null,
      ProjectTreeSelectorSegment[]? startSelector = null,
      int? depth = null,
      int? pageSize = null,
      string? cursor = null)
  {
      var rendered = await coordinator.BrowseAsync(
          new ProjectTreeBrowseRequest(projectPath, startSelector, depth, pageSize, cursor)).ConfigureAwait(false);
      return StructuredToolResult.CreateCanonical(rendered.CanonicalText, isError: !rendered.IsSuccess);
  }
  ```

  Add complete parameter descriptions stating selector matching, point-in-time pagination, adjustable page size, and restart/expiry behavior.

- [ ] **Step 7: Register production singletons**

  ```csharp
  builder.Services.AddSingleton(sp => new ProjectTreeCursorCodec(
      sp.GetRequiredService<AuthenticatedCursorProtector>()));
  builder.Services.AddSingleton(sp => new ProjectTreeSnapshotStore(TimeProvider.System));
  builder.Services.AddSingleton(sp => new ProjectTreePageProjector(
      sp.GetRequiredService<ProjectTreeCursorCodec>()));
  builder.Services.AddSingleton(sp => new ProjectTreeBrowseCoordinator(
      sp.GetRequiredService<OpennessWorkerClient>(),
      sp.GetRequiredService<ProjectTreeCursorCodec>(),
      sp.GetRequiredService<ProjectTreeSnapshotStore>(),
      sp.GetRequiredService<ProjectTreePageProjector>(),
      TimeProvider.System));
  ```

  Confirm the existing `OpennessWorkerClient` is singleton and both hardware and project-tree codecs resolve the same `AuthenticatedCursorProtector` instance. Do not add a static store or compatibility weak table for project-tree browsing.

- [ ] **Step 8: Add typed FakeWorker scenarios**

  Construct scenario payloads from shared DTOs so future contract changes fail compilation rather than silently drifting.

  ```csharp
  static ProjectTreeBrowseResultInfo ProjectTreeV3Fixture()
      => new()
      {
          Depth = null,
          StartSelector = null,
          Roots = new List<ProjectTreeNode>
          {
              new()
              {
                  Name = "PLC_1",
                  NodeType = ProjectTreeNodeTypes.Device,
                  Children = BuildProjectTreeChildren()
              }
          }
      };
  ```

  The one-shot scenario fails a second `browse_project_tree_v3_snapshot` request, proving continuation never crosses IPC. The malformed scenario includes a recognizable secret marker that protocol tests assert is absent from the public response.

- [ ] **Step 9: Run integration tests, stub-build, and stop at the commit boundary**

  Run the Step 3 command again and confirm GREEN, then:

  ```powershell
  dotnet build TiaMcpServer.sln -c Debug -m:1 --no-restore --disable-build-servers /p:UseTiaPortalReferenceStubs=true
  rg -n "startPath|StartPath|details\.Path|\[\"Path\"\]|ProjectTreeWalker" TiaMcpServer TiaMcpServer.Contracts TiaMcpServer.OpennessWorker TiaMcpServer.FakeWorker TiaMcpServer.Tests
  git diff --check
  ```

  Confirm GREEN, no remaining production legacy path, exact schema, one worker call per initial request, no worker call per continuation, and byte-identical text/structured output.

  Do not commit without explicit authorization. When authorized, use:

  ```powershell
  git add TiaMcpServer/ProjectTree/ProjectTreeBrowseCoordinator.cs TiaMcpServer/Tools/ProjectReadTools.cs TiaMcpServer/Worker/OpennessWorkerClient.cs TiaMcpServer/Program.cs TiaMcpServer.Contracts/WorkerRequest.cs TiaMcpServer.OpennessWorker TiaMcpServer.FakeWorker/Program.cs TiaMcpServer.Tests/Project TiaMcpServer.Tests/Tools/McpToolSchemaTests.cs
  git commit -m "feat(project): expose cached project tree browsing"
  ```

---

### Task 10: Document the v3 Migration and Prepare the Read-Only Live Harness

**Files:**

- Create: `scripts/live-test-project-tree-v3.ps1`
- Create: `TiaMcpServer.Tests/Project/ProjectTreeLiveHarnessContractTests.cs`
- Modify: `README.md`
- Modify: `docs/ARCHITECTURE.md`
- Modify: `docs/SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md`
- Modify: `docs/development/local-mcp-testing.md`
- Modify: `docs/IMPROVEMENT_LOG.md`

- [ ] **Step 1: Write a failing static contract test for the live harness**

  The test requires read-only host startup, three timed runs per initial-browse mode, full continuation walking, parent-chain selector reconstruction, missing/ambiguous/invalid selector checks, canonical text/structured equality, and JSON evidence output. It rejects every write-tool name and `confirm=true` spelling.

  ```csharp
  [Fact]
  public void LiveHarness_IsReadOnlyAndCoversTheApprovedEvidenceMatrix()
  {
      var source = File.ReadAllText(RepositoryFile("scripts", "live-test-project-tree-v3.ps1"));
      Assert.Contains("--read-only", source, StringComparison.Ordinal);
      Assert.Contains("1..3", source, StringComparison.Ordinal);
      Assert.Contains("Measure-InitialBrowse", source, StringComparison.Ordinal);
      Assert.Contains("Read-AllSnapshotPages", source, StringComparison.Ordinal);
      Assert.Contains("Reconstruct-TypedSelector", source, StringComparison.Ordinal);
      Assert.Contains("target_not_found", source, StringComparison.Ordinal);
      Assert.Contains("target_ambiguous", source, StringComparison.Ordinal);
      Assert.Contains("invalid_selector", source, StringComparison.Ordinal);
      Assert.DoesNotContain("network_write", source, StringComparison.Ordinal);
      Assert.DoesNotContain("apply_write_batch", source, StringComparison.Ordinal);
      Assert.DoesNotContain("confirm=true", source, StringComparison.OrdinalIgnoreCase);
  }
  ```

- [ ] **Step 2: Run the harness contract test and confirm RED**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~ProjectTreeLiveHarnessContractTests"
  ```

  Expected RED: `scripts/live-test-project-tree-v3.ps1` does not exist.

- [ ] **Step 3: Implement a deterministic read-only evidence harness without running it**

  Require a real project path through `-ProjectPath` or `TIA_MCP_LIVE_PROJECT_PATH`; default the server path to the Release build; launch it with `--read-only`; implement MCP initialize/list/call framing; and write evidence under `artifacts/issue-32-live/`.

  ```powershell
  [CmdletBinding()]
  param(
      [string] $ProjectPath = $env:TIA_MCP_LIVE_PROJECT_PATH,
      [string] $ServerPath = (Join-Path $PSScriptRoot '..\TiaMcpServer\bin\Release\net8.0\TiaMcpServer.exe'),
      [string] $EvidencePath = (Join-Path $PSScriptRoot '..\artifacts\issue-32-live\project-tree-v3-evidence.json')
  )

  Set-StrictMode -Version Latest
  $ErrorActionPreference = 'Stop'
  if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
      throw 'Supply -ProjectPath or TIA_MCP_LIVE_PROJECT_PATH. HU00954_CPU_AA_V21 is preferred when available.'
  }

  $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
  $startInfo.FileName = (Resolve-Path -LiteralPath $ServerPath).Path
  $startInfo.UseShellExecute = $false
  $startInfo.CreateNoWindow = $true
  $startInfo.RedirectStandardInput = $true
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true
  $startInfo.ArgumentList.Add('--read-only')
  $startInfo.ArgumentList.Add('--project')
  $startInfo.ArgumentList.Add((Resolve-Path -LiteralPath $ProjectPath).Path)
  $server = [System.Diagnostics.Process]::new()
  $server.StartInfo = $startInfo
  if (-not $server.Start()) { throw 'Could not start the TIA MCP server.' }
  ```

  The harness must close the process in `finally`, must not save or call any mutating tool, and must retain stdout/stderr beside the structured evidence.

- [ ] **Step 4: Implement timing, paging, and semantic evidence collection**

  ```powershell
  function Get-MedianMilliseconds([double[]] $Values) {
      $ordered = @($Values | Sort-Object)
      return $ordered[[math]::Floor($ordered.Count / 2)]
  }

  function Measure-InitialBrowse([hashtable] $Arguments) {
      $runs = foreach ($run in 1..3) {
          $watch = [System.Diagnostics.Stopwatch]::StartNew()
          $response = Invoke-McpTool -Name 'browse_project_tree' -Arguments $Arguments
          $watch.Stop()
          Assert-CanonicalRepresentationsEqual $response
          [pscustomobject]@{ run = $run; elapsedMs = $watch.Elapsed.TotalMilliseconds; response = $response }
      }
      [pscustomobject]@{
          runs = $runs
          medianMs = Get-MedianMilliseconds @($runs.elapsedMs)
      }
  }

  function Read-AllSnapshotPages([object] $FirstPage) {
      $pages = @($FirstPage)
      $cursor = $FirstPage.result.pagination.nextCursor
      while ($null -ne $cursor) {
          $next = Invoke-McpTool -Name 'browse_project_tree' -Arguments @{ cursor = $cursor; pageSize = 200 }
          $pages += $next
          $cursor = $next.result.pagination.nextCursor
      }
      return $pages
  }
  ```

  Record total node count, exact sequence continuity, parent-before-child relationships, cursor completion, no legacy `Path`, no truncation marker, and canonical representation equality. Reconstruct one deep selector from returned parent IDs and compare its complete result with the equivalent subtree from the complete selected-device snapshot.

  Search the observed tree for a naturally ambiguous direct-child `(nodeType, name)` pair. If none exists, fail the ambiguous-selector acceptance check explicitly; do not modify the project to create one. The user may then authorize a different read-only project fixture.

- [ ] **Step 5: Update all maintained documentation**

  Add the exact v2-to-v3 request migration:

  ```json
  { "projectPath": null, "depth": 2, "startPath": "PLC_1" }
  ```

  ```json
  {
    "projectPath": null,
    "depth": 2,
    "startSelector": [
      { "nodeType": "Device", "name": "PLC_1" }
    ]
  }
  ```

  Also include a complete v3 success envelope example, cursor-only continuation, selector reconstruction from parent-first nodes, point-in-time/cache-loss semantics, page and snapshot limits, all failure categories, and the removal of `details.Path`. State that `v3.0.0` is required because the old bare nested array is not retained.

  In `README.md`, every cross-document link must be an absolute `https://github.com/Czarnak/tia-portal-mcp/blob/main/...` URL. Keep procedural detail in the four maintained `docs/` pages rather than expanding the landing page.

- [ ] **Step 6: Update the architecture and improvement boundaries precisely**

  `docs/ARCHITECTURE.md` must show this flow:

  ```text
  cursor-free browse
      -> net48 typed/scoped walk (one worker call)
      -> net8 strict decode + post-filter flatten
      -> bounded immutable snapshot store
      -> exact canonical page

  cursor continuation
      -> authenticate process-local cursor
      -> cache lookup + query/range validation under lock
      -> exact canonical page (zero worker calls)
  ```

  Record the deeper direct Openness resolver/depth-pruned walk as a measured follow-up, not shipped v3 behavior. In `docs/IMPROVEMENT_LOG.md`, distinguish offline implementation from the separately pending live performance/semantic acceptance until that run is authorized and accepted.

- [ ] **Step 7: Validate docs and harness without live execution**

  Run the Step 2 test again and confirm GREEN, then:

  ```powershell
  rg -n "startPath|deviceName|browse_project_tree|contractVersion|snapshot_unavailable|result_item_too_large" README.md docs/ARCHITECTURE.md docs/SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md docs/development/local-mcp-testing.md docs/IMPROVEMENT_LOG.md
  pwsh -NoProfile -Command '$errors=$null; [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path "scripts/live-test-project-tree-v3.ps1"),[ref]$null,[ref]$errors) > $null; if($errors.Count){$errors | ForEach-Object Message; exit 1}'
  git diff --check
  ```

  Remaining `startPath`/`deviceName` matches must be clearly labeled v2 migration examples or explicit removal statements, never current v3 inputs. Do not run the harness.

- [ ] **Step 8: Review maintained versus historical docs and stop at the commit boundary**

  Confirm no historical spec, plan, or acceptance report was rewritten. The only historical file changed by implementation should be a later live report, after authorization.

  Do not commit without explicit authorization. When authorized, use:

  ```powershell
  git add README.md docs/ARCHITECTURE.md docs/SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md docs/development/local-mcp-testing.md docs/IMPROVEMENT_LOG.md scripts/live-test-project-tree-v3.ps1 TiaMcpServer.Tests/Project/ProjectTreeLiveHarnessContractTests.cs
  git commit -m "docs(project): document project tree v3 migration"
  ```

---

### Task 11: Run the Mandatory Offline, Stub, Coverage, and Package Gates

**Files:**

- Review only: all implementation and documentation files changed by Tasks 1–10
- Produce local ignored artifacts under: `artifacts/issue-32-coverage-*`, `artifacts/issue-32-v3-package/`

- [ ] **Step 1: Confirm the expected branch, commits, and worktree scope**

  ```powershell
  git branch --show-current
  git log --oneline --decorate -12
  git status --short
  git diff --stat main...HEAD
  git diff --stat
  ```

  Expected branch: `feature/v3-cutover`. Confirm that its ancestry contains the four preceding stack branches. Investigate any unrelated path before continuing; do not discard user changes.

- [ ] **Step 2: Restore once and run the serial Release stub build**

  ```powershell
  dotnet restore TiaMcpServer.sln
  dotnet build TiaMcpServer.sln -c Release -m:1 --no-restore --disable-build-servers /p:UseTiaPortalReferenceStubs=true
  ```

  Expected: zero build errors. Treat warnings as evidence to inspect rather than silently accepting new ones.

- [ ] **Step 3: Run the complete test suite from the verified Release build**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Release --no-build -m:1 --disable-build-servers
  ```

  Expected: every test passes. Record the exact passed/skipped/failed counts; do not reuse an earlier focused result.

- [ ] **Step 4: Collect scoped coverage and enforce the repository threshold**

  ```powershell
  $coverageResults = Join-Path (Get-Location) ("artifacts\issue-32-coverage-" + [guid]::NewGuid().ToString('N'))
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Release --no-build --collect:"XPlat Code Coverage" --settings TiaMcpServer.Tests/coverage.runsettings --results-directory $coverageResults
  $coverageReport = Get-ChildItem -LiteralPath $coverageResults -Recurse -Filter coverage.cobertura.xml | Select-Object -First 1
  .\scripts\verify-coverage-threshold.ps1 -CoveragePath $coverageReport.FullName -MinimumLineRate 0.80
  ```

  Expected: scoped line coverage is at least 80%. If it is not, add behavior-focused tests to the responsible earlier task and rerun that task's RED/GREEN cycle before repeating this gate.

- [ ] **Step 5: Pack the breaking release as v3.0.0 and verify package contents**

  ```powershell
  dotnet pack TiaMcpServer/TiaMcpServer.csproj -c Release --no-restore -o artifacts/issue-32-v3-package /p:Version=3.0.0 /p:PackageVersion=3.0.0 /p:InformationalVersion=3.0.0 /p:IncludeSourceRevisionInInformationalVersion=false /p:UseTiaPortalReferenceStubs=true
  .\scripts\verify-doctor-package.ps1 -PackagePath .\artifacts\issue-32-v3-package\TiaMcpServer.3.0.0.nupkg
  ```

  Inspect the package metadata and bundled README to confirm version `3.0.0`, the new v3 request/response documentation, and no Siemens DLLs.

- [ ] **Step 6: Run contract-specific searches and diff hygiene**

  ```powershell
  rg -n "startPath|StartPath|details\.Path|\[\"Path\"\]" TiaMcpServer TiaMcpServer.Contracts TiaMcpServer.OpennessWorker TiaMcpServer.FakeWorker TiaMcpServer.Tests
  rg -n "ConditionalWeakTable|static.*ProjectTree|static.*Snapshot" TiaMcpServer/ProjectTree TiaMcpServer/Program.cs
  rg -n "NetworkObjectCursorCodec" TiaMcpServer.OpennessWorker TiaMcpServer.Contracts
  git diff --check
  git status --short
  ```

  Expected:

  - no current production `StartPath` or legacy `details.Path` producer;
  - no new project-tree static cache or weak table;
  - the net48 network-object cursor remains separate;
  - only intended files are modified.

- [ ] **Step 7: Perform a requirement-by-requirement implementation review**

  Check each item in the spec's mandatory offline list against a named passing test or command. In particular, confirm all selector transitions, differential equivalence, recursive grouped-device selection, pre-order parent relationships, exact output schema, typed rejection, TTL/LRU/replay/concurrency, every failure category, one/zero worker-call counts, and every operation-policy category.

  ```powershell
  git diff --name-only main...HEAD
  git diff -- README.md docs TiaMcpServer TiaMcpServer.Contracts TiaMcpServer.OpennessWorker TiaMcpServer.FakeWorker TiaMcpServer.Tests scripts/live-test-project-tree-v3.ps1
  ```

- [ ] **Step 8: Stop at the offline-complete and live-authorization boundary**

  Report exact build/test/coverage/package evidence, all commits actually authorized and created, and the remaining live boundary. Do not run `scripts/live-test-project-tree-v3.ps1`, do not claim issue #32 resolved, and do not push or open a PR without new explicit authorization.

---

## Post-Stack Release Gate

### Task 12: Run Separately Authorized Live TIA Portal V21 Acceptance

**Authorization gate:** Do not start this task until the user explicitly authorizes the live run after reviewing Task 11's fresh offline evidence. This task is read-only; it does not authorize saving, writes, PLC control, pushing, opening a PR, or merging.

**Files:**

- Execute: `scripts/live-test-project-tree-v3.ps1`
- Read: `artifacts/issue-32-live/project-tree-v3-evidence.json`
- Create after successful review: `docs/superpowers/acceptance/reports/2026-09-06-issue-32-project-tree-v3-live.md`
- Modify after successful review: `docs/superpowers/README.md`
- Modify after successful review: `docs/README.md`
- Modify after successful review: `docs/IMPROVEMENT_LOG.md`

- [ ] **Step 1: Reconfirm project, read-only mode, and clean baseline**

  ```powershell
  $env:TIA_MCP_LIVE_PROJECT_PATH
  Test-Path -LiteralPath $env:TIA_MCP_LIVE_PROJECT_PATH
  git status --short
  ```

  Prefer the issue's `HU00954_CPU_AA_V21` project when available. If the path is missing, the project is open with unsaved work, or read-only access cannot be guaranteed, stop and ask the user rather than substituting another project or mode.

- [ ] **Step 2: Build against the installed V21 assemblies without running the harness yet**

  ```powershell
  dotnet build TiaMcpServer.sln -c Release -m:1 --disable-build-servers /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"
  ```

  Expected: the real-assembly Release build succeeds. This is still build evidence, not live Openness acceptance.

- [ ] **Step 3: Execute the authorized read-only harness once**

  ```powershell
  .\scripts\live-test-project-tree-v3.ps1 -ProjectPath $env:TIA_MCP_LIVE_PROJECT_PATH -ServerPath .\TiaMcpServer\bin\Release\net8.0\TiaMcpServer.exe -EvidencePath .\artifacts\issue-32-live\project-tree-v3-evidence.json
  ```

  Do not retry automatically after a crash or timeout. Preserve stdout, stderr, and the first evidence file for diagnosis.

- [ ] **Step 4: Validate structural and semantic acceptance evidence**

  ```powershell
  $evidence = Get-Content -LiteralPath .\artifacts\issue-32-live\project-tree-v3-evidence.json -Raw | ConvertFrom-Json -Depth 100
  $evidence.summary | Format-List
  $evidence.timing | Format-List
  $evidence.checks | Format-Table name, passed, detail -AutoSize
  ```

  Required PASS evidence:

  - exactly one Openness traversal per initial call and no worker call for continuation;
  - all selected pages complete with contiguous order, correct parents, expected total, and no truncation;
  - deep reconstructed selector equals the corresponding full selected-device subtree;
  - missing, ambiguous, and structurally invalid selectors return their exact categories;
  - three-run medians and run spread are recorded for unselected and one-Device initial calls;
  - the Device selector shows a material improvement on this project.

- [ ] **Step 5: Ask the user to judge the timing evidence**

  Present the two three-run distributions, medians, structural checks, and any environmental caveats. There is no invented numeric SLO. The user retains final acceptance authority over whether the measured device-scoping improvement is material.

- [ ] **Step 6: Record accepted live evidence without rewriting history**

  Only after the user accepts the result, create the report with project identity, environment, commit, exact commands, three runs and medians, full page counts, semantic checks, failure checks, stdout/stderr locations, and no-save/no-mutation statement.

  ```markdown
  # Issue #32 project-tree browsing v3 — live TIA Portal V21 acceptance

  **Result:** PASS — accepted by the user after review of the recorded three-run medians and structural evidence.

  **Boundary:** Read-only TIA Portal V21 acceptance. No project save, engineering mutation, PLC control, deployment, or plant acceptance was performed.
  ```

  Add the report to `docs/superpowers/README.md` and `docs/README.md`. Update `docs/IMPROVEMENT_LOG.md` from live-pending to accepted while retaining the deferred deeper direct-resolver optimization.

- [ ] **Step 7: Validate the report and stop at the commit boundary**

  ```powershell
  rg -n "Issue #32|three-run|median|one worker|zero worker|read-only|no project save|material improvement" docs/superpowers/acceptance/reports/2026-09-06-issue-32-project-tree-v3-live.md docs/superpowers/README.md docs/README.md docs/IMPROVEMENT_LOG.md
  git diff --check
  git status --short
  ```

  Do not commit without explicit authorization. When authorized, use:

  ```powershell
  git add docs/superpowers/acceptance/reports/2026-09-06-issue-32-project-tree-v3-live.md docs/superpowers/README.md docs/README.md docs/IMPROVEMENT_LOG.md
  git commit -m "test(project): record project tree v3 live acceptance"
  ```

  Do not push, open a PR, close issue #32, or merge without separate explicit authorization.

---

## Traceability Checklist

| Approved design concern | Owning task(s) |
| --- | --- |
| One uniform v3 request/response and output schema | 1, 8 |
| Complete typed selector vocabulary and direct-child resolution | 1, 2 |
| Device scoping before PLC discovery and full selected-device walk | 2, 3 |
| Typed worker payload and removal of `details.Path` | 3, 4 |
| Flat pre-order nodes and selector reconstruction | 4 |
| Shared purpose-separated authenticated cursor protection | 6 |
| Project-tree cursor classification and query identity | 7 |
| Snapshot TTL, LRU, limits, replay, concurrency, and disposal | 7 |
| Exact bounded page projection and explicit size failures | 8 |
| One initial worker call and zero continuation calls | 9 |
| Capability-aware timeout/crash guidance | 5 |
| v3 migration, architecture, operations, testing, and improvement docs | 10 |
| Full stub/test/coverage/package gates | 11 |
| Separately authorized live V21 equivalence and performance judgment | 12 |

The implementation is complete only when every applicable checkbox through Task 11 has fresh evidence. Issue #32 is not accepted as resolved until Task 12 is separately authorized, its structural checks pass, and the user accepts the measured performance result.
