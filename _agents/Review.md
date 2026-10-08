# Review

Review of the `GetStoredPath` addition in commit 7368188 ("Add `GetStoredPath`"),
covering `Win32KernelService.GetStoredPath`, the `IWin32InteropServices.GetStoredPath`
facade member and wrapper, the new `FindFirstFileW`/`FindClose`/`WIN32_FIND_DATAW`
interop declarations, and the README updates.

The original review reported building the solution and running a probe harness
against the `net8.0-windows` and `net48` targets (see original evidence below).
Independent assessment checks and decisions are recorded separately in this file.

## Assessment and recommended action

Assessment of the current implementation, 2026-10-08. Issue numbers are retained.
The assessment below is retained as the rationale. Issues 1–4 and 6–7 have now been
implemented and marked resolved; issue 5 remains rejected. See the implementation
checks at the end of this file.

| Issue | Decision | Recommended action |
| --- | --- | --- |
| 1 | Valid, high priority; the absolute wording is incorrect | Use extended-length native queries for normalized ordinary file-system paths, preserving the resolved public result form. Add long-path regression coverage. |
| 2 | Valid coverage gap, medium priority | Add focused kernel and composition contract tests, including regression cases for the accepted behavior fixes. |
| 3 | Valid cross-framework bug, lower priority | Ensure a directory separator is present when joining a drive-qualified device root and a component. |
| 4 | Valid file-system contract bug, medium priority | Reject bare non-file-system device inputs with ArgumentException. Different native failures for different devices are not themselves a defect. |
| 5 | Not a valid-path handling bug as described | Keep extended-path semantics. Clarify the accepted syntax; explicit ArgumentException validation is optional. Do not normalize extended paths automatically. |
| 6 | Valid exception-documentation gap, low priority | Document the unsupported ADS case and the Framework-specific NotSupportedException. Uniform argument validation may be considered separately. |
| 7 | Valid documentation clarification, low priority | Document short-name expansion; preserve the returned stored long names. |

