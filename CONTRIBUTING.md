# Contributing

Keep library code, user documentation, reproducible scripts and reviewed source contracts in this repository. Public APIs require equivalent Chinese and English XML documentation and the same public contract on all 15 target frameworks.

## Repository boundaries

- `src/`, `samples/`, `tests/`, `smoke/` and `native/`: source code and test fixtures expressed as source.
- `docs/`: installation, usage, API and compatibility documentation for users. `docs/blog/` contains standalone user-facing blog articles, including drafts, and their illustrations.
- `eng/` and `tools/`: build, generation and packaging tooling with usage instructions. Name active tooling by function; keep historical stage acceptance scripts outside the checkout.
- `eng/contracts/`: current generator inputs, public API snapshots, native ABI and dependency locks, and package distribution policy. These files are inputs to maintained tooling; they contain no session authorization, stage plans or execution history.

Keep development plans, stage reports, internal writing notes, cloud session notes, raw logs, test results, downloaded binaries, generated model files and packages outside the checkout. User-facing blog drafts belong in `docs/blog/`; mark their status in its index and keep source material and verification records outside the checkout. Do not require archived development records to build or test a fresh clone. Only published user-facing changes belong in release notes; unpublished candidate diaries are development records.

All scripts default to the sibling `../MIGraphXSharp.Workspace/` directory:

| Path under the workspace | Content |
| --- | --- |
| `artifacts/bin`, `artifacts/obj` | Managed build output and intermediates |
| `artifacts/packages` | Local packages and restore cache |
| `artifacts/models` | Generated model fixtures |
| `artifacts/docs`, `api` | Generated documentation site and API metadata |
| `artifacts/release-evidence`, `test-results` | Local validation output |
| `.cache` | Downloaded header, official runtime audit inputs and other caches |

Set `MIGRAPHX_WORKSPACE_ROOT` to an absolute directory outside the checkout to override the default. Use the same value across build, test, pack and docs commands. Avoid changing only `BaseOutputPath` or `RestorePackagesPath`, which would desynchronize tooling. IDE state such as `.vs/` remains ignored local metadata.

## Build prerequisites

Install the .NET SDK selected by `global.json`, PowerShell 7 and a C compiler. The solution's optional adapter and tests also need the HipSharp source baseline:

```powershell
git clone https://github.com/guojin-yan/HIP-CSharp-API.git ../HipSharpSource
git -C ../HipSharpSource checkout --detach 0c730005c37bacbd4f143b33d1f56e9ce517ac2f
$env:HIPSHARP_REPOSITORY_ROOT = (Resolve-Path ../HipSharpSource).Path
.\eng\build.ps1 -Configuration Release
```

You may point `HIPSHARP_REPOSITORY_ROOT` at an existing compatible checkout instead. The core has no HipSharp package dependency; the full solution builds the adapter and test projects.

## Local checks

Run checks appropriate to the change. The standard entry points are:

```powershell
.\eng\verify-repository-layout.ps1
.\eng\build.ps1 -Configuration Release
.\eng\test.ps1 -Configuration Release -NoBuild
.\eng\docs.ps1 -Configuration Release -NoBuild
```

Native declarations are generated from the frozen header and normalized model; do not hand-edit `src/**/Generated/*.cs`. Use `eng/generate-interop.ps1 -AcquireHeader -Verify` to check reproducibility. See [engineering commands](eng/README.md) for native ABI, public API and package checks. Reproducible model generators live in [tests/fixtures](tests/fixtures/README.md).

Keep substitute-library tests separate from official runtime qualification. Keep session-specific runtime probes, execution records and failures outside the checkout. Describe application-facing support boundaries in [platform support](docs/compatibility/platforms.md).

## Distribution and security

The project ships managed packages only. Install a coherent MIGraphX/ROCm native environment from AMD's official system repository. Do not commit native binaries, model binaries, credentials, host addresses or raw cloud output.

The default version is `0.0.0`. Package publication, release tags and Pages deployment require separate owner authorization. Intentional API changes require review of the compatibility snapshots. See [SECURITY.md](SECURITY.md).

For managed candidate packaging and portable offline evidence review, see [Release candidates](eng/RELEASING.md). These checks do not require a GPU or change native support declarations.

Contributions are provided under [Apache-2.0](LICENSE).
