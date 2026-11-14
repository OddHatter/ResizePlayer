# ResizePlayer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A client-side Valheim mod that scales the player as a whole (with hitbox/collision) and per body part, from a live-reloading config, synced to friends who have the mod.

**Architecture:** One `PlayerScaler` MonoBehaviour per Player (added by a `Player.Awake` postfix). Each frame it works out the target sizes: from config for the local player (writing changes to the player's ZDO), or from the ZDO for remote players. Then it applies them: the root `localScale` for Overall, and humanoid bone `localScale`s for parts, always computed from cached originals. The local player also gets reach and swim depth scaled, and a `GameCamera.UpdateCamera` prefix scales camera max distance. Unity-free math lives in `BodyParts.cs`, which has unit tests.

**Tech Stack:** C# (net472, LangVersion latest), BepInEx 5.4.23, Harmony, Krafs.Publicizer, Unity Animator humanoid bones. Tests: xunit on net8.0.

**Spec:** `docs/superpowers/specs/2026-11-14-resizeplayer-design.md`

## Global Constraints

- GUID `com.oddhatter.resizeplayer`, name `ResizePlayer`, version `1.0.0`, namespace `Oddhatter.ResizePlayer`, `[BepInProcess("valheim.exe")]`.
- Build: `TreatWarningsAsErrors`, so 0 warnings. Same csproj layout as `../TameFollow/TameFollow.csproj` (Deploy and Package targets).
- dotnet isn't on PATH. Use `DOTNET_ROOT=$HOME/.dotnet $HOME/.dotnet/dotnet ...` (below: `$DN` = that prefix).
- Config: `General.Enabled` (true). `Size.Overall`, `Size.Head`, `Size.Neck`, `Size.Chest`, `Size.Belly`, `Size.Hips`,
  `Size.Arms`, `Size.Hands`, `Size.Legs`, `Size.Feet`: default 1.0, `AcceptableValueRange<float>(0.25f, 4f)`.
- Edits to the cfg apply live (FileSystemWatcher reload copied from TameFollow). Values are read every frame, never cached.
- Never change prefab data or saved character/world data. `Enabled = false` gives vanilla immediately.
- All bone scales are uniform, and every scale is computed from cached originals, never multiplied cumulatively.
- ZDO keys: `oddhatter.resizeplayer.<part lowercase>` (floats). Missing = 1. Every value read is clamped to 0.25–4.
- Reach (`m_maxInteractDistance`, `m_maxPlaceDistance`) and camera max distance (`m_maxDistance`, `m_maxDistanceBoat`)
  × `max(1, Overall)`. Swim depth (`m_swimDepth`) × `Overall`. Local player only.
- Stats otherwise vanilla. Body parts are visual only. No UI or hotkeys. No other creatures. Character-select preview not scaled.
- Death ragdoll: not scaled in v1 (builder's call from the spec; YAGNI).
- Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. Out-of-range or garbage sizes (0, -1, 100, NaN, from a hand-edited cfg or a friend's ZDO) → clamped to 0.25–4, NaN → 1, no crash. Test: Task 2 `Clamp_*`.
2. Toggling `Enabled` or changing a value many times → sizes are the same as setting the final value once (no drift). Test: Task 3 Step 6 in-game check.
3. Respawn after death, relog, or teleport → a new Player object is still scaled, and the camera/reach use the new body. Test: Task 4 Step 4 in-game check.
4. A friend joins after you set your sizes (values already in your ZDO before their client creates your Player) → they see you scaled immediately, not after your next edit. Test: Task 5 Step 3 (first-read sentinel) plus the in-game check.
5. Extreme part combinations (Hips 0.25 with Legs 4, Arms 4 with Hands 0.25) → each part shows its own size. Test: Task 2 `LocalFactor_extremes`.

---

### Task 1: Project scaffold, config, live reload

**Files:**
- Create: `ResizePlayer.csproj`, `src/ResizePlayerPlugin.cs`, `manifest.json`, `LICENSE`, `.gitignore`, `icon.png`

**Interfaces:**
- Produces: `ResizePlayerPlugin.Log` (`ManualLogSource`), `ResizePlayerPlugin.Enabled` (`ConfigEntry<bool>`).
  The size entries are bound in Task 2, once `Part` exists.

- [ ] **Step 1: csproj.** Copy `../TameFollow/TameFollow.csproj`. Change: AssemblyName `ResizePlayer`, RootNamespace
  `Oddhatter.ResizePlayer`, deploy dir `BepInEx/plugins/Oddhatter-ResizePlayer`, zip `dist/Oddhatter-ResizePlayer-$(Version).zip`.
  Add `<Reference Include="UnityEngine.AnimationModule">` (same HintPath style) and `<Compile Remove="tests/**" />`.
  Add other Unity module references only if the compiler asks for them.
- [ ] **Step 2: Plugin.** `src/ResizePlayerPlugin.cs`: copy `../TameFollow/src/TameFollowPlugin.cs` (Awake, Update,
  OnDestroy, WatchConfigFile, OnConfigFileEvent, ReloadConfigFromDisk verbatim), renamed. Bind `General.Enabled` only.
  Keep the `Patched X.Y` log loop.
- [ ] **Step 3: Metadata.** `manifest.json` (name ResizePlayer, 1.0.0, website `https://github.com/OddHatter/ResizePlayer`,
  description "Resize your Viking: giant, tiny, big head, wide hips and more. Live config, synced to friends who have it.",
  dependency `denikson-BepInExPack_Valheim-5.4.2360`). Copy LICENSE, .gitignore and icon.png from TameFollow.
- [ ] **Step 4: Build.** Run `$DN build -c Release -p:Deploy=true`. Expected: `0 Warning(s) 0 Error(s)` and `Deployed to ...`.
- [ ] **Step 5: Tracker.** Create `tasks/todo.md` (gitignored, as in the sibling mods). Include: a header line (GUID, BepInEx,
  client-side plus synced), the goal in the user's words, a link to the spec and to this plan, a checklist with one item
  per task (tick each one as it finishes), and a Findings section for the in-game results. Keep it updated after every task.
- [ ] **Step 6: Commit.** `git add -A && git commit -m "Scaffold ResizePlayer: config and live reload"`

### Task 2: BodyParts math (Unity-free) with tests

**Files:**
- Create: `src/BodyParts.cs`, `tests/ResizePlayer.Tests/ResizePlayer.Tests.csproj`, `tests/ResizePlayer.Tests/BodyPartsTests.cs`
- Modify: `src/ResizePlayerPlugin.cs` (build `Sizes` from `BodyParts.All`)

**Interfaces:**
- Produces (namespace `Oddhatter.ResizePlayer`, all `internal`, no UnityEngine references):
  - `enum Part { Overall, Head, Neck, Chest, Belly, Hips, Arms, Hands, Legs, Feet }`
  - `static class BodyParts`: `const float Min = 0.25f, Max = 4f`; `static readonly Part[] All` (enum order);
    `static float Clamp(float v)`; `static string ZdoKey(Part p)`; `static float LocalFactor(float own, float parent)`;
    `static float ReachFactor(float overall)`
  - `sealed class PartValues`: `float this[Part p] { get; set; }`; `static PartValues Identity()` (all 1);
    `bool ApproximatelyEquals(PartValues other)` (each |Δ| ≤ 1e-4, null → false); `void CopyFrom(PartValues other)`

- [ ] **Step 1: Test project.** net8.0, packages `xunit` 2.9.2, `xunit.runner.visualstudio` 2.8.2,
  `Microsoft.NET.Test.Sdk` 17.11.1, plus `<Compile Include="../../src/BodyParts.cs" />`.
- [ ] **Step 2: Write failing tests** in `BodyPartsTests.cs`:

```csharp
[Fact] public void Clamp_inRange_unchanged() => Assert.Equal(2.5f, BodyParts.Clamp(2.5f));
[Fact] public void Clamp_low() { Assert.Equal(0.25f, BodyParts.Clamp(0f)); Assert.Equal(0.25f, BodyParts.Clamp(-1f)); }
[Fact] public void Clamp_high() => Assert.Equal(4f, BodyParts.Clamp(100f));
[Fact] public void Clamp_nonFinite_isOne() { Assert.Equal(1f, BodyParts.Clamp(float.NaN)); Assert.Equal(1f, BodyParts.Clamp(float.PositiveInfinity)); }
[Fact] public void ZdoKey_format() { Assert.Equal("oddhatter.resizeplayer.head", BodyParts.ZdoKey(Part.Head)); Assert.Equal("oddhatter.resizeplayer.overall", BodyParts.ZdoKey(Part.Overall)); }
[Fact] public void LocalFactor_ratio() { Assert.Equal(2f, BodyParts.LocalFactor(2f, 1f)); Assert.Equal(0.5f, BodyParts.LocalFactor(1f, 2f)); }
[Fact] public void LocalFactor_extremes() { Assert.Equal(16f, BodyParts.LocalFactor(4f, 0.25f)); Assert.Equal(1f / 16f, BodyParts.LocalFactor(0.25f, 4f), 5); }
[Fact] public void ReachFactor_neverBelowOne() { Assert.Equal(1f, BodyParts.ReachFactor(0.25f)); Assert.Equal(3f, BodyParts.ReachFactor(3f)); }
[Fact] public void All_hasEveryPartOnce() => Assert.Equal(Enum.GetValues(typeof(Part)).Length, BodyParts.All.Distinct().Count());
[Fact] public void PartValues_identity_isAllOne() { var v = PartValues.Identity(); Assert.All(BodyParts.All, p => Assert.Equal(1f, v[p])); }
[Fact] public void PartValues_approxEquals() { var a = PartValues.Identity(); var b = PartValues.Identity(); b[Part.Hips] = 1.00005f; Assert.True(a.ApproximatelyEquals(b)); b[Part.Hips] = 1.1f; Assert.False(a.ApproximatelyEquals(b)); Assert.False(a.ApproximatelyEquals(null)); }
[Fact] public void PartValues_copyFrom_isIndependent() { var a = PartValues.Identity(); var b = PartValues.Identity(); b[Part.Head] = 3f; a.CopyFrom(b); b[Part.Head] = 1f; Assert.Equal(3f, a[Part.Head]); }
```

- [ ] **Step 3: Run tests.** `$DN test tests/ResizePlayer.Tests`. Expected: build failure (BodyParts/Part not defined).
- [ ] **Step 4: Implement `src/BodyParts.cs`** to the Interfaces block. `Clamp`: non-finite → 1, else clamp to [Min, Max].
  `ZdoKey`: `"oddhatter.resizeplayer." + p.ToString().ToLowerInvariant()`. `LocalFactor`: `own / parent` (inputs already clamped).
  `ReachFactor`: `Math.Max(1f, overall)`. `PartValues` backed by `float[All.Length]`.
- [ ] **Step 5: Run tests.** `$DN test tests/ResizePlayer.Tests`. Expected: all pass. Then
  `$DN build -c Release` on the mod. Expected: 0 warnings (BodyParts.cs must compile on net472).
- [ ] **Step 6: Wire config.** In the plugin, add `internal static Dictionary<Part, ConfigEntry<float>> Sizes`, bound by
  looping over `BodyParts.All` (section `Size`, key `p.ToString()`, default 1, `AcceptableValueRange<float>(Min, Max)`).
  Descriptions in plain English, e.g. Overall: "Whole body, including hitbox, collision, reach and swim depth.",
  Hips: "Pelvis and butt.". Rebuild: 0 warnings. Start the game once: the cfg lists all 11 settings.
- [ ] **Step 7: Commit.** `git commit -m "BodyParts: clamping, ZDO keys, part ratios, with tests"`

### Task 3: PlayerScaler: apply sizes to the local player

**Files:**
- Create: `src/PlayerScaler.cs`, `src/Patches.cs`

**Interfaces:**
- Consumes: `ResizePlayerPlugin.Enabled`, `ResizePlayerPlugin.Sizes`, `ResizePlayerPlugin.Log`, everything in Task 2.
- Produces: `internal sealed class PlayerScaler : MonoBehaviour`, with `internal static float LocalOverall` (effective
  Overall of the local player, 1 when disabled or there's no local player; read by Task 4). Private helpers named in the
  steps below are used again by Tasks 4 and 5.

- [ ] **Step 1: Patch.** `Patches.cs`: `[HarmonyPatch(typeof(Player), nameof(Player.Awake))]` postfix →
  `__instance.gameObject.AddComponent<PlayerScaler>()`.
- [ ] **Step 2: Bone map** (static in PlayerScaler), `(HumanBodyBones, Part)`:
  Hips→Hips, Spine→Belly, Chest→Chest, UpperChest→Chest, Neck→Neck, Head→Head,
  Left/RightUpperArm→Arms, Left/RightLowerArm→Arms, Left/RightHand→Hands,
  Left/RightUpperLeg→Legs, Left/RightLowerLeg→Legs, Left/RightFoot→Feet, Left/RightToes→Feet.
- [ ] **Step 3: `private bool EnsureBones()`.** Cache when `_player.m_animator` differs from the cached animator, or any
  cached Transform is null. For each map entry use `animator.GetBoneTransform(bone)`. A null bone is skipped and logged
  once per bone name (static HashSet). Store `{Transform t, Part part, Part? parentPart, Vector3 original}`, where
  `parentPart` is the part of the nearest ancestor Transform that is also in the cache (walk `t.parent` up to the
  animator's transform). Keep originals in a `Dictionary<Transform, Vector3>` that survives recaching, so a bone the mod
  already scaled is never captured as an original. Root original =
  `ZNetScene.instance.GetPrefab("Player").transform.localScale` (fall back to `Vector3.one`).
- [ ] **Step 4: `private PartValues LocalTarget()`** (clamped config values) and **`private void Apply(PartValues target)`**.
  `target == null` means vanilla. With a target: every frame, set each bone's `t.localScale = original * LocalFactor(target[part],
  parentPart == null ? 1 : target[parentPart])`. Set the root to `rootOriginal * target[Overall]` only when it differs.
  With null, when the previous apply was non-null: restore every bone original and the root, once.
  `LateUpdate`: return if `!_nview.IsValid()`. If `_player == Player.m_localPlayer`, then target = `Enabled ? LocalTarget() : null`,
  `Apply(target)`, and set `LocalOverall`. (Remote players come in Task 5.) In `OnDestroy`, reset `LocalOverall = 1` if this was the local player.
  On the first local apply, log once: `ZSyncTransform.m_syncScale` and `ZNetView.m_syncInitialScale` of this player (the spec's open question).
- [ ] **Step 5: Keep feet on the ground when Legs ≠ 1.** Leg bones are scaled after animation and foot IK
  (`CharacterAnimEvent.UpdateFootIK` runs in OnAnimatorIK, before LateUpdate), so longer legs sink the feet into the
  ground and shorter legs leave them floating. Fix it by raising or lowering the skeleton:
  - At the first `EnsureBones` (nothing scaled yet), measure `_restLegLength = (|LeftUpperLeg→LeftLowerLeg| +
    |LeftLowerLeg→LeftFoot|) / root.lossyScale.y` (world distances). Also cache `_armature = hips.parent` and its original
    `localPosition`.
  - In `Apply` with a target: `worldOffset = root.up * (target[Legs] - 1) * _restLegLength * root.lossyScale.y`, then
    `_armature.localPosition = armatureOriginal + _armature.parent.InverseTransformVector(worldOffset)`. This is an absolute
    write, so there's no drift.
  - Restore the original when the target is null. If `hips.parent` is the animator's own transform (no separate armature
    node), skip the offset and log once.
- [ ] **Step 6: Build, deploy, in-game check** (ask the user to run it; report what they see).
  `$DN build -c Release -p:Deploy=true` → 0 warnings. In game, editing the cfg while playing (each applies within ~1 s):
  Head 3 → big head with helmet and hair. Hips 2 → wide pelvis/butt, normal legs. Arms 2 + Hands 1 → normal hands.
  Legs 2 and Legs 0.5 → feet on the ground while standing and running. Feet 2 → big feet, not sunk.
  Overall 3 → giant that can't pass a 1-wide door. Overall 0.5 → tiny. **No drift:** Overall 1 → 3 → 1 → 3 several times,
  and Enabled false → true, ends identical to a single change. Enabled false → vanilla at once.
  Log shows the syncScale/syncInitialScale line.
- [ ] **Step 7: Commit.** `git commit -m "PlayerScaler: whole-body and per-part scaling of the local player"`

### Task 4: Reach, swim depth, camera

**Files:**
- Modify: `src/PlayerScaler.cs`, `src/Patches.cs`

**Interfaces:**
- Consumes: `PlayerScaler.LocalOverall`, `BodyParts.ReachFactor`.

- [ ] **Step 1: Reach and swim** (local player, in LateUpdate after Apply). Cache this player's originals of
  `m_maxInteractDistance`, `m_maxPlaceDistance` and `m_swimDepth` once, at the component's first LateUpdate. Set them each frame
  to `original * ReachFactor(overall)` (the first two) and `original * overall` (swim), with `overall = LocalOverall`
  (1 when disabled → originals restored).
- [ ] **Step 2: Camera.** `[HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateCamera))]` prefix. Keep static
  `s_camera` plus the original `m_maxDistance` and `m_maxDistanceBoat`, recaptured when `__instance != s_camera`. Set both to
  `original * BodyParts.ReachFactor(PlayerScaler.LocalOverall)`.
- [ ] **Step 3: Build.** `$DN build -c Release -p:Deploy=true` → 0 warnings, and the log lists `Patched GameCamera.UpdateCamera`.
- [ ] **Step 4: In-game check** (ask the user). Overall 3: open a chest, use a workbench, sleep in a bed, pick up a dropped
  item, place a floor piece on the ground. Scroll out: the camera isn't inside the body. Wade knee-deep without swimming.
  Overall 0.25: swims in shallow water, reach still 5 m. Die at Overall 3 and respawn: still a giant, can still use a chest.
  Enabled false → reach, swim and camera vanilla.
- [ ] **Step 5: Commit.** `git commit -m "Scale reach, swim depth and camera distance with overall size"`

### Task 5: Multiplayer sync over ZDO

**Files:**
- Modify: `src/PlayerScaler.cs`

**Interfaces:**
- Consumes: `BodyParts.ZdoKey`, `BodyParts.Clamp`, `PartValues`, `Apply`, `LocalTarget` from Task 3.

- [ ] **Step 1: Local writes.** For the local player (also the ZDO owner): each frame compute the publish values
  (`Enabled ? LocalTarget() : PartValues.Identity()`). If they're not `ApproximatelyEquals(_lastPublished)`, then
  `zdo.Set(BodyParts.ZdoKey(p), value)` for each changed part, and copy into `_lastPublished`
  (starts null, so the first frame always publishes).
- [ ] **Step 2: Remote reads.** For other players: target = `Enabled ? _remote : null`. Refresh `_remote` when
  `zdo.DataRevision != _lastRevision` or `!_haveRemote`: each part = `Clamp(zdo.GetFloat(ZdoKey(p), 1f))`. Then `Apply(target)`.
- [ ] **Step 3: First read.** `_haveRemote` starts false, so a player whose ZDO already holds values (a friend joining
  after you set sizes) is scaled on its first frame.
- [ ] **Step 4: Build, deploy, in-game check with a friend** who has the same build (ask the user). Each sees the other's
  sizes. A live edit shows on the other client within ~1 s. Friend joins after you set sizes → scaled immediately.
  You set Enabled false → the friend sees you normal. A vanilla friend → no errors on either side.
- [ ] **Step 5: Commit.** `git commit -m "Sync sizes to other players through the player ZDO"`

### Task 6: README, package, review notes

**Files:**
- Create: `README.md`. Modify: `tasks/todo.md` (gitignored, review section as in the other mods).

- [ ] **Step 1: README** in the style of `../TameFollow/README.md`:
  - What it does, as bullets.
  - A config table with all 11 settings (defaults and ranges).
  - What scales: hitbox, reach, swim and camera.
  - What doesn't: speed, damage, carry weight, melee reach.
  - Body parts are visual only.
  - "Big butt" is a wider pelvis, because there's no butt bone.
  - Friends need the mod to see your sizes, and players without it see you at normal size. Correct this line if Task 3's syncScale log shows vanilla players get Overall too.
  - Same sizes on every character. Build, install and license sections.
- [ ] **Step 2: Package.** `$DN build -c Release -p:Deploy=true -p:Package=true`. Expected: `dist/Oddhatter-ResizePlayer-1.0.0.zip`
  containing manifest.json, README.md, icon.png and plugins/ResizePlayer.dll.
- [ ] **Step 3: Review notes** in `tasks/todo.md`: files, the syncScale finding, the in-game results the user reported, and known limits (ragdoll not scaled).
- [ ] **Step 4: Commit.** `git commit -m "README and packaging"`
