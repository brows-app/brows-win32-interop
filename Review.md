# Solution code review

Initial review completed on 2026-10-02. A status follow-up on 2026-10-03 checked findings #3 and #17–20. The initial scope covered all 46 C# source files, both libraries, both test projects, build/package settings, and the release workflow, with particular attention to native signatures, COM vtable order, HRESULT handling, apartment use, cancellation, and resource lifetime.

There are **20 numbered findings** below. The Boolean returned by `Work` and `Operate` means that some work was done, whether or not it succeeded; returning `true` after the invalid-name probe is expected under that contract, so it is not a finding. P1 means high priority, P2 means medium priority, and P3 means low priority. Findings distinguish reproduced behavior, defects established by code inspection, and platform/API contract limitations. Findings #1–16 and #18–20 are marked fixed or resolved below; finding #17 remains open.

## Findings

### 1. [P1] The cancellation callback returns a successful HRESULT

**Status: Fixed.**

**Location:** [Win32ProgressSink.cs:9–39](source/Brows.Win32.Interop.Operations/Win32/Win32ProgressSink.cs#L9).

Before the fix, casting `ERROR_CANCELLED` directly to `HRESULT` returned `0x000004C7`. Its failure bit was clear, so it was a success HRESULT. The callback did not report an error to COM when cancellation was requested.

**Before the fix:** Calling the sink with an already canceled token returned exactly `0x000004C7`.

**Fix applied:** The callback converts the Win32 error with `HRESULTExtension.FromWin32`, yielding the failure HRESULT `0x800704C7`. Cancellation is now also checked at worker entry, before native execution, and in pre-operation callbacks that can cancel the current and subsequent operations. See [COM error handling](https://learn.microsoft.com/en-us/windows/win32/learnwin32/error-handling-in-com) and [PreCopyItem cancellation behavior](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifileoperationprogresssink-precopyitem).

### 2. [P1] The default file-operation mode ignores cancellation once work starts

**Status: Fixed.**

**Location:** [Win32FileOperation.cs:124–174](source/Brows.Win32.Interop.Operations/Win32/Win32FileOperation.cs#L124), [Win32ProgressSink.cs:21–39](source/Brows.Win32.Interop.Operations/Win32/Win32ProgressSink.cs#L21).

Before the fix, the progress sink was advised only when `Silent` was true. With the default `Silent = false`, the cancellation token was never checked by `Work()` or any callback. The supplied `STAThreadPool` token only cancels work before dispatch; its synchronous callback cannot be interrupted after it starts. Canceling the library token during a copy, move, or delete therefore did not stop the native operation in the default mode.

**Evidence before the fix:** The conditional registration and absence of token checks were established by inspection; the installed `Brows.Win32.STAThreadPool` 1.2.2 README explicitly documents that a token does not interrupt an already started synchronous callback.

**Fix applied:** The cancellation sink is advised in both modes. The worker checks the token before and during queueing, immediately before `PerformOperations`, and after native execution. The sink checks it at operation start, before each item, and in progress callbacks. The sink is unadvised on completion or failure. The default mode continues using native progress UI, so its managed progress argument is still unused. Built the Operations project in Release for all four target frameworks: zero errors; existing documentation and SourceLink warnings remain. Native cancellation after a copy has started was not reproduced after this change.

### 3. [P1] Progress updates violate the dependency's UI context requirement

**Status: Resolved by upgrading `Brows.Operations` to 1.2.0.**

**Location:** [Win32ProgressSink.cs:38](source/Brows.Win32.Interop.Operations/Win32/Win32ProgressSink.cs#L38), [Win32FileOperation.cs:156,245–248](source/Brows.Win32.Interop.Operations/Win32/Win32FileOperation.cs#L156).

Before the dependency update, silent-mode `UpdateProgress` ran during `PerformOperations` on an STA worker and called `IOperationProgress.Change` there, while `Brows.Operations` 1.1.0 required calls on the operation's UI synchronization context. An STA worker is not necessarily that context.

**Evidence:** The `Brows.Operations` 1.2.0 XML documentation for `IOperationProgress` says its members can be called from any thread and that calls made off the operator's synchronization context are posted there, so state changes and notifications run on that context. The `Change` documentation further says off-context reports are applied asynchronously and consecutive pending reports for the same operation may be merged. This contract is present in the package assets for all four target frameworks used by this solution.

**Resolution:** `Directory.Packages.props` now selects `Brows.Operations` 1.2.0. The existing direct call from `Win32ProgressSink` is supported by the updated contract, so this repository does not need its own synchronization-context adapter. Progress notifications may be asynchronous and coalesced as documented by the dependency.

### 4. [P2] Public service methods bypass all disposal coordination

**Status: Fixed.**

**Location:** [Win32BaseService.cs:23–39](source/Brows.Win32.Interop/Win32/Win32BaseService.cs#L23), [Win32KernelService.cs:32,102](source/Brows.Win32.Interop/Win32/Win32KernelService.cs#L32), [Win32ShellService.cs:55–93](source/Brows.Win32.Interop/Win32/Win32ShellService.cs#L55).

Before the fix, public service methods never called `BeginOperation` or `EndOperation`. `ActiveOperations` stayed zero, so disposal could not wait for active operations or prevent subsequent service calls as its public documentation promises. An owned Shell pool could be emptied while service work was pending, and subsequent calls could create workers again.

**Reproduced before the fix:** `Win32KernelService.PathsAreEquivalent` returned `true` after the service was disposed. Inspection confirmed that Shell methods had the same missing guard. The pool's `Empty()` requests worker exit without synchronously joining active work.

**Fix applied:** Every public operation now pairs `BeginOperation` with `EndOperation` in a `finally` block. The asynchronous link lookup remains tracked until its awaited work completes. No tests or build were run for this change.

### 5. [P2] Repeated disposal runs cleanup again

**Status: Fixed.**

**Location:** [Win32BaseService.cs:53–75](source/Brows.Win32.Interop/Win32/Win32BaseService.cs#L53).

Before the fix, the disposal method checked `Disposing` but did not check `Disposed` before starting cleanup. After the first call finished, a second call set `Disposing` again and executed `DisposeCore` again. An owned Shell pool was emptied repeatedly. During a repeated cleanup, another concurrent disposer could also return immediately because `Disposed` remained true from the first cleanup.

**Evidence:** Direct control-flow inspection of the two flags and the cleanup call; this does not depend on a native failure.

**Fix applied:** Disposal now returns immediately when the service is already disposed. Concurrent callers wait while `Disposing` is true, so the stale `Disposed` value from an earlier cleanup cannot let them return before the current cleanup completes. No tests or build were run for this change.

### 6. [P2] Path equivalence cannot compare directories

**Status: Fixed.**

**Location:** [Win32KernelService.cs:30–45](source/Brows.Win32.Interop/Win32/Win32KernelService.cs#L30).

Before the fix, both `CreateFileW` calls used zero flags. Windows requires `FILE_FLAG_BACKUP_SEMANTICS` to open directory handles. The public method accepts paths without restricting them to ordinary files, but even comparing a directory with itself threw instead of returning true.

**Reproduced:** Comparing the fixture directory to itself threw `Win32Exception` with “Access is denied” in x64 and x86.

**Fix applied:** Both `CreateFileW` calls now include `FILE_FLAG_BACKUP_SEMANTICS`, allowing directory handles to be opened for the identity comparison. See [CreateFileW directory requirements](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew). No tests or build were run for this change.

### 7. [P2] Metadata queries reject existing handles with delete access

**Status: Fixed.**

**Location:** [Win32KernelService.cs:32,42,94](source/Brows.Win32.Interop/Win32/Win32KernelService.cs#L32).

Before the fix, all three metadata opens specified `FileShare.ReadWrite` and omitted `FileShare.Delete`. A file or directory already opened with delete access could not be opened with this sharing mask, even when the requested metadata was available. This broke identity and case-sensitivity queries around common rename/delete-capable handles.

**Reproduced:** Holding a valid `DELETE`-access handle with read/write/delete sharing made `PathsAreEquivalent(file, file)` fail with a sharing violation.

**Fix applied:** All three inspection handles now allow read, write, and delete sharing. See the [CreateFileW sharing rules](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew). No tests or build were run for this change.

### 8. [P2] File identity checks unnecessarily request file data access

**Status: Fixed.**

**Location:** [Win32KernelService.cs:31,41](source/Brows.Win32.Interop/Win32/Win32KernelService.cs#L31), [kernel32.cs:47–54](source/Brows.Win32.Interop/Win32/PlatformInvoke/kernel32.cs#L47).

Before the fix, `FileAccess.Read` was passed directly as a native access mask. Its numeric value is `1`, which is `FILE_READ_DATA`, rather than the .NET meaning of an abstract read mode. File identity inspection does not need permission to read file contents. The extra access caused avoidable sharing and permission failures.

**Reproduced:** With an exclusive writer open, a metadata-only `CreateFileW` call succeeded, while `PathsAreEquivalent` threw a sharing violation.

**Fix applied:** The identity handles now request zero access, which is sufficient for the metadata query, and `CreateFileW` accepts a raw native `uint` access mask rather than `System.IO.FileAccess`. This avoids interpreting .NET's `FileAccess.Read` value as `FILE_READ_DATA`. See [CreateFileW metadata access](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew). No tests or build were run for this change.

### 9. [P2] File identity comparison can falsely equate different ReFS files

**Status: Fixed.**

**Location:** [Win32KernelService.cs:50–68](source/Brows.Win32.Interop/Win32/Win32KernelService.cs#L50).

Before the fix, the method compared only the volume serial number and the legacy 64-bit file index from `BY_HANDLE_FILE_INFORMATION`. Microsoft explicitly states that this 64-bit identifier is not guaranteed unique on ReFS, which uses 128-bit file identifiers. The method could therefore report that distinct ReFS files were equivalent.

**Evidence:** A documented filesystem limitation, not a collision reproduced on this machine.

**Fix applied:** The method now compares the volume serial and full 128-bit file ID from `GetFileInformationByHandleEx(FileIdInfo)`. If that query fails, it uses the legacy 64-bit identifier only when both handles are confirmed to be on NTFS. See [FILE_ID_INFO](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_id_info) and [BY_HANDLE_FILE_INFORMATION](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/ns-fileapi-by_handle_file_information). No tests or build were run for this change.

### 10. [P2] IO_STATUS_BLOCK has the wrong layout in a 32-bit process

**Status: Fixed.**

**Location:** [IO_STATUS_BLOCK.cs:5–9](source/Brows.Win32.Interop/Win32/PlatformInvoke/IO_STATUS_BLOCK.cs#L5), [ntdll.cs:8–13](source/Brows.Win32.Interop/Win32/PlatformInvoke/ntdll.cs#L8).

Before the fix, the managed declaration used a 32-bit status followed by `ulong`, producing 16 bytes with `Information` at offset 8 on x86. Native output was written into the wrong managed field location.

**Reproduced before the fix:** In an explicitly verified 32-bit process (`IntPtr.Size == 4`), `Marshal.SizeOf` returned 16 and `Marshal.OffsetOf(Information)` returned 8. The old layout happened to match the relevant offsets on x64.

**Impact limit:** The current case-sensitivity method does not read `Information`, so this defect does not by itself demonstrate a wrong case-sensitivity result or a memory overwrite outside the supplied structure. It is nevertheless an incorrect native output contract.

**Fix applied:** The status union and `Information` now use pointer-sized `IntPtr` and `UIntPtr` fields, matching the native layout on x86 and x64. The current call site does not read the status field. See the [native IO_STATUS_BLOCK definition](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/wdm/ns-wdm-_io_status_block). No tests or build were run for this change.

### 11. [P2] Case-sensitivity query failures are silently reported as false

**Status: Fixed.**

**Location:** [Win32KernelService.cs:112–146](source/Brows.Win32.Interop/Win32/Win32KernelService.cs#L112).

Before the fix, the switch returned false for every unrecognized NTSTATUS, including genuine access or I/O failures. An unsuccessful query was therefore indistinguishable from a directory explicitly configured as case insensitive. Callers that use this result to compare names could incorrectly collapse distinct case-sensitive entries.

Before the fix, the handle was opened with access zero, while Microsoft's contract for `FileCaseSensitiveInformation` specifies `FILE_READ_ATTRIBUTES`. Both access zero and explicit read-attributes access succeeded on this machine; a universal failure from access zero is **not** claimed here.

**Fix applied:** The handle now requests `FILE_READ_ATTRIBUTES`. Only `STATUS_NOT_IMPLEMENTED`, `STATUS_NOT_SUPPORTED`, and `STATUS_INVALID_INFO_CLASS` return the compatibility fallback value `false`; other statuses throw `IOException` with the NTSTATUS value. See [NtQueryInformationFile](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntifs/nf-ntifs-ntqueryinformationfile). No tests or build were run for this change.

### 12. [P2] Shortcut resolution swallows cancellation after entry

**Status: Fixed.**

**Location:** [Win32ShellService.cs:81–126](source/Brows.Win32.Interop/Win32/Win32ShellService.cs#L81).

Before the fix, a token canceled before entry threw, but cancellation while awaiting `ThreadPool.Work` was caught by `catch (Exception)` and converted to null. The same operation had inconsistent cancellation semantics depending on timing, and callers could not distinguish cancellation from a missing shortcut target.

**Reproduced before the fix:** Occupying a pool's only worker, queuing `GetLinkPath`, and then canceling its token produced null with task state `RanToCompletion`.

**Fix applied:** `GetLinkPath` now rethrows `OperationCanceledException` when its cancellation token has been canceled; other failures retain the existing logged `null` result. No tests or build were run for this change.

### 13. [P2] Shell execution bypasses the STA pool and COM initialization

**Status: Fixed.**

**Location:** [Win32ShellService.cs:18–48](source/Brows.Win32.Interop/Win32/Win32ShellService.cs#L18), [public execution methods:69–79](source/Brows.Win32.Interop/Win32/Win32ShellService.cs#L69).

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

**Fix applied:** The SDK baseline is now `10.0.100`; the existing `feature` roll-forward policy is preserved. Microsoft documents `10.0.100` as a valid SDK version format in [global.json version requirements](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json). No build or tests were run for this change.

### 16. [P3] Two NTSTATUS constants have incorrect numeric values

**Status: Fixed.**

**Location:** [NTSTATUS.cs:653](source/Brows.Win32.Interop/Win32/PlatformInvoke/NTSTATUS.cs#L653), [NTSTATUS.cs:4719](source/Brows.Win32.Interop/Win32/PlatformInvoke/NTSTATUS.cs#L4719).

| Constant | Before-fix value | Correct Windows SDK value |
| --- | --- | --- |
| `STATUS_GRAPHICS_DRIVER_MISMATCH` | `0x401E0117` | `0xC01E0009` |
| `STATUS_PKU2U_CERT_FAILURE` | `0xC000042E` | `0xC000042F` |

Before the fix, the first value also had the wrong severity bits: it represented an informational value instead of the actual error status. Any future comparisons or status classification using these members would be wrong.

**Verified:** Compared 1,784 named constants present in both the enum and Windows SDK 10.0.26100.0 `shared/ntstatus.h`; these were the two numeric mismatches. Neither member is used by the current case-sensitivity query, so no current public-method failure is attributed to them.

**Fix applied:** Both enum values now match Windows SDK 10.0.26100.0. No tests or build were run for this change.

### 17. [P2] A failed package push can be hidden by a later successful push

**Status: Open.**

**Location:** [workflow.yml:81–83](.github/workflows/workflow.yml#L81).

The publishing loop does not check each `dotnet nuget push` exit code. With normal PowerShell native-command error behavior, failure to publish the first package followed by successful publication of the second leaves a zero final exit code. The step can appear successful despite a partial release. Fixing finding 14 makes this path reachable.

**Evidence:** Code inspection plus GitHub's documented PowerShell wrapper, which propagates the final native exit code. No packages were published during this review.

**Fix:** Check `$LASTEXITCODE` immediately after each push and throw on failure, or explicitly enable native-command failure propagation. See [GitHub Actions shell exit-code behavior](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#exit-codes-and-error-action-preference).

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

## Verification and limits

- Initial full build: both libraries and both test projects built in Release for `net462`, `net48`, `net8.0-windows`, and `net10.0-windows` with **zero errors** and 212 warnings, including documentation and local SourceLink warnings.
- Initial test run completed successfully but executed **no test cases**. In the 2026-10-03 follow-up, the solution test command discovered and passed 16 cases per test project for all four target frameworks.
- Follow-up Release build of the Interop project succeeded for all four target frameworks with **zero errors** and no `CS1570` warnings; four unrelated `CS8981` lowercase-name warnings remain.
- Ran disposable Win32/COM probes against workspace fixtures in .NET 10 x64 and x86 before the callback fix. Confirmed findings 4, 6, 7, 8, 10, and 12 through the observations above. The updated HRESULT conversion was compile-checked after the fix.
- Verified that `IFileOperationProgressSink` marshals and `IFileOperation.Advise` returns `S_OK` despite the internal types and assembly-level `ComVisible(false)`. Those visibility attributes are **not** reported as a defect.
- Verified successful Shell shortcut resolution and successful silent file creation. Compared `IFileOperation` and `IFileOperationProgressSink` GUIDs, method order, parameter widths, Unicode strings, and BOOL output against the installed SDK. No vtable-order mismatch was found in these declarations. Releasing a queued source item's managed wrapper before `PerformOperations` is not itself a finding: COM retains the native references needed by the queued operation.
- Checked native structures and constants against Windows SDK 10.0.26100.0 and consulted the linked Microsoft API contracts. ReFS collisions, older Windows behavior, third-party Shell extensions, and every possible UI scheduling interaction were not reproduced. Findings relying on those contracts are labeled accordingly.

Build/test/probe artifacts were temporary. The review does not claim that compilation or these probes establish correctness for every filesystem, Shell extension, Windows version, or COM implementation.
