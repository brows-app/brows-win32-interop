# Solution code review

Initial review completed on 2026-10-02. The final review on 2026-10-03 checked every existing finding and all
49 current C# source files, all 11 test files, both libraries, build/package settings, and the release workflow.
It covered native signatures, COM vtable order, HRESULT handling, apartment use, cancellation, resource lifetime,
and path handling.

There are **22 numbered findings** below. Findings **#1–16 and #18–20 remain fixed or resolved**;
**#17 remains open**. Findings **#21 and #22** were added during the final review; their current statuses
and fix validation are recorded in their sections below. The final review did not change production code.

The Boolean returned by `Work` and `Operate` indicates that work was queued for native execution, rather than
success for every item; returning `true` after the invalid-name probe is expected under that contract.
P1 means high priority, P2 means medium priority, and P3 means low priority. Findings distinguish reproduced
behavior, defects established by code inspection, and platform/API contract limitations.

## Findings

### 1. [P1] The cancellation callback returns a successful HRESULT

**Status: Fixed.**

**Location:** [Win32ProgressSink.cs:9–39](source/Brows.Win32.Interop.Operations/Win32/Win32ProgressSink.cs#L9).

Before the fix, casting `ERROR_CANCELLED` directly to `HRESULT` returned `0x000004C7`. Its failure bit was clear, so it was a success HRESULT. The callback did not report an error to COM when cancellation was requested.

**Before the fix:** Calling the sink with an already canceled token returned exactly `0x000004C7`.

**Fix applied:** The callback converts the Win32 error with `HRESULTExtension.FromWin32`, yielding the failure HRESULT `0x800704C7`. Cancellation is now also checked at worker entry, before native execution, and in pre-operation callbacks that can cancel the current and subsequent operations. See [COM error handling](https://learn.microsoft.com/en-us/windows/win32/learnwin32/error-handling-in-com) and [PreCopyItem cancellation behavior](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifileoperationprogresssink-precopyitem).

### 2. [P1] The default file-operation mode ignores cancellation once work starts

**Status: Fixed.**

**Location:**
[Win32FileOperation.cs:131–179](source/Brows.Win32.Interop.Operations/Win32/Win32FileOperation.cs#L131),
[Win32ProgressSink.cs:21–39](source/Brows.Win32.Interop.Operations/Win32/Win32ProgressSink.cs#L21).

Before the fix, the progress sink was advised only when `Silent` was true. With the default `Silent = false`, the cancellation token was never checked by `Work()` or any callback. The supplied `STAThreadPool` token only cancels work before dispatch; its synchronous callback cannot be interrupted after it starts. Canceling the library token during a copy, move, or delete therefore did not stop the native operation in the default mode.

**Evidence before the fix:** The conditional registration and absence of token checks were established by inspection; the installed `Brows.Win32.STAThreadPool` 1.2.2 README explicitly documents that a token does not interrupt an already started synchronous callback.

**Fix applied:** The cancellation sink is advised in both modes. The worker checks the token before and during
queueing, immediately before `PerformOperations`, and after native execution. The sink checks it at operation
start, before each item, and in progress callbacks. The sink is unadvised on completion or failure. The default
mode continues using native progress UI, so its managed progress argument is still unused.

**Final verification:** Canceling after the destination was created during a 512 MiB silent native copy propagated
`OperationCanceledException`, stopped that copy, and prevented the next queued file from being copied. Callback
probes on x64 and x86 returned `0x800704C7`. The default mode's use of the same sink was checked by inspection;
an interactive native-progress-UI cancellation probe was not performed.

### 3. [P1] Progress updates violate the dependency's UI context requirement

**Status: Resolved by upgrading `Brows.Operations` to 1.2.0.**

**Location:**
[Win32ProgressSink.cs:38](source/Brows.Win32.Interop.Operations/Win32/Win32ProgressSink.cs#L38),
[Win32FileOperation.cs:156–159](source/Brows.Win32.Interop.Operations/Win32/Win32FileOperation.cs#L156).

Before the dependency update, silent-mode `UpdateProgress` ran during `PerformOperations` on an STA worker and called `IOperationProgress.Change` there, while `Brows.Operations` 1.1.0 required calls on the operation's UI synchronization context. An STA worker is not necessarily that context.

**Evidence:** The `Brows.Operations` 1.2.0 XML documentation for `IOperationProgress` says its members can be called from any thread and that calls made off the operator's synchronization context are posted there, so state changes and notifications run on that context. The `Change` documentation further says off-context reports are applied asynchronously and consecutive pending reports for the same operation may be merged. This contract is present in the package assets for all four target frameworks used by this solution.

**Resolution:** `Directory.Packages.props` now selects `Brows.Operations` 1.2.0. The existing direct call from `Win32ProgressSink` is supported by the updated contract, so this repository does not need its own synchronization-context adapter. Progress notifications may be asynchronous and coalesced as documented by the dependency.

**Final verification:** The dependency source at the commit recorded in the installed package was also checked.
Its progress wrapper routes `Change` through `OperationContext.Report`, which posts off-context reports before
applying state changes. The any-thread contract is backed by the implementation.

### 4. [P2] Public service methods bypass all disposal coordination

**Status: Fixed.**

**Location:**
[Win32BaseService.cs:23–39](source/Brows.Win32.Interop/Win32/Win32BaseService.cs#L23),
[Win32KernelService.cs:101–123](source/Brows.Win32.Interop/Win32/Win32KernelService.cs#L101),
[Win32ShellService.cs:40–55](source/Brows.Win32.Interop/Win32/Win32ShellService.cs#L40),
[120–127](source/Brows.Win32.Interop/Win32/Win32ShellService.cs#L120).

Before the fix, public service methods never called `BeginOperation` or `EndOperation`. `ActiveOperations` stayed zero, so disposal could not wait for active operations or prevent subsequent service calls as its public documentation promises. An owned Shell pool could be emptied while service work was pending, and subsequent calls could create workers again.

**Reproduced before the fix:** `Win32KernelService.PathsAreEquivalent` returned `true` after the service was disposed. Inspection confirmed that Shell methods had the same missing guard. The pool's `Empty()` requests worker exit without synchronously joining active work.

**Fix applied:** Every public operation now pairs `BeginOperation` with `EndOperation` in a `finally` block.
The asynchronous link lookup remains tracked until its awaited work completes. Final x64 and x86 probes confirmed
that disposal waits for active work; the committed tests also verify rejection of calls after disposal.

### 5. [P2] Repeated disposal runs cleanup again

**Status: Fixed.**

**Location:** [Win32BaseService.cs:53–79](source/Brows.Win32.Interop/Win32/Win32BaseService.cs#L53).

Before the fix, the disposal method checked `Disposing` but did not check `Disposed` before starting cleanup. After the first call finished, a second call set `Disposing` again and executed `DisposeCore` again. An owned Shell pool was emptied repeatedly. During a repeated cleanup, another concurrent disposer could also return immediately because `Disposed` remained true from the first cleanup.

**Evidence:** Direct control-flow inspection of the two flags and the cleanup call; this does not depend on a native failure.

**Fix applied:** Disposal now returns immediately when the service is already disposed. Concurrent callers wait
while `Disposing` is true. Final x64 and x86 probes ran two concurrent disposers while work was active and confirmed
that both completed after the work was released, with exactly one cleanup call. Repeated disposal tests pass.

### 6. [P2] Path equivalence cannot compare directories

**Status: Fixed.**

**Location:** [Win32KernelService.cs:33–48](source/Brows.Win32.Interop/Win32/Win32KernelService.cs#L33).

Before the fix, both `CreateFileW` calls used zero flags. Windows requires `FILE_FLAG_BACKUP_SEMANTICS` to open directory handles. The public method accepts paths without restricting them to ordinary files, but even comparing a directory with itself threw instead of returning true.

**Reproduced:** Comparing the fixture directory to itself threw `Win32Exception` with “Access is denied” in x64 and x86.

**Fix applied:** Both `CreateFileW` calls now include `FILE_FLAG_BACKUP_SEMANTICS`, allowing directory handles to be
opened for the identity comparison. See [CreateFileW directory requirements][create-file]. Final x64 and x86
probes returned `true` for a directory compared with itself; the corresponding committed test passes on all
four target frameworks.

### 7. [P2] Metadata queries reject existing handles with delete access

**Status: Fixed.**

**Location:**
[Win32KernelService.cs:35,45](source/Brows.Win32.Interop/Win32/Win32KernelService.cs#L35),
[131](source/Brows.Win32.Interop/Win32/Win32KernelService.cs#L131).

Before the fix, all three metadata opens specified `FileShare.ReadWrite` and omitted `FileShare.Delete`. A file or directory already opened with delete access could not be opened with this sharing mask, even when the requested metadata was available. This broke identity and case-sensitivity queries around common rename/delete-capable handles.

**Reproduced:** Holding a valid `DELETE`-access handle with read/write/delete sharing made `PathsAreEquivalent(file, file)` fail with a sharing violation.

**Fix applied:** All three inspection handles now allow read, write, and delete sharing. See the
[CreateFileW sharing rules][create-file]. Final x64 and x86 probes held a valid `DELETE`-access handle and
successfully compared the file with itself.

### 8. [P2] File identity checks unnecessarily request file data access

**Status: Fixed.**

**Location:**
[Win32KernelService.cs:33–44](source/Brows.Win32.Interop/Win32/Win32KernelService.cs#L33),
[kernel32.cs:47–54](source/Brows.Win32.Interop/Win32/PlatformInvoke/kernel32.cs#L47).

Before the fix, `FileAccess.Read` was passed directly as a native access mask. Its numeric value is `1`, which is `FILE_READ_DATA`, rather than the .NET meaning of an abstract read mode. File identity inspection does not need permission to read file contents. The extra access caused avoidable sharing and permission failures.

**Reproduced:** With an exclusive writer open, a metadata-only `CreateFileW` call succeeded, while `PathsAreEquivalent` threw a sharing violation.

**Fix applied:** The identity handles now request zero access, which is sufficient for the metadata query, and
`CreateFileW` accepts a raw native `uint` access mask rather than `System.IO.FileAccess`. This avoids interpreting
.NET's `FileAccess.Read` value as `FILE_READ_DATA`. See [CreateFileW metadata access][create-file]. Final x64 and
x86 probes successfully compared a file with itself while an exclusive writer held it open.

### 9. [P2] File identity comparison can falsely equate different ReFS files

**Status: Fixed.**

**Location:** [Win32KernelService.cs:53–87](source/Brows.Win32.Interop/Win32/Win32KernelService.cs#L53).

Before the fix, the method compared only the volume serial number and the legacy 64-bit file index from `BY_HANDLE_FILE_INFORMATION`. Microsoft explicitly states that this 64-bit identifier is not guaranteed unique on ReFS, which uses 128-bit file identifiers. The method could therefore report that distinct ReFS files were equivalent.

**Evidence:** A documented filesystem limitation, not a collision reproduced on this machine.

**Fix applied:** The method now compares the volume serial and full 128-bit file ID from
`GetFileInformationByHandleEx(FileIdInfo)`. If that query fails, it uses the legacy 64-bit identifier only when
both handles are confirmed to be on NTFS. See [FILE_ID_INFO][file-id-info] and
[BY_HANDLE_FILE_INFORMATION][legacy-file-info]. Final review confirmed the full-width comparison and restricted
fallback; no ReFS collision was reproduced.

### 10. [P2] IO_STATUS_BLOCK has the wrong layout in a 32-bit process

**Status: Fixed.**

**Location:** [IO_STATUS_BLOCK.cs:5–9](source/Brows.Win32.Interop/Win32/PlatformInvoke/IO_STATUS_BLOCK.cs#L5), [ntdll.cs:8–13](source/Brows.Win32.Interop/Win32/PlatformInvoke/ntdll.cs#L8).

Before the fix, the managed declaration used a 32-bit status followed by `ulong`, producing 16 bytes with `Information` at offset 8 on x86. Native output was written into the wrong managed field location.

**Reproduced before the fix:** In an explicitly verified 32-bit process (`IntPtr.Size == 4`), `Marshal.SizeOf` returned 16 and `Marshal.OffsetOf(Information)` returned 8. The old layout happened to match the relevant offsets on x64.

**Impact limit:** The current case-sensitivity method does not read `Information`, so this defect does not by itself demonstrate a wrong case-sensitivity result or a memory overwrite outside the supplied structure. It is nevertheless an incorrect native output contract.

**Fix applied:** The status union and `Information` now use pointer-sized `IntPtr` and `UIntPtr` fields, matching
the native layout on x86 and x64. The current call site does not read the status field. See the
[native IO_STATUS_BLOCK definition][io-status-block]. Final probes returned size 8 and offset 4 on x86, and
size 16 and offset 8 on x64.

### 11. [P2] Case-sensitivity query failures are silently reported as false

**Status: Fixed.**

**Location:** [Win32KernelService.cs:125–158](source/Brows.Win32.Interop/Win32/Win32KernelService.cs#L125).

Before the fix, the switch returned false for every unrecognized NTSTATUS, including genuine access or I/O failures. An unsuccessful query was therefore indistinguishable from a directory explicitly configured as case insensitive. Callers that use this result to compare names could incorrectly collapse distinct case-sensitive entries.

Before the fix, the handle was opened with access zero, while Microsoft's contract for `FileCaseSensitiveInformation` specifies `FILE_READ_ATTRIBUTES`. Both access zero and explicit read-attributes access succeeded on this machine; a universal failure from access zero is **not** claimed here.

**Fix applied:** The handle now requests `FILE_READ_ATTRIBUTES`. Only `STATUS_NOT_IMPLEMENTED`,
`STATUS_NOT_SUPPORTED`, and `STATUS_INVALID_INFO_CLASS` return the compatibility fallback value `false`;
other statuses throw `IOException` with the NTSTATUS value. See [NtQueryInformationFile][nt-query-info].
Final review confirmed the access mask and status switch; the committed case-sensitivity tests pass on all
four target frameworks. Unexpected native failures were not injected.

### 12. [P2] Shortcut resolution swallows cancellation after entry

**Status: Fixed.**

**Location:** [Win32ShellService.cs:132–164](source/Brows.Win32.Interop/Win32/Win32ShellService.cs#L132).

Before the fix, a token canceled before entry threw, but cancellation while awaiting `ThreadPool.Work` was caught by `catch (Exception)` and converted to null. The same operation had inconsistent cancellation semantics depending on timing, and callers could not distinguish cancellation from a missing shortcut target.

**Reproduced before the fix:** Occupying a pool's only worker, queuing `GetLinkPath`, and then canceling its token produced null with task state `RanToCompletion`.

**Fix applied:** `GetLinkPath` now rethrows `OperationCanceledException` when its cancellation token has been
canceled; other failures retain the existing logged `null` result. Final x64 and x86 probes occupied the only
worker, queued a valid shortcut lookup, and canceled it. Both propagated `OperationCanceledException`.

### 13. [P2] Shell execution bypasses the STA pool and COM initialization

**Status: Fixed.**

**Location:**
[Win32ShellService.cs:21–55](source/Brows.Win32.Interop/Win32/Win32ShellService.cs#L21),
[public execution methods:86–112](source/Brows.Win32.Interop/Win32/Win32ShellService.cs#L86).

Before the fix, all three execution methods invoked `ShellExecuteExW` directly on the caller's thread. They performed no COM initialization or apartment check and did not use the service's STA pool. Calling them from an MTA worker or an uninitialized native thread could fail for Shell extensions that require STA COM, even though simple executable launches might succeed.

**Evidence:** An API contract problem established by inspection; no failing third-party Shell extension was invoked. Microsoft documents that this API can activate COM extensions and that some require STA.

**Fix applied:** Both `ExecuteDefault` overloads and `ExecuteProperties` now return `Task` and dispatch `ShellExecuteExW` through the service's message-pumping `STAThreadPool`. Each accepts an optional final `CancellationToken`, which can cancel queued work before dispatch; a native call already in progress cannot be interrupted. Operation tracking remains active until the worker completes, so disposal waits for queued or running execution. Callers must await the returned task to observe failures. See [ShellExecuteExW apartment guidance](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shellexecuteexw). The interop project built successfully across all target frameworks; its documentation warnings at that time are addressed by finding 19. No third-party Shell extension was exercised.

### 14. [P1] Both NuGet packages fail to build because their README is missing

**Status: Fixed.**

**Location:** [source/Directory.Build.props:12](source/Directory.Build.props#L12), both source `.csproj` files, [release pack step:74–76](.github/workflows/workflow.yml#L74).

Before the fix, `PackageReadmeFile` was set to `README.md`, but neither library included that file as a packed item. Defining the metadata property did not include the repository README in the package. The root README was also empty.

**Reproduced:** `dotnet pack --no-build --no-restore --configuration Release` failed for both source projects with `NU5039: The readme file 'README.md' does not exist in the package.` This blocks the release workflow before publishing.

**Fix applied:** Each packable project now has its own README, and the shared source props explicitly packs it at the NuGet package root. The repository root also has a README. A Release solution build and `dotnet pack --no-build --no-restore` succeeded; both `.nupkg` files contain a nonempty `README.md` at package root. At the time of that validation, the solution's test command completed successfully without executing cases; test coverage was added later (see finding 18).

### 15. [P2] global.json specifies an invalid SDK version

**Status: Fixed.**

**Location:** [global.json:4](global.json#L4).

Before the fix, `10.0.0` was not a valid .NET SDK feature-band version. SDK versions start at `10.0.100`, not the runtime-style `10.0.0`. The configuration did not provide a valid SDK baseline for its roll-forward policy.

**Reproduced before the fix:** `dotnet --info` identified this file as invalid and explicitly reported that SDK feature bands start at 1. The installed CLI still selected SDK 10.0.112 and completed the build; this finding was not a claim that the current build failed.

**Fix applied:** The SDK baseline is now `10.0.100`; the existing `feature` roll-forward policy is preserved.
Microsoft documents `10.0.100` as a valid SDK version format in
[global.json version requirements](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json).
Final restore, build, tests, and pack succeeded with the selected SDK 10.0.112.

### 16. [P3] Two NTSTATUS constants have incorrect numeric values

**Status: Fixed.**

**Location:** [NTSTATUS.cs:653](source/Brows.Win32.Interop/Win32/PlatformInvoke/NTSTATUS.cs#L653), [NTSTATUS.cs:4719](source/Brows.Win32.Interop/Win32/PlatformInvoke/NTSTATUS.cs#L4719).

| Constant | Before-fix value | Correct Windows SDK value |
| --- | --- | --- |
| `STATUS_GRAPHICS_DRIVER_MISMATCH` | `0x401E0117` | `0xC01E0009` |
| `STATUS_PKU2U_CERT_FAILURE` | `0xC000042E` | `0xC000042F` |

Before the fix, the first value also had the wrong severity bits: it represented an informational value instead of the actual error status. Any future comparisons or status classification using these members would be wrong.

**Verified:** Compared 1,784 named constants present in both the enum and Windows SDK 10.0.26100.0 `shared/ntstatus.h`; these were the two numeric mismatches. Neither member is used by the current case-sensitivity query, so no current public-method failure is attributed to them.

**Fix applied:** Both enum values now match Windows SDK 10.0.26100.0. The final comparison of all 1,784 matching
named NTSTATUS constants found zero numeric mismatches.

### 17. [P2] A failed package push can be hidden by a later successful push — Resolved

**Status: Resolved.**

**Location:** [workflow.yml:81–83](.github/workflows/workflow.yml#L81).

The publishing loop does not check each `dotnet nuget push` exit code. With normal PowerShell native-command error behavior, failure to publish the first package followed by successful publication of the second leaves a zero final exit code. The step can appear successful despite a partial release. Fixing finding 14 makes this path reachable.

**Evidence:** Code inspection plus GitHub's documented PowerShell wrapper, which propagates the final native exit code. No packages were published during this review.

**Reproduction:** A harmless PowerShell simulation with two native commands exiting 1 and then 0 left
`$LASTEXITCODE` equal to 0, with native-command error propagation disabled.

**Resolution:** The publishing loop now checks `$LASTEXITCODE` immediately after each `dotnet nuget push` and
throws with the package path and exit code when a push fails. This makes the step fail at the first unsuccessful
push. See [GitHub Actions shell exit-code behavior](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#exit-codes-and-error-action-preference).

### 18. [P2] The solution's test step exercises no implementation

**Status: Resolved.**

**Location:** [Brows.Win32.Interop.Tests](tests/Brows.Win32.Interop.Tests/Brows.Win32.Interop.Tests.csproj), [Brows.Win32.Interop.Operations.Tests](tests/Brows.Win32.Interop.Operations.Tests/Brows.Win32.Interop.Operations.Tests.csproj), [workflow.yml:64–65](.github/workflows/workflow.yml#L64).

At the time of the initial review, both test projects contained only assembly attributes and no test cases, and the Interop test project did not reference its library. That left CI's test step with no regression protection.

**Current state:** Both test projects now reference their corresponding library and contain NUnit test fixtures. The solution test run discovered and passed 16 cases per test project for each of the four target frameworks. CI's test step now exercises implementation code.

This resolves the zero-test finding. It does not claim exhaustive coverage of native layouts or every callback path.

### 19. [P3] Malformed NTSTATUS documentation produces compiler warnings

**Status: Fixed.**

**Location:** [NTSTATUS.cs near 2916](source/Brows.Win32.Interop/Win32/PlatformInvoke/NTSTATUS.cs#L2916), [4882](source/Brows.Win32.Interop/Win32/PlatformInvoke/NTSTATUS.cs#L4882), [6966](source/Brows.Win32.Interop/Win32/PlatformInvoke/NTSTATUS.cs#L6966), [8942](source/Brows.Win32.Interop/Win32/PlatformInvoke/NTSTATUS.cs#L8942).

The cited XML documentation blocks are now contiguous, with no unprefixed blank lines before their closing tags.

**Verified:** A follow-up Release build of the Interop project succeeded for all four target frameworks and emitted no `CS1570` warnings. It emitted four unrelated `CS8981` warnings for the lowercase `ntdll` and `winerror` type names.

The original full-build warnings were documentation defects, not evidence of incorrect enum values beyond finding 16.

### 20. [P3] Package metadata links to a different repository

**Status: Fixed.**

**Location:** [source/Directory.Build.props:13](source/Directory.Build.props#L13).

`PackageProjectUrl` now points to `https://github.com/brows-app/brows-win32-interop`, matching this solution's `RepositoryUrl`.

The package project link now identifies the correct repository.

### 21. [P2] Shortcut lookup fails for a filename relative to the current directory — Resolved

**Status: Resolved.**

**Location:**
[Win32ShellService.cs:137,145–146](source/Brows.Win32.Interop/Win32/Win32ShellService.cs#L137).

Before the fix, `GetLinkPath("document.lnk", token)` passed `Path.GetDirectoryName(file)`, an empty string for this
input, to `Shell.NameSpace`. The Shell returned no folder, so the method returned `null` even when that shortcut
existed in the current directory and had a valid target. The public API does not restrict the shortcut path to
absolute paths.

**Resolution:** `GetLinkPath` now calls `Path.GetFullPath` on the caller thread before queuing the Shell lookup.
Regression tests cover a bare filename, a relative directory path, and a queued lookup while the process current
directory changes. All three failed against the original implementation and passed after the fix on
`net10.0-windows`. After the fix, the affected test project passed all 19 cases on each of `net462`, `net48`,
`net8.0-windows`, and `net10.0-windows`.

**Reproduced:** In .NET 10 x64 and .NET Framework 4.6.2 x86, a valid shortcut resolved to its target when passed
by absolute path, but the same shortcut returned `null` when passed as `document.lnk` from its containing directory.
See the [Shell.NameSpace path contract][shell-namespace].

**Fix:** Resolve a relative filesystem shortcut path against the caller's current directory before dispatching
work, then use its full directory and filename for `NameSpace` and `ParseName`. Add regression coverage for a
bare filename and a path containing a relative directory, alongside the existing absolute-path test.

### 22. [P2] Opening a relative document with an executable uses the wrong directory — Resolved

**Status: Resolved. Added during the final review.**

**Location:**
[Win32ShellService.cs:109](source/Brows.Win32.Interop/Win32/Win32ShellService.cs#L109),
[28–30](source/Brows.Win32.Interop/Win32/Win32ShellService.cs#L28).

Before the fix, `ExecuteDefault(file, with)` quoted the supplied document path unchanged, while `Execute` set
`lpDirectory` to the executable's parent directory. If `file` was relative and the executable lived elsewhere,
the child resolved the argument against the executable's directory instead of the caller's directory. It could
fail to open an existing document or open a different document with the same name.

**Reproduced before the fix:** A child executable launched with `ExecuteDefault("document.txt", absoluteExecutablePath)`
received `document.txt` and ran in its own output directory. It reported `File.Exists(args[0]) == false`, although
the document existed in the caller's current directory. The probe ran on .NET 10 x64. The documented
[SHELLEXECUTEINFOW working-directory behavior][shell-execute-info] matches this observation.

**Resolution:** The `with` overload now resolves the document path against the caller's current directory and
quotes that full path before it queues STA work. The executable argument and the other Shell overloads retain
their existing behavior. Operation disposal and cancellation checks run before path resolution.

**Regression coverage:** `ExecuteDefault_WithExecutable_WhenDocumentPathIsRelative_UsesCallerDirectory` launches
a copied Windows Script Host from a temporary executable directory and places same-named scripts in that
directory and the caller directory. The caller and executable paths and the document name contain spaces. The
test confirms the caller's script ran for both relative and absolute document arguments and waits for a bounded
child-process signal before cleanup. Before the fix, the test failed because the executable-directory script
wrote `executable`; after the fix it passed.

**Branch verification:** The core test project built successfully for `net462`, `net48`, `net8.0-windows`, and
`net10.0-windows`. All 17 tests passed on each target framework (68 total). The build emitted only the two
existing `CS8981` warnings for lowercase native type names.

**Squash-merge verification:** Restore and the Release solution build succeeded on all four target frameworks.
The focused regression passed, followed by all 20 core tests and 16 Operations tests on each target framework
(144 executions total), with no failures or skips. Only existing `CS8981` warnings were emitted.

## Verification and limits

Final validation on 2026-10-03 used the selected SDK 10.0.112 and the following solution commands:

```powershell
dotnet restore brows-win32-interop.slnx
dotnet build brows-win32-interop.slnx --no-restore --configuration Release --verbosity minimal
dotnet test brows-win32-interop.slnx --no-restore --configuration Release --no-build --verbosity minimal
dotnet pack brows-win32-interop.slnx --no-restore --configuration Release --no-build --verbosity minimal
```

- Restore succeeded for all four projects. Both libraries and both test projects built in Release for `net462`,
  `net48`, `net8.0-windows`, and `net10.0-windows`: **zero errors**, four `CS8981` warnings for the lowercase
  `ntdll` and `winerror` names, and no `CS1570` warnings.
- Tests discovered and passed **16 cases per test project per target framework: 128 executions total**,
  with no failures or skips. These tests exercise implementation code but do not cover the new relative-path bugs.
- Pack succeeded for both libraries. Each `.nupkg` contains a nonempty root `README.md` and all four target assets;
  both symbol packages were also produced. No packages were published.
- Isolated probes on .NET 10 x64 and .NET Framework 4.6.2 x86 confirmed directory identity, metadata access with
  an exclusive writer and a delete-access handle, disposal waiting for active work, exactly one concurrent cleanup,
  cancellation HRESULTs, and cancellation of a queued shortcut lookup. They also reproduced finding #21.
- A .NET 10 x64 native copy probe canceled after the destination was created, observed propagated cancellation,
  and confirmed that the next queued file was not copied. A separate child-executable probe reproduced finding #22.
- Structure probes confirmed `IO_STATUS_BLOCK` size/Information offset of 16/8 on x64 and 8/4 on x86,
  `FILE_ID_INFO` size 24 on both, and `SHELLEXECUTEINFOW` sizes 112 and 60 respectively.
- Compared **1,784 named NTSTATUS values and 91 other native constants** against Windows SDK 10.0.26100.0;
  no numeric mismatches remain. Reviewed native signatures and COM method order; no additional mismatch was found.
- Checked the `Brows.Operations` 1.2.0 threading contract in all four package assets and the corresponding source
  implementation. The upgrade satisfactorily resolves finding #3.
- Rechecked the release workflow and safely simulated an earlier native failure followed by success;
  finding #17 remains open.

The initial review's build produced 212 warnings and its test run executed no cases; those are historical results.
The final build and tests above supersede them. Earlier COM probes also confirmed successful `Advise` despite
internal types and assembly-level `ComVisible(false)`, successful shortcut resolution, and silent file creation.
COM retains the native references needed by queued operations after source wrappers are released; this is not
reported as a lifetime defect.

ReFS collisions, older Windows behavior, unexpected native case-query failures, third-party Shell extensions,
and interactive progress-UI scheduling were not reproduced. Related fixes were checked against code and API
contracts, with these limits retained. Build, package, and isolated-probe artifacts remain under ignored `out/`.
Passing compilation and these checks do not establish correctness for every filesystem or Shell implementation.

[shell-namespace]: https://learn.microsoft.com/en-us/windows/win32/shell/shell-namespace
[shell-execute-info]: https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ns-shellapi-shellexecuteinfow
[create-file]: https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew
[file-id-info]: https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_id_info
[legacy-file-info]: https://learn.microsoft.com/en-us/windows/win32/api/fileapi/ns-fileapi-by_handle_file_information
[io-status-block]: https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/wdm/ns-wdm-_io_status_block
[nt-query-info]: https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntifs/nf-ntifs-ntqueryinformationfile
