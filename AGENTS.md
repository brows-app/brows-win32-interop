# Repository guidance

This file applies to the entire repository. Read the relevant project README and
nearby implementation and tests before making changes.

## Solution layout

- `brows-win32-interop.slnx` is the solution containing the three package
  projects and their three matching test projects.
- `source/Brows.Win32.Interop/` contains the core Win32 and COM interop package.
  `Win32/` holds the higher-level services, `Win32/PlatformInvoke/` contains
  native declarations and structures, and `Win32/InteropServices/` contains COM
  wrappers and the `ComTypes/` declarations. Its `README.md` documents the
  package and public services.
- `source/Brows.Win32.Interop.Composition/` contains the Brows.Composition
  exports for the core services and batched file operations. Its `README.md`
  documents package use.
- `source/Brows.Win32.Interop.Operations/` contains batched Windows Shell file
  operations. `Win32/Win32FileOperation.cs` coordinates a batch,
  `Win32/Win32FileOperations/` contains the operation item types, and
  `Win32/Win32ProgressSink.cs` handles Shell progress callbacks. Its `README.md`
  documents package use.
- `tests/Brows.Win32.Interop.Tests/Win32/` contains tests for the core services.
- `tests/Brows.Win32.Interop.Composition.Tests/Win32/` contains tests for the
  Composition exports.
- `tests/Brows.Win32.Interop.Operations.Tests/Win32/` contains file-operation
  tests, with item tests under `Win32/Win32FileOperations/`.
- `Directory.Build.props` and `Directory.Packages.props` hold shared build
  settings and centrally managed package versions. `source/Directory.Build.props`
  and `tests/Directory.Build.props` add settings for those project groups.
  `global.json` selects the SDK, and `.github/workflows/workflow.yml` defines CI.
- `samples/` currently contains shared build properties only; there are no sample
  projects in the solution.

Inspect `git status` and existing diffs before editing. Preserve existing work;
do not overwrite or revert unrelated changes. Keep unrelated changes out of
fixes and commits, and stage only the changes belonging to the requested task.

## Preserve existing work

- When working in a Git repository, inspect its status and existing diffs before
  editing. Preserve existing work, avoid reverting unrelated changes, and keep
  changes scoped to the requested task.
- Do not commit generated build or test output, packages, or IDE state unless the
  task specifically requires them.

## Build and test

Identify the solution, affected projects, and target frameworks before building.
Use the SDK selected by `global.json` when present, and the operating system,
targeting packs, and runtimes required by the affected projects.

Build and test on Windows; the packages call Windows APIs at runtime. Use the SDK
selected by `global.json` and the solution's documented commands from the
repository root:

```powershell
dotnet restore brows-win32-interop.slnx
dotnet build brows-win32-interop.slnx --no-restore --configuration Release
dotnet test brows-win32-interop.slnx --no-restore --configuration Release --no-build
dotnet pack brows-win32-interop.slnx --no-restore --configuration Release --no-build
```

For focused iteration, select the affected test project and target framework.
Use test filters to run the relevant fixture or test when supported by the
repository's test runner.

For code changes, run relevant tests and build all affected target frameworks.
Use full solution validation when changing shared build settings, dependencies,
or behavior spanning projects. Documentation-only changes need a content and
diff review. Report the checks performed and any validation that could not run.

For efficient testing, run the affected test first, then its fixture or project,
before broader validation required by the change. Confirm that a regression test
fails because of the reported bug, rather than a build or environment problem.
Use `--no-build` only when the binaries include the latest code changes.

## Code and project conventions

- Follow `.editorconfig`: four spaces for C#; two spaces for
  project XML, XAML, JSON, and Markdown. C# and project/XAML files use UTF-8 with
  BOM; Markdown uses UTF-8 without BOM. Keep C# and XAML lines within the
  configured 120-column guideline.
- Use file-scoped namespaces and opening braces on the same line. Follow the
  configured C# style; prefer `var` over explicit type names for local variables.
  Use PascalCase for types and members and an `I` prefix for interfaces.
- Never prefix type names with their namespace. Always use `using` statements
  to import namespaced types.
- Method parameters of type `CancellationToken` must always come last in the
  parameter list.
- Use the configured C# language version. APIs must be available on every
  framework targeted by the affected project. Preserve framework-specific
  conditions and compatibility shims; a newer language version does not supply
  newer runtime APIs.
- Check shared build settings and global usings before adding project-specific
  settings or imports. Preserve existing build-property import structures.
- When central package management is configured, manage dependency versions in
  `Directory.Packages.props` and omit versions from project package references.
  Keep dependency changes focused on the task.
- Do not add to the public API unless explicitly instructed to do so. Keep new
  types and members internal or private when the requested change permits it.
- Declare classes either `sealed` or `abstract`. Avoid concrete classes that
  can be extended.
- Prefer immutable types. When state mutation is required, keep it private. Prefer
  read-only properties supplied through constructors and init-only properties.
  Pure data objects should be `record` types.  
