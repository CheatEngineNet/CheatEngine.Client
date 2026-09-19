# CheatEngine.Client

A public, fluent, strongly-typed C# 14 / .NET 10 client API, built as a mapper/binder over CheatEngine.SDK.

```powershell
dotnet build CheatEngine.Client.slnx
```

## Layout

The solution folders of `CheatEngine.Client.slnx` mirror the directories below one to one, so a path in Rider's Solution
Explorer is also the path on disk. `/Solution Items/` is the only virtual folder.

```
CheatEngine.Client/
├─ eng/                                  MSBuild profiles, selected by the top-level folder of a project
├─ libs/                                 Small layered libraries
│  ├─ CheatEngine.Client.Abstractions/   Public vocabulary and contracts
│  ├─ CheatEngine.Client.Binding/        Mapper/binder onto CheatEngine.SDK
│  └─ CheatEngine.Client.Fluent/         Fluent public API
├─ src/
│  └─ CheatEngine.Client/                The one project a consumer references
└─ tests/                                One <Project>.Tests twin per project above
```

## Projects

| Project                           | Role                                                                                   | References                          |
|-----------------------------------|----------------------------------------------------------------------------------------|-------------------------------------|
| `CheatEngine.Client.Abstractions` | Strongly-typed public vocabulary, and the contracts between the fluent and the binding | nothing                             |
| `CheatEngine.Client.Fluent`       | Fluent, composable public API                                                          | `Abstractions`                      |
| `CheatEngine.Client.Binding`      | Mapper/binder onto CheatEngine.SDK. Internal by default                                                   | `Abstractions`                      |
| `CheatEngine.Client`              | Composition root: the single project a consumer references                             | `Abstractions`, `Fluent`, `Binding` |

Dependency rules:

- `Abstractions` is the base. `Fluent` and `Binding` never reference each other, and only `CheatEngine.Client` composes
  them.
- `libs/` never references `src/` or `tests/`. A consumer references `CheatEngine.Client` only.
- `Binding` is the only project meant to depend on CheatEngine.SDK (the default rule, to revisit if `Abstractions` reuses CheatEngine.SDK
  vocabulary).
- A test project references its subject only.

## Conventions

- Folder name, project file name, assembly name and root namespace are the same string.
- Every project has a `README.md` next to its project file. The build fails without it (`CHEATENGINECLIENT9001`).
- Every project in `libs/` and `src/` has a twin `tests/<Project>.Tests`, which sees its internals. The `.Tests` suffix is
  reserved for test projects.
- The top-level folder of a project selects its profile: `libs/` and `src/` use `eng/Shipping.props`, `tests/` uses
  `eng/Tests.props`. A project outside these folders has no target framework and does not build.
- `Directory.Build.props` is the only one of the repository: no nested `Directory.Build.*` files.
- Build output goes to `artifacts/`, never inside a project folder.
- No `Common`, `Utils` or `Helpers` folders.

## Solution folders

- One solution folder per directory, with the same path (`/libs/`, `/src/`, `/tests/`, `/eng/`). They stay flat, and
  nest only where the disk nests.
- Entries are sorted by path, case-insensitively. Project sources and project READMEs are not listed. `artifacts/`,
  `.idea/` and `.claude/` are never listed.
- Adding a project: its folder, its `.csproj`, its `README.md`, one `<Project>` line in the matching solution folder, and
  its `.Tests` twin.

## Reserved, not created yet

A folder exists only when it holds a real file: no empty placeholder directories, no `.gitkeep`. A new top-level folder
that holds projects (`samples/`, `benchmarks/`) needs its own profile in `eng/`, an `<Import>` for it in
`Directory.Build.props`, and its own solution folder. `tests/CheatEngine.Client.Tests.Shared/` reuses the `tests/`
profile and the `/tests/` solution folder. `docs/` holds no project, so it only gets a file-only solution folder.

| Location                                 | Created when                                                                       |
|------------------------------------------|------------------------------------------------------------------------------------|
| `samples/`                               | The first public API is worth demonstrating                                        |
| `tests/CheatEngine.Client.Tests.Shared/` | A second test project needs the same fake                                          |
| `benchmarks/`                            | The first performance-sensitive path exists                                        |
| `docs/`                                  | A document no longer fits in a README (`docs/adr/` with the first decision record) |
