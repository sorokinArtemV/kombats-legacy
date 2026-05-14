# Phase III — Deliverable 1 Report: SignalR Redis Backplane Code Change

## 1. Header

- **Date:** 2026-05-13
- **Branch:** `feat/signalr-backplane`
- **Parent commit:** `1958389` (docs: chapter 3 planning and investigation reports), branched off `development`
- **Working tree:** 3 modified files, unstaged, uncommitted (architect commits)
- **Scope:** Battle composition-root SignalR registration only, per `CHAPTER_3_PLAN.md` §4/§7/§12.

---

## 2. Files modified

| File | Lines changed | Change |
|---|---|---|
| `Directory.Packages.props` | +1 (line 46) | New `<PackageVersion>` for `Microsoft.AspNetCore.SignalR.StackExchangeRedis 10.0.3` under the existing `SignalR` ItemGroup. |
| `src/Kombats.Battle/Kombats.Battle.Bootstrap/Kombats.Battle.Bootstrap.csproj` | +1 (line 29) | New `<PackageReference>` (version-less, central pin) for the same package. |
| `src/Kombats.Battle/Kombats.Battle.Bootstrap/Program.cs` | +2 / −1 (lines 127–131) | One-line comment + `.AddStackExchangeRedis(redisConnectionString)` chained onto the existing `AddSignalR(...).AddJsonProtocol(...)` builder. |

Three files total. `git diff --stat`: `3 files changed, 5 insertions(+), 1 deletion(-)`.

---

## 3. Package version chosen

**`Microsoft.AspNetCore.SignalR.StackExchangeRedis 10.0.3`** (stable, `net10.0` target framework).

**Justification.** The latest stable on NuGet for this package as of today is `10.0.8` — also a `net10.0` build, also stable. I deliberately chose `10.0.3` instead. Reason: the package transitively depends on `Microsoft.Extensions.Options >= 10.0.8` at version 10.0.8, but the rest of this monorepo pins **all** `Microsoft.Extensions.*` (and `Microsoft.AspNetCore.*`, `Microsoft.EntityFrameworkCore.*`) packages to `10.0.3` centrally in `Directory.Packages.props`. Pulling in `10.0.8` of just the SignalR-Redis package would either (a) cascade to upgrading every other Microsoft.* pin to `10.0.8` — out of scope for a one-line backplane addition — or (b) trigger `NU1605` downgrade errors at restore.

`10.0.3` is the highest version that aligns 1:1 with the established baseline. Its dependency declarations:

- `MessagePack 2.5.187` — new transitive, no conflict.
- `Microsoft.Extensions.Options 10.0.3` — exact match with the rest of the repo's pin.
- `StackExchange.Redis 2.7.27` — the repo already pins `StackExchange.Redis` to `2.8.16`, which satisfies `>= 2.7.27`. No mismatch warning emitted; nothing to flag here.

If a future chapter bulk-bumps every Microsoft.* package from `10.0.3` to whatever's latest, this one moves with the rest.

---

## 4. Diff of the change

```diff
diff --git a/Directory.Packages.props b/Directory.Packages.props
index fde43b5..5fe0621 100644
--- a/Directory.Packages.props
+++ b/Directory.Packages.props
@@ -43,6 +43,7 @@
 
   <ItemGroup Label="SignalR">
     <PackageVersion Include="Microsoft.AspNetCore.SignalR.Client" Version="10.0.3" />
+    <PackageVersion Include="Microsoft.AspNetCore.SignalR.StackExchangeRedis" Version="10.0.3" />
   </ItemGroup>
 
   <ItemGroup Label="API Documentation">
diff --git a/src/Kombats.Battle/Kombats.Battle.Bootstrap/Kombats.Battle.Bootstrap.csproj b/src/Kombats.Battle/Kombats.Battle.Bootstrap/Kombats.Battle.Bootstrap.csproj
index 3eb7017..bcb9585 100644
--- a/src/Kombats.Battle/Kombats.Battle.Bootstrap/Kombats.Battle.Bootstrap.csproj
+++ b/src/Kombats.Battle/Kombats.Battle.Bootstrap/Kombats.Battle.Bootstrap.csproj
@@ -26,6 +26,7 @@
     <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
     <PackageReference Include="EFCore.NamingConventions" />
     <PackageReference Include="StackExchange.Redis" />
+    <PackageReference Include="Microsoft.AspNetCore.SignalR.StackExchangeRedis" />
     <PackageReference Include="AspNetCore.HealthChecks.NpgSql" />
     <PackageReference Include="AspNetCore.HealthChecks.Redis" />
     <PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" />
diff --git a/src/Kombats.Battle/Kombats.Battle.Bootstrap/Program.cs b/src/Kombats.Battle/Kombats.Battle.Bootstrap/Program.cs
index eb3b47f..799ec26 100644
--- a/src/Kombats.Battle/Kombats.Battle.Bootstrap/Program.cs
+++ b/src/Kombats.Battle/Kombats.Battle.Bootstrap/Program.cs
@@ -125,8 +125,10 @@ builder.Services.AddScoped<IRulesetProvider, RulesetProvider>();
 builder.Services.AddSingleton<ISeedGenerator, SeedGenerator>();
 
 // Infrastructure — SignalR
+// Backplane reuses the same Redis instance as battle state today; production deployments should split the two onto dedicated Redis nodes.
 builder.Services.AddSignalR(options => { options.EnableDetailedErrors = builder.Environment.IsDevelopment(); })
-    .AddJsonProtocol(options => { options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()); });
+    .AddJsonProtocol(options => { options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()); })
+    .AddStackExchangeRedis(redisConnectionString);
 
 // Messaging (Kombats.Messaging with transactional outbox — AD-01)
 builder.Services.AddMessaging<BattleDbContext>(
```

