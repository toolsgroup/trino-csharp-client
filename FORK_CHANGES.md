# Fork changes

This is a ToolsGroup fork of [trinodb/trino-csharp-client](https://github.com/trinodb/trino-csharp-client), maintained because upstream has had no code pushed to `main` since 2025 and several real bugs sit unmerged as open PRs. This document tracks what's been fixed or changed here independently of upstream, so it's clear at a glance what to expect if/when reconciling against a future upstream sync.

Each entry links the internal PR; upstream issues/PRs are cross-referenced where a fix mirrors or relates to one.

## Behavior fixes

### 1. `TrinoCommand.ExecuteDbDataReaderAsync` matched `CommandBehavior` by exact equality instead of flags

`CommandBehavior` is a `[Flags]` enum, but the exact-match `switch` meant any combined value (e.g. Dapper's `SequentialAccess | SingleResult`) fell through to `throw new NotSupportedException()`, breaking Dapper support entirely. Fixed by checking each flag with bitwise `&` instead.

Cherry-picked from upstream PR [trinodb/trino-csharp-client#22](https://github.com/trinodb/trino-csharp-client/pull/22) (open, unmerged upstream) rather than re-derived, for correct authorship.

**PR:** [#1](https://github.com/toolsgroup/trino-csharp-client/pull/1)

### 2. `TrinoDataReader.NextResult()` unconditionally threw

Any well-behaved ADO.NET consumer (Dapper included) calls `NextResult()` defensively even for a single-result-set query, expecting `false` rather than an exception. Fixed to return `false`, matching the [`IDataReader.NextResult`](https://learn.microsoft.com/en-us/dotnet/api/system.data.idatareader.nextresult)
contract. Relates to upstream issue [trinodb/trino-csharp-client#18](https://github.com/trinodb/trino-csharp-client/issues/18)
(open, no PR).

**PR:** [#2](https://github.com/toolsgroup/trino-csharp-client/pull/2)

### 3. Test fixture files resolved against the wrong directory

`TrinoTestServer.Create()` resolved fixture filenames (e.g. `"trino_schema_columns.txt"`) against the process's current working directory, while the `.csproj`'s `<None Update="scripts\*.txt">` items deploy them under a `scripts/` subfolder next to the test assembly — 9 of 33 tests failed with `FileNotFoundException` whenever the suite was actually run (upstream's own CI never executed
tests at all, only built, so this had gone unnoticed). Fixed by resolving via `Path.Combine(AppContext.BaseDirectory, "scripts", testFile)` instead of relying on the working directory.

**PR:** [#3](https://github.com/toolsgroup/trino-csharp-client/pull/3)

### 4. `TrinoBigDecimal` had no conversion to `double`, and `ToDecimal()` mishandled negative signs

`TrinoBigDecimal` implements neither `IConvertible` nor a conversion operator, so `Convert.ToDouble()`/`(double)` casts on a numeric aggregation result (e.g. `SUM(qty * price)`) throw `InvalidCastException`; the only prior options were `ToDecimal()` (throws above ~28 significant digits) or manually parsing `.ToString()`. Added `ToDouble()`.

While implementing it, found and fixed a separate, silent bug in `ToDecimal()`: it combined the integer and fractional parts as `integerDecimal + fractionalDecimal`, which is wrong whenever the value is negative with a nonzero integer part (e.g. `"-123.456"` came back as `-122.544`, not `-123.456`, with no exception at all). Fixed to subtract instead of add when the integer part is negative; all existing overflow guards are unchanged.

**Does not fix** the separate, already-known upstream issue [trinodb/trino-csharp-client#29](https://github.com/trinodb/trino-csharp-client/issues/29) (open PR [#30](https://github.com/trinodb/trino-csharp-client/pull/30)) where a value like `-0.6` loses its sign entirely at *construction* time (`BigInteger.Parse("-0")` collapses to `0`) — confirmed still reproducing; that needs a structural change to where sign is stored, not addressed here.

**PR:** [#7](https://github.com/toolsgroup/trino-csharp-client/pull/7)

## Packaging & versioning

Repackaged `Trino.Client`/`Trino.Data.ADO` under `ToolsGroup.Trino.Client`/`ToolsGroup.Trino.Data.ADO` NuGet IDs (avoids collision with unaffiliated third-party republishes already on nuget.org — `Trino.Core`, `TrinoClient`, `DL.Trino.Client`, `NH.Trino.Client`, none affiliated with `trinodb`).

C# namespaces are unchanged. Versioning is independent of upstream: PR-label-driven (`major-release`/`minor-release`/`patch-release`/`skip-release`), published to GitHub Packages.

**PR:** [#4](https://github.com/toolsgroup/trino-csharp-client/pull/4)

## Infrastructure

CI cannot call `toolsgroup/tg-shared-actions`' reusable workflows/actions — GitHub does not allow a public repository to call actions or reusable workflows hosted in a private repository, and this repo can't be made private on our plan. CI is therefore fully self-contained (no `uses: toolsgroup/tg-shared-actions/...` anywhere), with the same PR-label versioning and label checks reimplemented locally.

**PRs:** [#5](https://github.com/toolsgroup/trino-csharp-client/pull/5),
[#6](https://github.com/toolsgroup/trino-csharp-client/pull/6)

## Syncing with upstream

If an upstream PR we've mirrored here eventually merges (e.g. #22), drop our equivalent commit on
the next sync and take upstream's version instead, to reduce this fork's maintenance surface.