- Put each top-level type in a separate file named after the type. Keep closely
  related helper types nested with their owning type when that matches the
  existing design.
- Namespaces must match the directory structure. The namespace of a type is the
  project's root namespace combined with the folders that contain the type's
  file, relative to the project root.
- Update public API documentation, relevant READMEs, and usage examples when
  changing the documented contract. Do not add XML documentation comments to
  private or internal types or members.
- Keep changes scoped. Avoid unrelated formatting, framework, package-version,
  or release-workflow changes. Do not commit generated build/test output,
  packages, or IDE state such as `out/`, `bin/`, `obj/`, and `.vs/`.

Within a C# type, use this member order, omitting items that do not exist:

1. Static constructor.
2. Private constants.
3. Private fields.
4. Private events.
5. Private properties.
6. Private instance constructors.
7. Private methods.
8. Internal constants.
9. Internal events.
10. Internal properties.
11. Internal instance constructors.
12. Internal methods.
13. Protected constants.
14. Protected events.
15. Protected properties.
16. Protected instance constructors.
17. Protected methods.
18. Public constants.
19. Public fields, only where interop layout or framework conventions require them.
20. Public events.
21. Public properties.
22. Public instance constructors.
23. Public methods.
24. Explicit interface implementations, after everything else.

### Properties

Prefer compiler-generated backing fields. In C# 14 or later, use the `field`
keyword when a property needs custom accessor logic but can use an automatic
backing field. Keep accessors without custom logic auto-implemented, and do not
declare a separate backing field for these properties. For a custom setter,
validate `value` and assign it to `field` after validation:

```csharp
using System;
using System.Threading;

namespace MyNamespace;

internal sealed class MyNewClass {
    private readonly
#if NET9_0_OR_GREATER
        Lock
#else
        object
#endif
        Locker = new();

    private void MyPrivateMethod() {
        /*
         * This is the comment style to use. Prefer this style
         * when writing comments.
         */
        lock (Locker) {
            Console.WriteLine("Hello, World!"); // Only use this style of comment
                                                // when a single line needs clarification.
        }
    }

    internal string MyInternalString {
        get;
        set {
            bool valueIsNull = value is null;
            if (valueIsNull) {
                throw new ArgumentNullException(nameof(MyInternalString));
            }
            field = value;
        }
    } = string.Empty;

    internal void MyInternalMethod() {
    }

    /// <summary>
    /// Gets the string supplied when this instance was created.
    /// </summary>
    public string MyPublicString { get; }

    /// <summary>
    /// Gets or sets a string that is initialized when first read.
    /// </summary>
    public string MyStringWithAutoBackingField {
        get => field ??= "Hello, World!";
        set;
    }

    /// <summary>
    /// Gets or sets a non-negative number. The default value is 100.
    /// </summary>
    public double MyDoubleWithDefault {
        get;
        set {
            if (value < 0) {
                throw new ArgumentOutOfRangeException(
                    nameof(MyDoubleWithDefault),
                    value,
                    "The value must not be negative.");
            }

            field = value;
        }
    } = 100;

    /// <summary>
    /// Initializes a new instance of the <see cref="MyNewClass"/> class.
    /// </summary>
    /// <param name="myPublicString">
    /// The string to expose through <see cref="MyPublicString"/>.
    /// </param>
    public MyNewClass(string myPublicString) {
        MyPublicString = myPublicString ?? throw new ArgumentNullException(nameof(myPublicString));
    }

    /// <summary>
    /// Performs the public operation.
    /// </summary>
    public void MyPublicMethod() {
    }
}
```

For synchronization fields, use `System.Threading.Lock` when the target
framework is `net9.0` or greater. In multi-targeted code, use
`NET9_0_OR_GREATER` to select `Lock` for those targets and `object` for earlier
targets, as shown above.

Use a manually declared field when an automatic backing field would make the
code more complicated, such as when the field must be `volatile`.

### C# style

- Follow the target repository's `.editorconfig` and nearby code. The repositories
  commonly use four spaces for C# indentation and put opening braces on the same
  line; preserve the project's configured line endings and other local settings.
- Use block-bodied methods and constructors with opening and closing braces. Do
  not use expression-bodied methods (`=>`). Expression-bodied properties and
  accessors are fine when they keep the property clear.
- Always use braces around statement bodies, including `if`/`else`, `do`, `for`,
  `foreach`, `while`, `using`, and `lock`, even when the body is one statement.
- Keep simple conditions inline. For compound or complex `if` or `while` conditions,
  calculate a descriptively named `bool` variable beforehand; update or recompute it
  explicitly inside loops.

  > Examples of acceptable and unacceptable conditions:

  ```csharp
  /*
   * These simple conditions are acceptable inline.
   */
  if (arg is null) { }
  if (num >= 0) { }
  if (string.IsNullOrEmpty(str)) { }

  /*
   * Calculate compound conditions beforehand and give them a descriptive name.
   */
  var inputIsInvalid = input is null || number < 0 || string.IsNullOrEmpty(text);
  if (inputIsInvalid) { }
  ```