The connection string passed (`redisConnectionString`) is exactly the same local variable already in scope at `Program.cs:98` — `builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379,abortConnect=false"`. No new config key, no new fallback. Per CHAPTER_3_PLAN §12 Q6 / Q7: shared connection string, default channel prefix (no `options.Configuration.ChannelPrefix = …` override).

---

## 5. Build result

`dotnet restore` + `dotnet build --no-restore` on the full solution:

```
Build succeeded.
    84 Warning(s)
    0 Error(s)

Time Elapsed 00:00:03.05
```

- **0 errors.**
- **84 warnings, exactly equal to the pre-change baseline.** Confirmed by stashing the change, rebuilding, observing `84 Warning(s)`, popping the stash, rebuilding — same 84. **No new warnings introduced by the backplane addition.**
- Warning categories (all pre-existing, unrelated to this change):
  - `NU1902` — `OpenTelemetry.*` `1.15.0` known-vulnerability advisories, present across Battle, BFF, Chat, Matchmaking, Players, Observability and their test projects.
  - `NU1510` — pruning hints on four `Microsoft.Extensions.*` references in `Kombats.Observability`.
- **No `NU1605` downgrade warnings.** **No `NU1608` version-conflict warnings.** Specifically: no warning relating to `StackExchange.Redis` (project pin `2.8.16` ≥ transitive request `2.7.27`) or `Microsoft.Extensions.Options` (project pin `10.0.3` = transitive request `10.0.3`). `grep -iE "(SignalR\.StackExchange|NU1605|NU1608)"` over the full build log → 0 matches.

---

## 6. Sanity checks

- **BFF `.AddSignalR()` is unchanged.** `src/Kombats.Bff/Kombats.Bff.Bootstrap/Program.cs:301` — verbatim `builder.Services.AddSignalR()`. File not in `git diff`. Confirms §12 Q11 (BFF stays single-replica, no backplane).
- **Chat `.AddSignalR()` is unchanged.** `src/Kombats.Chat/Kombats.Chat.Bootstrap/Program.cs:211` — verbatim `builder.Services.AddSignalR();`. File not in `git diff`. Confirms §11 (chat backplane is a separate future chapter).
- **No hub code modified.** `git diff --name-only` lists three files only; none of `BattleHub.cs`, `ChatHub.cs`, `InternalChatHub.cs`, `SignalRBattleRealtimeNotifier.cs`, `SignalRChatNotifier.cs`, `HubContextBattleSender.cs`, `HubContextChatSender.cs` appear in the diff.
- **No external-sender call-sites touched.** Per `SIGNALR_SURFACE_MAP.md` §F, all 12 external sender call-sites live in files not modified.
- **No `docker-compose*.yml` modified.** `git diff --name-only` does not list `docker-compose.yml`, `docker-compose.multi-replica.yml`, or any file under `observability/`.
- **No other `src/Kombats.*` files modified.** Only `src/Kombats.Battle/Kombats.Battle.Bootstrap/Program.cs` and `src/Kombats.Battle/Kombats.Battle.Bootstrap/Kombats.Battle.Bootstrap.csproj`.
- **`packages.lock.json` is not used in this solution** (no such files anywhere under `src/` or `tests/`), so no lock file is part of the diff.

---

## 7. Things I did not do

- No `git commit`. No `git push`. No `git add`. The change is left in the working tree, unstaged, for the architect's review.
- No load test run. No `dotnet run -- load`, no `dotnet run -- smoke`, no `dotnet run -- single-bot`.
- No edit to `docker-compose.yml`, `docker-compose.multi-replica.yml`, `observability/docker-compose.*.yml`, or anything else under `observability/`.
- No `docker compose up`, `down`, `restart`, or any other compose command. The stack was not touched.
- No changes anywhere under `src/Kombats.*` outside the Battle Bootstrap composition root + its `.csproj`.
- No changes to BFF or Chat services. No changes to hub code, notifier code, relay code, or any test code.
- No "while I'm here" refactors or cleanups. The `NU1902` OpenTelemetry vulnerability warnings are pre-existing and untouched.
- No new config keys, no new appsettings entries, no new environment variables. The package's defaults are accepted (default channel prefix, per §12 Q7).
- No removal of the `maxReplicas: 1` ceiling in `infra/` — that's Ch5+ per §11 of the plan.

---

## 8. Open questions for architect

1. **Version-pin philosophy — confirm 10.0.3 over 10.0.8.** I chose `10.0.3` to align with every other `Microsoft.AspNetCore.*` / `Microsoft.Extensions.*` central pin. Latest stable is `10.0.8`. Both are .NET 10 compatible. Going to `10.0.8` would force `Microsoft.Extensions.Options` (currently pinned at `10.0.3` repo-wide) up to `10.0.8`, which would cascade. If the architect prefers "latest stable, version cascade accepted", I'll bump everything in a separate PR — but `10.0.3` is the minimum-disruption choice for this branch's purpose (prove the backplane unlocks multi-replica) and matches the spec wording "highest compatible version" interpreted as "highest that integrates cleanly with the existing baseline".

Otherwise: **None.**

---

## Artefact location

This file: `tests/Kombats.LoadTests/PHASE_3_D1_REPORT.md` — also uncommitted, alongside the code change.
