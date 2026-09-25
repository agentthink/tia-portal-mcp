# Issue #32 Scalable Project-Tree Browsing v3 Design

**Date:** 2026-09-06

**Status:** Complete design; all architectural sections are approved in discussion and the
written specification awaits final user review before implementation planning.

**Scope:** Replace `browse_project_tree` with one long-term structured, cached, paged contract;
introduce typed project-tree selectors; scope the worker walk by the selector's device; and
correct read-only transport-failure guidance.

## Purpose

Resolve the scalability and error-surface problems reported in
[issue #32](https://github.com/Czarnak/tia-portal-mcp/issues/32) without creating a permanent
legacy/modern split in the public tool contract.

The current implementation has three relevant failure modes:

1. `ProjectTreeWalker.Walk` materializes the complete project tree before the legacy
   `ProjectTreeFilter` applies `startPath` and `depth`. Legacy path values are slash-joined display
   strings rather than unique typed identities, and narrow requests therefore still perform the
   expensive device and PLC traversal.
2. `StandaloneToolResultFormatter` applies the 60,000-character batch-item budget to the
   serialized standalone tree. Truncation can cut the inner JSON document and forces clients to
   repeat the expensive walk for many manually selected subtrees.
3. Transport timeouts already use the `worker_timeout` failure category, but their shared error
   text says that a write outcome is unknown even when the requested operation is read-only.

The issue's live measurements show that these effects are operationally significant: a complete
project walk took about 35--40 seconds regardless of narrowing, while repeated subtree calls made
a software inventory take more than 80 minutes. One device-scoped full tree was approximately
545,263 characters.

## Approved architecture

### One uniform v3 contract

Issue #32 will deliberately replace the public `browse_project_tree` contract in v3.0.0. It will
not retain a permanent conditional response in which ordinary calls return the legacy nested
array while paged calls return a different document.

Every call will return one typed, versioned response document. The tool will advertise that
document through its MCP output schema. A single canonical serialization will produce both the
text content and `structuredContent`, so the two representations cannot drift. The v3 contract
will contain real JSON values rather than a JSON document encoded inside a string payload.

The public request remains flat and accepts only `projectPath`, `startSelector`, `depth`,
`pageSize`, and `cursor`. `startSelector` is an optional ordered array of typed path segments;
each segment contains exactly `nodeType` and `name`. Unknown fields are rejected, including the
removed v2 fields `deviceName` and `startPath`.

A cursor-free request establishes a new query. A continuation requires only `cursor`; it may
change `pageSize`, while any repeated query field must match the cached query. `pageSize` is
always optional and defaults to 100 on both first and continuation calls.

Every response uses the following top-level envelope:

- `contractVersion`, with the constant value `"3.0"`;
- `status`, either `"succeeded"` or `"failed"`;
- `result`, populated only for success;
- `failure`, populated only for failure with `category` and `message`; and
- `warnings`, always present as an array.

A successful `result` contains `snapshot`, `query`, `pagination`, and `nodes`. The pagination
object contains `offset`, `requestedPageSize`, `returnedCount`, and nullable `nextCursor`.
`requestedPageSize` is an upper bound rather than a promise because the character budget may
require a smaller page. A failed response sets `result` to null; a successful response sets
`failure` to null.

The successful `query` contains exactly `projectPath`, `startSelector`, and `depth`.
`projectPath` is the canonical resolved project path, `startSelector` is the canonical
TIA-observed segment sequence or null, and `depth` is the requested value or null. Adjustable
`pageSize` is pagination metadata rather than query identity.

Because this is a breaking replacement of an established v2 tool, the package release is v3.0.0.
The maintained documentation includes a migration section with old and new request/response
examples. A separate migration document is unnecessary for one changed tool, and the legacy
contract is not carried into v3 as a compatibility mode.

### Cached pagination, not repeated traversal

The first request for a logical result performs exactly one scoped Openness walk and materializes
one immutable DTO snapshot. Continuation requests page that cached snapshot and never repeat the
TIA Portal traversal.

The snapshot has point-in-time semantics. Project changes made after snapshot creation do not
alter its remaining pages and do not trigger an expensive re-observation. Cache expiry, eviction,
or host restart makes the continuation unavailable; the caller then starts a new snapshot from
page one. There is no claim that pages reflect live changes after the snapshot was created.

### Flat node pages

Paged results contain flat node records rather than serialized JSON fragments or overlapping
subtrees. Each node has exactly these fields:

- `nodeId`: a non-empty opaque string unique only within the snapshot;
- `parentNodeId`: its parent's ID, or null for a snapshot root;
- `sequence`: its zero-based global pre-order position;
- `name`: the existing node name;
- `nodeType`: the existing functional node type; and
- `details`: a string-valued object, using an empty object when the node has no details.

IDs and sequences are assigned after `startSelector` resolution and `depth` filtering. Sequence
values are contiguous from zero through `totalNodes - 1`. Every parent precedes its descendants,
including across page boundaries, so sorting by `sequence` preserves root and sibling order. A
selected subtree root is promoted to a snapshot root with `parentNodeId: null`.

The durable client key is the pair `(snapshotId, nodeId)`; `nodeId` has no cross-snapshot meaning.
The v3 node has neither `children` nor a repeated path or selector. The legacy `details.Path`
member is removed. Consumers reconstruct typed selectors while streaming from each node's
`nodeType` and `name` plus its already-delivered parent chain. Page boundaries never split a node.

Snapshot metadata contains:

- `snapshotId`: an opaque snapshot identifier;
- `createdAt`: the UTC ISO-8601 creation timestamp;
- `idleExpiresAt`: the current sliding-TTL deadline; and
- `totalNodes`: the node count after filtering.

`idleExpiresAt` is a time-based deadline, not a retention guarantee: LRU eviction or host restart
may make the snapshot unavailable earlier.

### Host-owned in-memory snapshots

The persistent net48 worker performs the initial Openness walk and returns the materialized DTO
tree once. The net8 host validates and flattens that tree, stores the snapshot in memory, and
serves all continuation pages without worker IPC.

The cache stores only public/contract DTO data, never Siemens Openness objects. It does not write
project data to disk. Because the snapshot is host-owned, paging may continue after a worker
restart as long as the host and cached snapshot still exist.

`ProjectTreeSnapshotStore` is an explicitly registered singleton. It uses one private lock because
the cache contains at most four immutable snapshots and its protected work is bounded. Worker IPC,
TIA traversal, flattening, and whole-snapshot measurement occur outside the lock. Lookup, exact
page projection, TTL renewal, insertion, and eviction occur inside it, so an entry cannot be
evicted while its page is being built.

Every cursor-free call makes a fresh TIA observation; identical concurrent requests are not
coalesced. Before lookup or insertion, the store removes expired entries. A page renews TTL and
LRU position only after its exact response fits the public budget. Failed cursor, filter, range,
or projection attempts do not renew the entry. Cursor replay is allowed and returns the same node
range while renewing its TTL.

A one-page initial result is not retained because it has no continuation. Once a multi-page
snapshot has been cached, it remains cached after its final page so replay of an earlier cursor is
possible until expiry or eviction. LRU ties are resolved by oldest successful access, then oldest
creation time, then ordinal `snapshotId`.

The store, `ProjectTreeCursorCodec`, `ProjectTreeBrowseCoordinator`, and shared cursor protector
are singleton services alongside the existing singleton `OpennessWorkerClient`. Tests inject
`.NET` `TimeProvider`; production uses system time. Cleanup is lazy, with no timer, reaper, or
background task. Store disposal clears all entries and rejects later access. Cursor-protector
disposal zeroes the signing key; the host container performs both disposals at shutdown. No new
static cache or compatibility `ConditionalWeakTable` is introduced.

### Resource bounds

The approved default bounds are:

- each public page stays below the existing 60,000-character standalone-response budget;
- `pageSize` accepts values from 1 through 200 and is a maximum rather than a guarantee;
- one snapshot may contain at most 4,000,000 serialized characters;
- the host caches at most four snapshots and 16,000,000 total serialized characters;
- snapshots use a 10-minute sliding time-to-live, renewed by successful page retrieval; and
- eviction removes expired snapshots first, then the least recently used snapshot.

Character limits are deterministic contract budgets, not claims about exact CLR heap usage.
Implementation must still avoid unnecessary duplicate materializations. An oversized snapshot or
individual node, or a snapshot removed by expiry or eviction, fails explicitly. The server never
truncates a node, returns partial JSON, or silently repeats the walk.

The immutable snapshot content is the resolved query plus all filtered, flattened nodes. The host
measures `CanonicalJson.Serialize(snapshotContent).Length` before insertion. Content above
4,000,000 characters is rejected and never cached. The same measured content length is used for
the 16,000,000-character aggregate cache limit.

Page projection measures the exact final `BrowseProjectTreeResponse`, including its envelope,
snapshot and query metadata, cursor, warnings, and nodes. It begins with the lesser of the
requested page size and the remaining node count, then removes only trailing nodes until the
canonical response is at most 60,000 characters. The cursor advances by the number actually
returned. At most 200 bounded serialization attempts are permitted; estimated or separately
summed node sizes are not used.

The following reusable failure categories extend the closed vocabulary:

- `invalid_selector`: the typed selector is structurally invalid or contains an unknown node type
  or impossible transition;
- `snapshot_too_large`: the filtered snapshot content exceeds 4,000,000 characters;
- `snapshot_unavailable`: an otherwise usable continuation refers to a snapshot removed by
  expiry, eviction, or host restart;
- `result_item_too_large`: the next complete node cannot fit even when returned alone; and
- `result_metadata_too_large`: the mandatory envelope, query, cursor, or diagnostics cannot fit
  without a node.

Valid selectors with zero or multiple matches use the existing `target_not_found` and
`target_ambiguous` categories. Malformed or unauthenticated cursors remain `invalid_cursor`;
repeated query differences remain `cursor_filter_mismatch`; authenticated out-of-range offsets
remain `cursor_out_of_range`; and malformed worker payloads remain `protocol_error`. Bounded
failure responses never echo oversized nodes or diagnostics. No failure renews the snapshot TTL.

### Typed selector and traversal boundary

`startSelector` is the one public subtree-selection mechanism. It replaces both the v2
`deviceName` proposal and the ambiguous legacy `startPath`. Omitting it selects the complete
project tree. When present, it is a non-empty ordered array whose first segment is a `Device` and
whose later segments identify one direct child at a time. An empty array, an empty name, an
unknown node type, an extra segment member, or an impossible parent-child transition is
`invalid_selector`.

For example, a blocks-folder selector is:

```json
[
  { "nodeType": "Device", "name": "PLC_1" },
  { "nodeType": "PlcSoftware", "name": "PLC_1" },
  { "nodeType": "BlockFolder", "name": "Program blocks" },
  { "nodeType": "BlockFolder", "name": "Motors" }
]
```

The stable v3 node-type vocabulary is the existing public tree vocabulary: `Device`,
`PlcSoftware`, `SoftwareUnit`, `BlockFolder`, `SystemBlockFolder`, `OB`, `FB`, `FC`, `GlobalDB`,
`InstanceDB`, `ArrayDB`, `Block`, `TagTableFolder`, `TagTable`, `TypeFolder`, and `Type`. Valid
transitions are:

- project root to `Device`;
- `Device` to `PlcSoftware`;
- `PlcSoftware` to `SoftwareUnit`, `BlockFolder`, `TagTableFolder`, or `TypeFolder`;
- `SoftwareUnit` to `BlockFolder`, `TagTableFolder`, or `TypeFolder`;
- `BlockFolder` to another `BlockFolder`, `SystemBlockFolder`, or any block leaf type;
- `SystemBlockFolder` to another `SystemBlockFolder` or any block leaf type;
- `TagTableFolder` to `TagTableFolder` or `TagTable`; and
- `TypeFolder` to `TypeFolder` or `Type`.

Leaf nodes accept no following segment. At every step, `nodeType` comparison is ordinal and
case-sensitive against the contract vocabulary, while `name` matching is exact and ordinal
case-insensitive. Zero direct-child matches return `target_not_found`; multiple matches return
`target_ambiguous`. The resolver never chooses a first match. It preserves TIA's observed spelling
in the canonical selector stored in the snapshot query; repeated selectors on continuation calls
are compared by the same semantics.

The first `Device` segment is resolved across the complete recursive `ProjectDeviceEnumerator`
result, preserving grouped-device completeness from issue #31. Device selection happens before
`PlcSoftwareLocator.FindInDevice`, avoiding device-item traversal for non-target devices. The
worker then performs one complete walk of the selected device and resolves the remaining typed
segments against that materialized DTO before applying `depth`. `depth: 1` returns only the
selected root; omitted `depth` returns its complete observed subtree. Because filtering follows
the complete selected-device walk, `ChildrenOmitted` remains exact.

This design deliberately does not promise that a selector below the device level or `depth`
reduces first-call Openness time. Direct typed navigation and depth-pruned Openness traversal are
a follow-up optimization behind the same public selector contract. Before replacing the
post-walk resolver, differential tests and live evidence must show that direct resolution produces
the same ordered subtree as full-walk filtering for every supported node type. Partial resolver
coverage with silent full-walk fallback is not allowed because it would make performance
unpredictable.

The canonical resolved selector and `depth` are part of snapshot query identity and cursor
binding. Unfiltered snapshots remain supported but are subject to the same 4,000,000-character
limit. There is no separate `deviceName`, `plcName`, or string-path selector.

### Cursor-authoritative continuation

The first request supplies the project-tree selectors and `pageSize`. A continuation needs only
the opaque cursor and may supply a different valid `pageSize`. If it repeats a selector, that value
must match the cached snapshot query or the call returns `cursor_filter_mismatch`.

The process-local, HMAC-authenticated project-tree cursor contains exactly the snapshot identifier,
query hash, and next offset. It never embeds project-tree nodes or `pageSize`. Tampered or malformed
cursors fail closed.

A small host-side `AuthenticatedCursorProtector` owns the 32-byte process key, HMAC-SHA256,
fixed-time comparison, strict unpadded Base64URL framing, canonical round-trip validation, a
protection-format version, a 4,096-character input limit, and cryptographic purpose separation.
Purpose separation prevents a hardware cursor from being accepted as a project-tree cursor. A
process-instance identifier is covered by the signature and lets the project-tree codec classify
a cursor from a previous host as `snapshot_unavailable`; malformed or incorrectly signed cursors
claiming the current instance remain `invalid_cursor`.

Domain codecs retain their own exact state validation. `ProjectTreeCursorCodec` validates
`snapshotId`, `queryHash`, and `offset`. The existing host-side `HardwarePageCursorCodec` will be
migrated to the same protector while retaining its hardware-specific project, binding, query,
snapshot, and offset checks. Its opaque encoding may change because its cursors are already
process-local. The net48 worker's `NetworkObjectCursorCodec` remains separate because it runs in a
different process and validates against a freshly observed live snapshot.

### Capability-aware transport-failure guidance

Timeout and crash guidance is selected centrally from `OperationPolicyCatalog` using the exact
worker request method. `Observe`, `TemporaryExport`, and `SafetyRead` receive safe-read guidance.
`Compile`, `ProjectLifecycle`, `ProjectMutation`, and `OnlineControl` receive state-affecting
guidance. Missing or unknown classifications fail closed to state-affecting guidance. Public MCP
annotations and the host's access mode are not used because they are too coarse for internal and
mixed-operation worker calls.

For a safe read, timeout text is:

> The TIA Openness worker did not complete the read before the timeout. No project or PLC runtime
> mutation was requested. The worker session was discarded; retrying the read is safe.

Safe-read crash text is:

> The TIA Openness worker stopped before returning the read result. No project or PLC runtime
> mutation was requested. The worker will restart on the next worker call; retrying the read is
> safe.

For a state-affecting operation, timeout text is:

> The TIA Openness worker timed out before completion was confirmed. The project or PLC runtime
> state may have changed. Inspect current state before retrying.

State-affecting crash text is:

> The TIA Openness worker stopped before completion was confirmed. The project or PLC runtime
> state may have changed. Inspect current state before retrying.

The `worker_timeout` and `worker_crashed` categories remain unchanged, and their shared contract
documentation becomes capability-neutral. Both failures still invalidate the verified worker
binding. The host never retries automatically. A host-cached project-tree continuation remains
usable because it performs no worker call and is not bound to the replacement worker session.

### Superseded compatibility decision

An earlier discussion point paired flat paged nodes with a byte-identical legacy unpaged response.
The later, explicit decision to make the result fit the project's long-term growth supersedes that
compatibility clause. The approved end state is one v3 response shape for every
`browse_project_tree` call.

An earlier v3 draft retained separate `deviceName` and `startPath` fields and treated paths as
post-walk presentation strings. The approved typed-selector design supersedes both fields. A
single structured selector now supplies device scoping and subtree identity without ambiguous
slash parsing or overlapping selector mechanisms.

## Preserved invariants

- Complete traversal still includes direct devices and devices in recursively nested
  `DeviceUserGroup` collections.
- Grouped devices remain ordinary flat `Device` nodes; public device-folder nodes are not added.
- System block folders remain `SystemBlockFolder`, functional block node types remain unchanged,
  and `details.IsSystemBlock` continues to mean hierarchy membership only.
- Openness traversal remains worker-local. Siemens assemblies and objects never enter the net8
  host process.
- `browse_project_tree` remains read-only and never saves or mutates the project.
- Worker success payloads are decoded into a declared type; malformed payloads become
  `protocol_error` without echoing untrusted worker data.
- Continuation pages never cause an implicit new walk. A missing snapshot is an explicit failure.
- Offline, FakeWorker, and stub evidence remain distinct from separately authorized live TIA
  Portal V21 acceptance.

## Rejected or superseded approaches

- Raising every project-tree response to a fixed multi-million-character limit: it can flood MCP
  clients and does not bound model context consumption.
- Permanently supporting legacy and structured response modes in one tool: it requires a union or
  loose output schema and doubles client, test, and maintenance paths.
- Returning serialized JSON fragments: pages are unusable until concatenated and recovery is
  brittle.
- Returning overlapping subtrees: ancestors are duplicated and deterministic merging becomes
  unnecessarily complex.
- Keeping snapshots in the worker: continuation depends on worker lifetime and adds net48 state.
- Keeping snapshots on disk: it introduces project-data persistence, cleanup, permission, and
  locking concerns.
- Re-observing the project before every page: it recreates the performance multiplication that the
  issue is intended to remove.
- Retaining `startPath` or introducing a typed slash-delimited path string: both require escaping,
  parsing, and collision rules that a structured selector avoids.
- Keeping `deviceName` or adding `plcName` beside `startSelector`: overlapping selectors introduce
  redundant states and mismatch rules.
- Repeating the complete selector on every flat node: parent-first ordering already makes the
  selector reconstructable and avoids depth-dependent payload growth.
- Implementing partial direct Openness resolution in v3: inconsistent fallback would make latency
  depend on undocumented node-type coverage. Direct pruning remains a separately measured
  follow-up behind the typed contract.

## Migration and documentation

The implementation updates the maintained descriptions and examples in:

- `README.md`;
- `docs/ARCHITECTURE.md`;
- `docs/SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md`;
- `docs/development/local-mcp-testing.md`; and
- `docs/IMPROVEMENT_LOG.md`.

The migration section shows that the v2 request:

```json
{ "projectPath": null, "depth": 2, "startPath": "PLC_1" }
```

becomes:

```json
{
  "projectPath": null,
  "depth": 2,
  "startSelector": [
    { "nodeType": "Device", "name": "PLC_1" }
  ]
}
```

It also contrasts the v2 bare nested array with the v3 canonical envelope and explains how to
reconstruct selectors from parent-first flat nodes. Historical specifications, plans, and
acceptance reports remain historical and are not rewritten.

## Verification and acceptance

### Mandatory offline and stub verification

Implementation is not offline-complete until fresh evidence covers:

- request and output schemas, legacy and unknown-field rejection, and every valid and invalid
  typed-selector transition;
- ordinal case-insensitive name matching, canonical observed spelling, missing targets, ambiguous
  targets, and selector reconstruction from paged nodes;
- differential equivalence between typed-selector post-filtering and the same subtree selected
  from a complete scoped tree;
- device selection before `PlcSoftwareLocator.FindInDevice` across direct and recursively grouped
  devices;
- stable preorder, contiguous sequences, parent relationships, promoted roots, depth semantics,
  exact `ChildrenOmitted`, and removal of `details.Path`;
- typed worker-payload rejection and byte-identical canonical text and structured content;
- exact page projection, page-size adjustment, snapshot budgets, TTL, LRU eviction, cursor replay,
  process restart, worker restart, and all explicit failure categories;
- exactly one worker call for an initial snapshot and zero worker calls for every continuation;
- capability-aware timeout and crash guidance for every operation-policy category, including the
  unknown fail-closed case; and
- the serial stub build, complete test suite, and NuGet package verification.

Offline, stub, and FakeWorker evidence proves protocol and host behavior but does not qualify real
Siemens Openness traversal.

### Separately authorized live TIA Portal V21 acceptance

Live acceptance is a distinct authorization gate after offline completion. It is read-only and
must not save or mutate the project. Using the issue's original `HU00954_CPU_AA_V21` project when
available, it:

1. records three-run median timing for an unselected initial browse;
2. records three-run median timing for an initial browse whose selector contains one `Device`;
3. pages the complete selected snapshot and verifies counts, ordering, cursor completion, and lack
   of truncation;
4. reconstructs a deep selector from returned nodes and compares its result with the equivalent
   subtree of the complete selected-device snapshot; and
5. exercises missing, ambiguous, and structurally invalid selectors.

The release gate is structural rather than a machine-specific wall-clock service-level objective:
one Openness traversal per initial request, no worker call on continuation, complete equivalent
results, and device selection before PLC discovery. Timings are still recorded, and device
scoping must show a material improvement before issue #32 is claimed resolved. There is no hidden
numeric threshold: the user retains final acceptance authority based on the recorded three-run
medians and run spread. If the evidence is not convincing, the cause is investigated rather than
weakening the semantic or structural criteria.

## Current verification boundary

This document records the completed and approved design discussion. The corresponding
[implementation plan](../plans/2026-09-06-issue-32-scalable-project-tree-browsing-v3.md) now
defines the task-level TDD, verification, commit, and acceptance gates. No production code, tests,
package version, or live TIA Portal state has yet been changed for issue #32. Live TIA Portal
acceptance still requires separate authorization after offline implementation is complete.