- Prefer early `return` over `else` blocks.
- Check reference values for null with `is null` and `is not null`. Prefer these
  patterns over `== null` and `!= null`.
- Prefer `/* ... */` block comments, with ` * ` at the start of each interior
  line. Use `//` only for a single-line clarification; align a continuation line
  with the comment when needed.
- In XML documentation, put each element's opening tag, content, and closing tag
  on separate lines. Document public APIs; do not add XML documentation comments
  to private or internal members.
- Use PascalCase for types and members, and prefix interfaces with `I`. Follow
  the repository's member ordering and `var` preferences. Prefer private
  `readonly` fields where possible, keep fields non-public, and put each type in
  a separate file named after the type.
- Prefer file-scoped namespaces where supported by the project, and declare
  closed concrete classes `sealed`.

## Tests

The source projects have matching test projects. The test project for each source
project has a name equal to the name of the source project, plus `.Tests`. For
example, a source project called `My.Foo.Bar` would have a corresponding test project
called `My.Foo.Bar.Tests`.

The test fixtures within each test project are named according to the type that
the test fixture tests. The name of the test-fixture class is equal to the name
of the tested type, plus `Test`. For example, a test fixture named `MyFooBarTest`
has tests for the type `MyFooBar`.

A test-fixture class is placed in the folder that mirrors the folder of the
tested type within its source project, and the namespace of a test-fixture
class is the same as the namespace of the type it tests.
 
 Follow the existing NUnit and Moq style when writing tests.

- Use the test framework, assertion style, and helpers already used by the affected
  projects.
- Add regression coverage for behavior changes. Confirm that a regression test
  fails because of the reported defect, rather than because of a build or
  environment problem.
- Prefer explicit task signals with bounded waits over timing-dependent sleeps.
- Keep tests and shared test helpers compatible with all applicable target
  frameworks.

## Code reviews

When asked to review code, inspect the entire requested scope thoroughly and
identify all bugs and issues you can find. Write the findings to `Review.md` in
the directory `_agents` in the repository root. Number each issue so it can be
referenced later. For every issue, describe the problem and a way to resolve it.
Cite specific file names and line numbers where applicable. Review existing
findings in `Review.md` before editing it so relevant issues are retained or
updated without duplication. Keep review issue numbers stable: never renumber
existing issues or reuse their numbers, including those marked resolved. However,
do not search the Git history for a `Review.md` if one does not exist: in that
case, simply create a new `Review.md` and start with issue #1. Assign each new
issue a number greater than the highest issue number already used in `Review.md`.

> The full path to `Review.md` should be `{root}/_agents/Review.md`.

When asked to fix issues from `Review.md`, work only on the issue numbers
specified in the request. Prefer red/green testing: first add a test that fails
because of each issue, then fix the code, then rerun the previously failing test
to confirm it passes. Once an issue is resolved, keep it in `Review.md` and mark
it as resolved in that issue's heading; do not remove it.

If worktrees are explicitly requested for fixing multiple issues, create a
separate Git worktree for each requested issue number. Place each worktree
directory directly in the parent directory of the original repository root,
as a sibling of that repository. Name it
`_agent-{original-directory-name}-{issue-number}`, where
`original-directory-name` is the name of the original repository's root directory.
Name each worktree branch
`agent/{issue-number}/{short-description}`. Fix, test, and update `Review.md`
for that issue in its own worktree. Commit each issue's changes on
its worktree branch. The first line of the commit message must start with
`Fix #{issue-number}.`, followed by a blank line and a description of what was
broken and how it was fixed. Once the commit is ready, notify the requester
that the worktree branch is ready to be merged.

> The `{short-description}` in branch names should be hyphenated, for example,
> `agent/5/validate-arguments`.

Before reporting completion, review the final diff for unintended changes,
whitespace problems, and accidental public API additions. Check new and untracked
files as well as tracked changes, and confirm that the changes match the request.

Report completion consistently: summarize what changed, list resolved issue
numbers when applicable, and state which tests or checks ran and their results.
Identify any checks that could not run. For worktree fixes, include the branch
name and commit hash that are ready to be merged.

## Planning

When asked to plan code changes, write your plan to `Plan-{short-description}.md`
in the directory `_agents` in the repository root. The `{short-description}` should
be PascalCase, with no spaces, dashes, or underscores in the string.

> For example, the full path to a plan for updating security code would be
> `{root}/_agents/Plan-SecurityUpdate.md`

Your plan should include enough information for another agent with lesser capability
to follow it. Include in the plan the means of accomplishing tasks: you will start
subagents for any tasks that can be logically separated from others, and you will
review their work as it completes and merge their changes into your main worktree
branch for the updates.