Microsoft documents both the host opt-in for ordinary long paths and the
extended-path syntax restrictions in [Maximum Path Length Limitation](https://learn.microsoft.com/en-us/windows/win32/fileio/maximum-file-path-limitation).
The short-name result follows the native API's documented behavior in
[WIN32_FIND_DATAW](https://learn.microsoft.com/en-us/windows/win32/api/minwinbase/ns-minwinbase-win32_find_dataw).

## Issues

### Issue 1: Ordinary long paths fail in hosts without long-path opt-in — accepted — Resolved

Assessment: fix the implementation rather than only documenting MAX_PATH. The defect is dependence on host configuration, not an unconditional failure for every long path. A fresh probe succeeded for a plain 364-character path under Windows PowerShell and failed under PowerShell 7; both accepted its extended form. Normalize ordinary inputs before adding a prefix to native queries so ordinary path semantics remain intact. Do not strip or reinterpret an explicitly supplied extended prefix.

`GetStoredPath` builds each per-component query with
`kernel32.FindFirstFileW(Path.Combine(storedPath, component), ...)` at
`source/Brows.Win32.Interop/Win32/Win32KernelService.cs:103-108`, using the plain
(extended-form-less) accumulated path. `FindFirstFileW` caps `lpFileName` at
`MAX_PATH` unless the query is in extended form (`\\?\` or `\\?\UNC\`) or the host
process is long-path aware. A library cannot assume the host opted in. Whether a particular host is long-path
aware depends on its executable manifest and the system setting, not the library target framework alone.

Verified on this machine (Windows 11, `LongPathsEnabled = 1`) on both `net8.0-windows`
and `net48` targets:

- A 359-character path to an existing file: `GetStoredPath` throws
  `Win32Exception` ("The system cannot find the path specified"), while
  `File.Exists` on the same plain path returns `true` (.NET's own managed IO
  works because the runtime prefixes `\\?\` internally).
- The same path passed with a `\\?\` prefix resolves correctly and returns the
  stored casing.

This contradicts the documented contract
(`source/Brows.Win32.Interop/README.md:37`: "The path and its components must exist
and be accessible"): the path exists and is accessible, yet the method can fail in hosts without long-path opt-in.
Deep directory trees make this reachable for a file-manager consumer, and the
`\\?\` workaround is caller-side knowledge the docs do not mention.

Resolution: build the per-component queries in extended form (`\\?\` plus the
drive-absolute stored path, `\\?\UNC\` plus `server\share` for UNC roots) while
returning the result in the caller's original form. Alternatively, document the
`MAX_PATH` limitation in the method remarks and both READMEs.

### Issue 2: No test coverage for `GetStoredPath` — accepted — Resolved

Assessment: add coverage. This is a verification gap, not proof that cancellation or disposal is currently broken: the new method uses the existing BeginOperation/EndOperation and UseServices lifecycle mechanisms. Prioritize stored spelling, missing paths, invalid inputs, long paths, disposal, facade cancellation, and shutdown. Use an isolated temporary directory; do not rely on global machine paths or installed short-name aliases.

The commit adds a public kernel method and a composition facade member with zero
tests on any target framework:

- `tests/Brows.Win32.Interop.Tests/Win32/Win32KernelServiceTest.cs` has no
  `GetStoredPath` tests, and `PublicMethods_AfterDispose_ThrowObjectDisposedException`
  (lines 67-78) was not extended to cover the new method.
- `tests/Brows.Win32.Interop.Composition.Tests/Win32/KernelShutdownTest.cs`
  establishes a per-method pattern (existing-path result, missing path throws,
  canceled token returns canceled task, after-kill throws `InvalidOperationException`)
  that was not extended to `GetStoredPath`.

All 202 currently passing tests predate the commit. The repository guidance requires
regression coverage for behavior changes, and issue 1 would have been caught by a
long-path test.

Resolution: add kernel tests (stored casing for a file and a directory; relative-path
resolution; missing component throws `Win32Exception`; `null` throws
`ArgumentNullException`; wildcards throw `ArgumentException`; root-only path; after
`Dispose` throws `ObjectDisposedException`) and composition tests (result through the
facade; pre-canceled token returns a canceled task; after `Kill` throws
`InvalidOperationException`).

### Issue 3 (accepted): `Path.Combine` volume-separator pitfall produces a malformed query for `\\.\C:` roots on the .NET Framework targets — Resolved

Assessment: confirmed against the existing net48 and net10.0-windows binaries. The Framework root is missing the final separator and Path.Combine constructs a drive-relative form. This is real if drive-qualified device paths are accepted, although less common than ordinary drive or UNC input. Fix the join; do not broadly reinterpret every device namespace as a filesystem path.

The component joins at `source/Brows.Win32.Interop/Win32/Win32KernelService.cs:104`
and `:110` use `Path.Combine`, which suppresses the appended separator when the first
argument ends with a volume separator (`:`). On the `net462`/`net48` targets,
`Path.GetPathRoot(@"\\.\C:\Windows")` returns `\\.\C:` without a trailing separator
(the `net8.0`/`net10.0` targets return `\\.\C:\` with one), so:

- `Path.Combine(@"\\.\C:", "Windows")` yields the malformed `\\.\C:Windows`
  (verified on `net48`), and `GetStoredPath(@"\\.\C:\Windows")` queries
  `FindFirstFileW` with that string and throws `Win32Exception` ("The filename,
  directory name, or volume label syntax is incorrect") for an existing directory.
- The same input succeeds on `net8.0`/`net10.0`, so the method behaves differently
  across the library's own targets.

Resolution: do not rely on `Path.Combine`'s colon handling for the join. Ensure
`storedPath` ends with `Path.DirectorySeparatorChar` before appending each component
(or append the separator and component explicitly) at both join sites.

### Issue 4 (accepted with qualification): DOS-device names are handled inconsistently; `nul` is returned as a "stored path" — Resolved

Assessment: confirmed that nul returns a device path. That successful result contradicts the stated file-system contract. Reject bare device objects rather than declaring them supported stored filesystem paths. The different CON and COM1 errors are expected consequences of different device types, accessibility, and machine configuration; they need not have identical native error codes. Namespace validation must still allow supported drive/share roots and distinguish device objects from actual files with reserved-looking names in the extended namespace.

The root-only branch at `source/Brows.Win32.Interop/Win32/Win32KernelService.cs:82-96`
validates the root with `CreateFileW` alone. `Path.GetFullPath` maps DOS-device names
to `\\.\` device paths and `Path.GetPathRoot` treats them as complete roots, so the
branch validates a device object rather than a file-system root. Verified identically
on `net8.0` and `net48`:

- `GetStoredPath("nul")` returns `\\.\nul`: `CreateFileW` opens the NUL device, the
  existence check passes, and the input is returned as the result. This violates the
  documented result ("an absolute path with the stored casing of each existing
  file-system component" at `source/Brows.Win32.Interop/Win32/Win32KernelService.cs:42`;
  there are no file-system components beneath a device). `\\.\PhysicalDrive0` is
  similarly passed through.
- `GetStoredPath("CON")` throws `Win32Exception` ("The parameter is incorrect") and
  `GetStoredPath("COM1")` throws `Win32Exception` ("The system cannot find the file
  specified"): three device names, three different outcomes.

Resolution: reject bare non-file-system device objects with ArgumentException,
while preserving supported file-system roots. The successful NUL result should be
prevented; differing native errors for other devices are not the problem.

### Issue 5: Extended input is not normalized — rejected as a valid-path bug

Assessment: the observed failures are real, but the premise that these are valid extended paths is incorrect. Microsoft explicitly excludes forward-slash separators and current/parent-directory notation from extended-path navigation. Path.GetFullPath preserving these strings is intentional. Do not strip the prefix and normalize automatically, because extended syntax also preserves names that ordinary Win32 normalization changes (including trailing spaces and periods). Documentation may clarify the restriction; ArgumentException validation would improve diagnostics but is not required to support these invalid navigation examples.

`Path.GetFullPath` at `source/Brows.Win32.Interop/Win32/Win32KernelService.cs:71`
performs no normalization for `\\?\`-prefixed paths, and the component split at `:98`
splits only on `\`. Consequences, verified on both `net8.0` and `net48`:

- `GetStoredPath(@"\\?\c:\windows/system32/cmd.exe")` throws `Win32Exception`
  (ERROR_INVALID_NAME) although the file exists: `windows/system32/cmd.exe` is
  treated as one component containing `/`, whereas the same file addressed without
  the prefix (`C:/windows/system32/cmd.exe`) resolves correctly.
- `Path.GetFullPath(@"\\?\C:\foo\..\bar")` returns the input unchanged on both
  runtimes, so the queries receive literal `..` components, which extended paths
  do not resolve.

Resolution after assessment: no normalization fix is required. Document that
extended input follows Windows extended-path syntax. Optionally reject unsupported
separator/navigation syntax with ArgumentException for clearer diagnostics. The
original suggestion to normalize these inputs automatically is not recommended.

### Issue 6 (accepted as a documentation gap): `net462`/`net48` throw undocumented `NotSupportedException` for colon (ADS) paths — Resolved

Assessment: confirmed NotSupportedException on net48 and Win32Exception on the modern target. ADS lookup is outside this method's component-enumeration capability; this is an unsupported-input and exception-contract issue, not evidence that stored casing for an ordinary file is wrong. Document the limitation and exception. If a uniform ArgumentException contract is desired, validate unsupported stream syntax consistently rather than catching unrelated native failures.

On the .NET Framework targets, `Path.GetFullPath` throws
`NotSupportedException` ("The given path's format is not supported") when a path
contains a second colon, such as an alternate data stream name
(`C:\dir\file.txt:stream`). Verified: `GetStoredPath(dir + "\file.txt:stream")`
throws `NotSupportedException` on `net48` but the documented `Win32Exception` on
`net8.0`. `NotSupportedException` is not among the exceptions documented for the
method (`source/Brows.Win32.Interop/Win32/Win32KernelService.cs:47-58` and
`source/Brows.Win32.Interop.Composition/Win32/IWin32InteropServices.cs:128-142`).

Resolution: catch `NotSupportedException` from `GetFullPath` and rethrow it as
`ArgumentException` to match the documented "path is invalid" case, or document the
exception for the `net462`/`net48` targets.

### Issue 7 (accepted as a documentation clarification): Short-name (8.3) components are resolved to long names without documentation — Resolved

Assessment: confirmed PROGRA~1 expands to Program Files on this machine. Returning cFileName is correct and should stay. The existing returns text already says stored spelling, so this is a modest documentation ambiguity in the casing description, not a behavioral bug. Add the short-name expansion sentence; tests should account for systems where 8.3 aliases are disabled.

`FindFirstFileW` returns the long name (`cFileName`) when queried with an 8.3 short
name, so `GetStoredPath(@"C:\PROGRA~1")` returns `C:\Program Files` (verified on both
targets). The result then differs from the input by more than casing, while the
documented contract (`source/Brows.Win32.Interop/Win32/Win32KernelService.cs:44-45`,
`source/Brows.Win32.Interop/README.md:37`,
`source/Brows.Win32.Interop.Composition/README.md:53-57`) implies only the casing
changes. This is reasonable behavior, but callers that map the result back onto
their input string (for example, substring replacement) can be surprised.

Resolution: add a remark sentence to the method documentation and both READMEs stating
that short-name components are resolved to their stored long names.

## Checks performed

- `dotnet restore`, `dotnet build --configuration Release` (all four target
  frameworks) — success; 8 pre-existing warnings, no errors.
- `dotnet test --configuration Release` across the solution — 202 tests passed,
  0 failed (none cover `GetStoredPath`).
- Probe harness against the built `net8.0-windows` and `net48` targets exercising
  `GetStoredPath` and the supporting `Path.*` behaviors referenced in the issues
  above (run from a scratch project outside the repository).

## Checks for this assessment (2026-10-08)

- Inspected current implementation, native declarations and struct layout, both
  READMEs, facade documentation and wrapper, base-service lifecycle, and the two
  relevant test fixtures. No GetStoredPath tests are present.
- Reproduced root joining, device inputs, invalid extended syntax, ADS exceptions,
  and short-name expansion using the existing net48 binary in Windows PowerShell
  and the existing net10.0-windows binary in PowerShell 7.6.6. These are host-specific
  probes, not an independent net8.0 or net462 validation.
- A 364-character existing file path in an isolated repository temporary directory
  succeeded without a prefix under Windows PowerShell, failed without a prefix
  under PowerShell 7, and succeeded with an extended prefix in both hosts. Temporary
  files were removed. An earlier probe in the tool's private temporary directory
  hit access-denied errors for both forms and was excluded from the path-length
  conclusions.
- Checked Microsoft documentation for long-path opt-in, extended syntax, device
  namespaces, Path.GetFullPath, and FindFirstFileW.
- No implementation changes, new build, or NUnit run were performed for this
  assessment. The earlier build/test results above are retained from the original
  review and were not rerun.

## Implementation and validation (2026-10-08)

- Branch: `fix/get-stored-path`.
- Issues 1 and 3: ordinary drive/UNC native queries now use extended-length roots,
  while the returned path keeps the resolved input root form. Explicit prefixes
  are preserved. Component joins include a directory separator, including the
  Framework device-drive root case. Ordinary component trimming is retained
  before prefixed queries; explicitly extended literal names are preserved.
- Issue 4: non-file-system device roots are rejected with ArgumentException before
  any device is opened. Drive/share roots remain supported; literal reserved names
  beneath an explicitly extended filesystem root are not treated as devices.
- Issue 2: added kernel coverage for stored file/directory spelling, relative paths,
  missing components, null/invalid/wildcard inputs, root-only inputs, long ordinary
  and extended paths, device-drive roots/paths, bare device rejection, ordinary
  trimming, literal extended names, and disposal. Added facade coverage for
  results, missing paths, pre-cancellation, and shutdown.
- Issues 6 and 7: documented unsupported alternate data streams and their
  Framework-specific exception, runtime length validation, short-name expansion,
  and long/extended path behavior in both public API contracts and READMEs.
- Red/green checks: the original Framework implementation failed the device-drive
  path/root and bare-device regressions. The original modern implementation failed
  the bare-device regressions. Long-path tests already passed in the opted-in test
  host, so a separate PowerShell 7 probe reproduced the original ordinary long-path
  failure and confirmed the fixed binary succeeds for the same existing path.
  An additional regression caught and then verified preservation of ordinary
  intermediate-component trimming when extended native prefixes are added.
- Focused kernel and facade tests passed after the fixes.
- `dotnet build brows-win32-interop.slnx --no-restore --configuration Release`: passed
  for net462, net48, net8.0-windows, and net10.0-windows, with existing warnings only.
- `dotnet test brows-win32-interop.slnx --no-restore --configuration Release --no-build`:
  312 passed, 0 failed, 0 skipped across the four target frameworks.
- Final content/diff review and whitespace checks passed. No new public API was
  added. Generated probe files and caches were removed.
- Live UNC-share integration was not exercised. Volume-GUID paths resolved on
  the modern runtime; Windows PowerShell Framework path parsing supplied no root
  for those paths, an existing host path-handling limitation rather than a change
  introduced here. Runtime path handling requirements are documented.
