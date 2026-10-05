# Code Review

Review completed on 2026-10-05. The branch diff (`origin/dev...HEAD`) and the working tree were both clean, so the
review covered the entire repository: all files in `Brows.Win32.Interop`, `Brows.Win32.Interop.Operations`, and
`Brows.Win32.Interop.Composition`, the `PlatformInvoke` and `ComTypes` declarations, all three test projects, and the
build settings. Native and platform contract points were verified empirically where practical. Two suspicions were
dismissed by verification and are not listed: the `FILE_INFORMATION_CLASS` numbering (queries with
`FileCaseSensitiveInformation = 71` succeed on this kernel), and the COM vtable orderings of `IFileOperation` and
`IFileOperationProgressSink` (they match the Shell declarations).

P1 means high priority, P2 means medium priority, and P3 means low priority. Findings 1-22 were resolved in earlier
reviews and are not renumbered or reused. The new findings are **23-32**, all open. No production code was changed
during this review.

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

### 24. [P1] File-operation batches are invisible to facade shutdown

**Location:** [Win32InteropServices.cs:130](source/Brows.Win32.Interop.Composition/Win32/Win32InteropServices.cs#L130)
and [Win32FileOperation.cs:298-330](source/Brows.Win32.Interop.Operations/Win32/Win32FileOperation.cs#L298).

`FileOperation` does not check `Killed`, and `Win32FileOperation.Operate` queues its work directly onto the STA pool
without entering the facade's `ActiveFacadeOperations` accounting. `Kill` therefore proceeds while a batch created
before shutdown is still queued or running, then calls `threadPool.Empty()` on the owned pool. Per the
`STAThreadPool.Empty` contract, workers exit their message loops; queued-but-not-started batch work never runs and
its returned task is orphaned. After `Kill`, `FileOperation` also keeps returning batches on a killed pool instead of
throwing `InvalidOperationException` like every other facade method.

Resolve by routing batch execution through the same admission the other facade methods use: track each `Operate` call
in `ActiveFacadeOperations` (or an equivalent registration) so `Kill` waits for admitted batches, and make
`FileOperation` reject calls once `Killed` is set.

### 25. [P1] Owner-window lookup can deadlock the STA worker against the UI thread

**Location:** [Win32WindowHelper.cs:30-46](source/Brows.Win32.Interop/Win32/Win32WindowHelper.cs#L45) and
[Win32WindowHelper.cs:68-79](source/Brows.Win32.Interop/Win32/Win32WindowHelper.cs#L76).

`Win32FileOperation.Work` calls `Win32WindowHelper.GetWindow()` on the STA worker. The WPF path synchronously invokes
the application dispatcher (`invoke.Invoke(dispatcher, ...)`), and the Windows Forms path synchronously invokes
`Control.Invoke`. If the UI thread is blocked waiting on the batch - for example a UI thread that synchronously waits
on `Operate(...).GetAwaiter().GetResult()` during shutdown - the worker waits inside the cross-thread invoke while the
UI thread waits for the worker's batch, producing a permanent deadlock that also wedges any subsequent
`STAThreadPool.Empty()`.

Resolve by not calling `GetWindow()` from the batch at all (require the owner window to be supplied by the caller, who
knows the UI context), or by using a non-blocking mechanism with a timeout (`Dispatcher.BeginInvoke` with a bounded
wait) so a busy or blocked UI thread degrades to `IntPtr.Zero` instead of hanging the worker.

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

### 27. [P2] Unadvise failure in finally replaces the batch's real exception

**Location:** [Win32FileOperation.cs:156-159](source/Brows.Win32.Interop.Operations/Win32/Win32FileOperation.cs#L156).

`Work` ends with `hr = FileOperation.Unadvise(progressSinkCookie); hr.ThrowOnError();` inside a `finally` block. If
the `try` block is already unwinding - `PerformOperations` failed with its HRESULT, or the cancellation check at line
145 threw - and `Unadvise` then fails (which is plausible after an aborted operation), the throw in `finally`
discards the original exception and the caller diagnoses the unadvising failure instead of the real one.

Resolve by not throwing from `finally`: log the `Unadvise` failure (the batch already has an `ILog`) or capture the
HRESULT for the success path only.

### 28. [P2] Cancellation after PerformOperations reports completed work as canceled

**Location:** [Win32FileOperation.cs:142-147](source/Brows.Win32.Interop.Operations/Win32/Win32FileOperation.cs#L142).

`Work` calls `CancellationToken.ThrowIfCancellationRequested()` between `PerformOperations` and
`performHr.ThrowOnError()`. When the token is canceled while the native call runs, the call still completes and the
files are actually copied or moved, but the caller receives `OperationCanceledException`: a retry duplicates the
work, and a genuine failure HRESULT in `performHr` and the `aborted` state are never surfaced.

Resolve by checking `performHr` first and reporting the operation's real outcome: treat the operation as completed
when `PerformOperations` returns success, and only honor a pre-`PerformOperations` cancellation check (line 141)
for the queued phase.

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

### 32. [P3] Dead local and dead null-check in Win32FileOperation

**Location:** [Win32FileOperation.cs:69-72](source/Brows.Win32.Interop.Operations/Win32/Win32FileOperation.cs#L72) and
[Win32FileOperation.cs:30-42](source/Brows.Win32.Interop.Operations/Win32/Win32FileOperation.cs#L37).

`Copy()` computes `var fileDir = Path.GetDirectoryName(path);` and never uses it, implying a destination-directory
behavior that does not exist. In `Iterate`, `act?.Invoke(item)` is a dead null-check because `act` is never null.
Resolve by deleting the `fileDir` line and invoking `act(item)` directly.

## Validation

All source, test, build, and documentation files in the repository were read for this review; no files were changed
except this one. Two native behaviors were exercised directly on this machine: an `NtQueryInformationFile` probe
confirming the repository's `FILE_INFORMATION_CLASS` numbering (class 71 succeeds; classes 65-67 and 72 are
rejected), and a `ShellExecuteExW` failure probe establishing issue 26. The test suite was not executed during this
review; no fix has been made yet, so no regression checks apply. One ShellExecuteExW probe had to be stopped after it
blocked on the Windows "choose an application" dialog, which is also why the review notes the batch mask
`SEE_MASK_FLAG_NO_UI` in `Execute` suppresses that dialog.