# Code Review

Review completed on 2026-10-05. The branch diff (`origin/dev...HEAD`) and the working tree were both clean, so the
review covered the entire repository: all files in `Brows.Win32.Interop`, `Brows.Win32.Interop.Operations`, and
`Brows.Win32.Interop.Composition`, the `PlatformInvoke` and `ComTypes` declarations, all three test projects, and the
build settings. Native and platform contract points were verified empirically where practical. Two suspicions were
dismissed by verification and are not listed: the `FILE_INFORMATION_CLASS` numbering (queries with
`FileCaseSensitiveInformation = 71` succeed on this kernel), and the COM vtable orderings of `IFileOperation` and
`IFileOperationProgressSink` (they match the Shell declarations).

P1 means high priority, P2 means medium priority, and P3 means low priority. Findings 1-22 were resolved in earlier
reviews and are not renumbered or reused. The new findings are **23-32**; all have since been resolved. No production
code was changed during this review itself.

## Findings

### 23. [P1] [Resolved] FileOperation throws ArgumentNullException when no STA pool is configured

**Location:** [Win32InteropServices.cs:130-132](source/Brows.Win32.Interop.Composition/Win32/Win32InteropServices.cs#L130).

`FileOperation(directory)` passed the facade's `ThreadPool` field to
`new Win32FileOperation(directory, ThreadPool)`. That field is `null` unless
`IExportAndVary<Win32InteropServicesVariable>.Vary` has been called, which the `Brows.Composition` host only does when
a variable is supplied. Every other facade method worked in this state: the lazy `ServiceWrapper` was created with a
null pool and `Win32ShellService` fell back to a service-owned pool. `FileOperation` was the one path that threw
`ArgumentNullException(paramName: "threadPool")` instead.

Resolved by rejecting the call with a clear `InvalidOperationException` ("The STA thread pool is null.") instead of
the fallback: the `Brows.Composition` contract demands that `Vary` be called early-on in the process, so using the
facade without it is a contract violation that now fails fast. A `ThreadPoolNotNull` property enforces this in
`FileOperation` and in the lazy `ServiceWrapper` factory (so all facade methods require `Vary`, not just
`FileOperation`), and `Kill` skips emptying a null pool. The composition test fixtures now call `Vary` in their setup
per the contract, and regression coverage locks the behavior in
(`FileOperation_WhenVaryWasNotCalled_ThrowsInvalidOperationException` and `FileOperation_WhenVaryWasCalled_ReturnsOperation`).

### 24. [P1] [Resolved] File-operation batches are invisible to facade shutdown

**Location:** [Win32InteropServices.cs:130](source/Brows.Win32.Interop.Composition/Win32/Win32InteropServices.cs#L130)
and [Win32FileOperation.cs:298-330](source/Brows.Win32.Interop.Operations/Win32/Win32FileOperation.cs#L298).

`FileOperation` does not check `Killed`, and `Win32FileOperation.Operate` queues its work directly onto the STA pool
without entering the facade's active-operation accounting. `Kill` therefore proceeded while a batch created before
shutdown was still queued or running, then called `threadPool.Empty()` on the owned pool. Per the `STAThreadPool.Empty`
contract, workers exit their message loops; queued-but-not-started batch work never ran and its returned task was
orphaned. After `Kill`, `FileOperation` also kept returning batches on a killed pool instead of throwing
`InvalidOperationException` like every other facade method.

Resolved by routing batch execution through the same admission the other facade methods use. `Win32FileOperation` grew
internal `OnOperationStarting`/`OnOperationFinished` callbacks that the facade wires up when it creates a batch:
`OnOperationStarting` runs synchronously in `Operate` before work is queued and rejects the batch with
`InvalidOperationException` once shutdown has started or finished, and `OnOperationFinished` decrements the facade's
active-operation count and pulses the shutdown wait, so `Kill` now waits for in-flight batches the same way it waits
for other facade operations. `Kill` also no longer empties caller-owned pools. Covered by red/green regression tests in
`KernelShutdownTest`: `Kill_WhenFileOperationBatchIsPending_WaitsForItToFinish` (verified red pre-fix by stashing the
production change with the tests in tree) and `Operate_WhenServicesWereKilled_ThrowsInvalidOperationException`. The
batch in the kill test sets `NoConfirmation`, `NoErrorUI`, and `Silent` so the Shell shows no UI during the run;
without those flags the test wedged for minutes on an unattended Shell progress dialog on the desktop, which also
slowed the whole suite. The full test suite passes on all target frameworks after the change.

### 25. [P1] [Resolved] Owner-window lookup can deadlock the STA worker against the UI thread

**Location:** [Win32WindowHelper.cs:30-46](source/Brows.Win32.Interop/Win32/Win32WindowHelper.cs#L45) and
[Win32WindowHelper.cs:68-79](source/Brows.Win32.Interop/Win32/Win32WindowHelper.cs#L76). (The file has since been
deleted by the fix.)

`Win32FileOperation.Work` called `Win32WindowHelper.GetWindow()` on the STA worker. The WPF path synchronously invoked
the application dispatcher (`invoke.Invoke(dispatcher, ...)`), and the Windows Forms path synchronously invoked
`Control.Invoke`. If the UI thread was blocked waiting on the batch - for example a UI thread that synchronously waits
on `Operate(...).GetAwaiter().GetResult()` during shutdown - the worker waited inside the cross-thread invoke while the
UI thread waited for the worker's batch, producing a permanent deadlock that also wedged any subsequent
`STAThreadPool.Empty()`.

Resolved by not calling `GetWindow()` from the batch at all: `Win32WindowHelper` is deleted, and the owner window is
supplied by the caller, who knows the UI context. `Win32FileOperation` exposes an `OnGetOwnerWindow` callback
(defaulting to no owner window) that the composition export passes through from
`Win32InteropServicesVariable.OnGetOwnerWindow`. The XML documentation on both properties and the Operations README
state the contract: the callback runs on the STA worker during `Operate` and must not synchronously wait on a thread
that could be waiting for the batch. Note for follow-up work: while fixing this issue the lifecycle fields of
`Win32InteropServices` were also renamed from `Killed`/`KillFinished` to `Killing` (entered) and `Killed` (finished),
mirroring `Win32BaseService`'s `Disposing`/`Disposed`; the shutdown tests that reflect on those field names were
updated to match. The full test suite passes on all target frameworks after the change.

### 26. [P2] [Resolved] ShellExecuteExW failures produce a misleading Win32Exception

**Location:** [Win32ShellService.cs:35-38](source/Brows.Win32.Interop/Win32/Win32ShellService.cs#L37).

`Execute` threw `new Win32Exception()` without capturing the failure information. This was verified empirically:
after a failed `ShellExecuteExW` call, `Marshal.GetLastWin32Error()` returned 1223 and `hInstApp` contained
`SE_ERR_FNF` (2), yet `new Win32Exception()` reported `ErrorCode` `0x80004005` with the unrelated message "The
system could not find the environment option that was entered". Callers of `ExecuteDefault` and `ExecuteProperties`
could not distinguish failure causes, and the existing tests passed only because they assert the exception type.

Resolved by capturing the call's own documented result: on failure, `hInstApp` holds the `SE_ERR_*` code (values
<= 32), so `Execute` now throws `new Win32Exception((int)hInstApp)` when `hInstApp` is in that range and falls back
to `Marshal.GetLastWin32Error()` only when the Shell did not set one - the ambient thread error is no longer trusted
as the primary source. Covered by `ExecuteDefault_WhenFileDoesNotExist_ReportsShellExecuteResult`, which pins the
reported code to `SE_ERR_FNF` (2). One caveat recorded from the fix work: in the production missing-file path on
this machine the ambient last-error already holds 2, so the old code coincidentally reported the right code there
and a red/green demonstration was not reproducible in that path (a probe of the unassociated-extension
`ERROR_CANCELLED` scenario found that Windows 11 returns success there instead of failing); the regression test
locks the reported code regardless of which source happens to be read.

### 27. [P2] [Resolved] Unadvise failure in finally replaces the batch's real exception

**Location:** [Win32FileOperation.cs:156-159](source/Brows.Win32.Interop.Operations/Win32/Win32FileOperation.cs#L156).

`Work` ended with `hr = FileOperation.Unadvise(progressSinkCookie); hr.ThrowOnError();` inside a `finally` block. If
the `try` block was already unwinding - `PerformOperations` failed with its HRESULT, or the cancellation check threw -
and `Unadvise` then failed (which is plausible after an aborted operation), the throw in `finally` discarded the
original exception and the caller diagnosed the unadvising failure instead of the real one.

Resolved by not throwing from `finally`: the `Unadvise` call and its `ThrowOnError` are wrapped in a `try`/`catch`
that logs the failure through the batch's existing `ILog` as a warning, so the in-flight exception from
`PerformOperations` or the cancellation check always propagates. A deterministic red/green test was not added because
forcing `Unadvise` to fail cannot be made reliable; the fix was verified by inspection and by the full suite passing
on all target frameworks after the change.

### 28. [P2] [Resolved] Cancellation after PerformOperations reports completed work as canceled

**Location:** [Win32FileOperation.cs:142-147](source/Brows.Win32.Interop.Operations/Win32/Win32FileOperation.cs#L142).

`Work` called `CancellationToken.ThrowIfCancellationRequested()` between `PerformOperations` and
`performHr.ThrowOnError()`. When the token was canceled while the native call ran, the call still completed and the
files were actually copied or moved, but the caller received `OperationCanceledException`: a retry duplicated the
work, and a genuine failure HRESULT in `performHr` and the `aborted` state were never surfaced.

Resolved by removing the post-`PerformOperations` cancellation check so the batch reports its real outcome: the
`performHr` and `abortedHr` results are always observed, and only the pre-`PerformOperations` check (line 141)
remains for the queued phase. The existing cancellation coverage (`Operate_WhenTokenAlreadyCanceled_DoesNotRun`)
targets the queued phase and still passes; a deterministic mid-`PerformOperations` cancellation test was not
added because timing a cancel inside the native call cannot be made reliable. The full Operations suite passes on
all target frameworks after the change.

### 29. [P2] [Resolved] A concurrent second Kill returns before shutdown completes

**Location:** [Win32InteropServices.cs:98-101](source/Brows.Win32.Interop.Composition/Win32/Win32InteropServices.cs#L100).

`Kill` started with `if (Killed) { return; }`. When thread A entered `Kill` and blocked in `Monitor.Wait` for active
facade operations, a concurrent thread B entered `Kill`, saw `Killed` was already true, and returned immediately.
Thread B proceeded as if services were disposed, while the `ServiceWrapper` and the facade-owned `STAThreadPool` were
still alive and the pending operation was still running.

Resolved by keying the wait loop on a new "shutdown finished" state (`KillFinished`), mirroring how
`Win32BaseService.Dispose` coordinates on its `Disposing`/`Disposed` pair: `Killed` still means "kill started"
(new operations are rejected, and the first caller waits for active facade operations), `KillFinished` is set in a
`finally` after services and any owned pool are released, and concurrent `Kill` callers now wait on `KillFinished`
before returning. Covered by `KernelShutdownTest.Kill_WhenCalledConcurrently_EveryCallerWaitsForShutdownToFinish`,
which failed before the fix and passes after it.

### 30. [P3] [Resolved] Shortcut extension check matches any extension ending in "lnk"

**Location:** [Win32ShellService.cs:152-155](source/Brows.Win32.Interop/Win32/Win32ShellService.cs#L153).

`GetLinkPath` filtered shortcuts with `ext?.EndsWith("lnk", StringComparison.OrdinalIgnoreCase)`. A file named
`notes.xlnk` passed the filter, so the service queued an unnecessary STA COM lookup (`NameSpace`, `ParseName`,
`GetLink`) that then failed and was swallowed as a null result, wasting an STA round-trip and logging a misleading
lookup-failure warning for a file that was never a shortcut.

Resolved by comparing the full extension: `ext?.Equals(".lnk", StringComparison.OrdinalIgnoreCase)`
(still null-safe), so only real `.lnk` files reach the STA lookup.

### 31. [P3] [Resolved] FileOperationProgressSink is a concrete extendable class

**Location:** [FileOperationProgressSink.cs:6-8](source/Brows.Win32.Interop/Win32/InteropServices/FileOperationProgressSink.cs#L6).

`AGENTS.md` requires under "Code and project conventions": "Declare classes either `sealed` or `abstract`."
`FileOperationProgressSink` was neither. It is never instantiated directly - only the derived `Win32ProgressSink` is -
so its all-`S_OK` default bodies exist only as a base.

Resolved by declaring it `abstract` (its `virtual` members keep their default bodies), which also prevents
accidental direct use of a sink that reports success for every callback.

### 32. [P3] [Resolved] Dead local and dead null-check in Win32FileOperation

**Location:** [Win32FileOperation.cs:69-72](source/Brows.Win32.Interop.Operations/Win32/Win32FileOperation.cs#L72) and
[Win32FileOperation.cs:30-42](source/Brows.Win32.Interop.Operations/Win32/Win32FileOperation.cs#L37).

`Copy()` computed `var fileDir = Path.GetDirectoryName(path);` and never used it, implying a destination-directory
behavior that does not exist. In `Iterate`, `act?.Invoke(item)` was a dead null-check because `act` is never null.

Resolved by deleting the `fileDir` line and invoking `act(item)` directly; `Iterate` now validates its `act` parameter
with `ArgumentNullException` up front, replacing the dead null-propagation with a clear contract check. The full
Operations suite passes on all target frameworks after the change.

## Validation

All source, test, build, and documentation files in the repository were read for this review; no files were changed
except this one. Two native behaviors were exercised directly on this machine: an `NtQueryInformationFile` probe
confirming the repository's `FILE_INFORMATION_CLASS` numbering (class 71 succeeds; classes 65-67 and 72 are
rejected), and a `ShellExecuteExW` failure probe establishing issue 26. The test suite was not executed during this
review; no fix has been made yet, so no regression checks apply. One ShellExecuteExW probe had to be stopped after it
blocked on the Windows "choose an application" dialog, which is also why the review notes the batch mask
`SEE_MASK_FLAG_NO_UI` in `Execute` suppresses that dialog.