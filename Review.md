# Code Review

## 1. [P1] Avoid capturing a caller synchronization context (resolved)

`source/Brows.Win32.Interop.Composition/Win32/Win32InteropServices.cs:41-43` wraps every Shell task in an `async` lambda that awaits without `ConfigureAwait(false)`. When a UI-thread caller synchronously waits on `ExecuteDefault`, `ExecuteProperties`, or a real `.lnk` `GetLinkPath` request, the STA work completes but this wrapper's continuation is posted back to the blocked UI context, so the call hangs. Await the inner task with `ConfigureAwait(false)` (or return the Shell task without an async wrapper) so the facade retains the underlying Shell service's context-free behavior.

Resolution: Added ConfigureAwait(false) to the nongeneric facade adapter. The adapter regression failed with the
original await and passed after the fix on net10.0-windows. Review confirmed GetLinkPath already uses the generic
helper directly, so the original finding's GetLinkPath example does not apply to this adapter.

## 2. [P2] Keep queued kernel queries alive during shutdown (resolved)

`source/Brows.Win32.Interop.Composition/Win32/Win32InteropServices.cs:120` (and the matching query at line 128) queues the actual `Win32KernelService` operation with `Task.Run`, so the service's `BeginOperation` has not happened when the facade returns. If `IImport.Kill()` runs before a saturated thread pool starts that delegate, `Kill` disposes the kernel service and the previously returned query task later fails with `ObjectDisposedException`. Track queued facade operations or otherwise synchronize disposal with these tasks so calls started before shutdown either complete or cancel predictably.

Resolution: Register facade operations under the shutdown lock before dispatch, release registrations in a
context-free async finally, and wait for accepted operations before disposing services. Reject later calls.
Reviewed admission, disposal, cancellation, and fault paths. The held-operation regression failed with the
original untracked helper and passed with tracking restored on net10.0-windows.

## 3. [P3] Document the new public service contract (resolved)

`source/Brows.Win32.Interop.Composition/Win32/IWin32InteropServices.cs:9-19` adds seven public API members without XML documentation, producing CS1591 warnings for every target framework and leaving consumers without the path, cancellation, error, and return-value contract. Add the public XML documentation and any applicable usage documentation required by `AGENTS.md:94-96`.

Resolution: Added XML documentation for the interface and all members, including parameters, results, cancellation,
native errors, and shutdown errors. Added a composition package usage guide and linked it from the root README.
Reviewed the contracts against the underlying kernel and Shell implementations, including null shortcut paths.

## Validation

Reviewed all three fixes and their regression coverage. The composition test project built and passed all nine
tests on each of net462, net48, net8.0-windows, and net10.0-windows (36 total). Both behavior regressions were
confirmed to fail with their original code before passing with the fixes. Final diff and formatting checks passed.
Existing CS1591 warnings on Win32InteropServicesVariable and CS8981 warnings on native binding type names remain.
