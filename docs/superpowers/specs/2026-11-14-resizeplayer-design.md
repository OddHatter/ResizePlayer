# ResizePlayer — Design

Valheim mod (BepInEx 5 / Harmony), Oddhatter family. GUID `com.oddhatter.resizeplayer`. Date 2026-11-14.

## Intent

Let the player change the size of their character: the whole body (a giant or a tiny Viking) and individual
body parts (big head, wide hips/butt, huge hands, ...). It's for the user and their friends on a shared server, for fun.
Friends who also install the mod see each other's sizes.

## Goals

- **Overall size** scales the whole player, and the gameplay body goes with it: collision capsule, hitbox (what enemies
  and projectiles hit), eye/camera point. A giant really is big: it can't fit through doors or portals, a tiny player can.
- **Body-part sizes**, one setting each: Head, Neck, Chest, Belly, Hips (pelvis/butt), Arms, Hands, Legs, Feet.
  Left and right share one value.
- **Set in the config file** (`BepInEx/config/com.oddhatter.resizeplayer.cfg`, also editable in r2modman's config editor).
  **Edits apply live** while the game runs, with no restart and no relog, using the same FileSystemWatcher reload as
  TameFollow/FreeFlight (`TameFollowPlugin.WatchConfigFile` / `ReloadConfigFromDisk`, copy verbatim).
- **Multiplayer:** every player with the mod sees every other player-with-the-mod at their chosen sizes, updated live
  when that player edits their config.

## Non-goals

- Stats stay vanilla: run/jump speed, stamina, damage, carry weight, health, melee reach (`Attack` range is not scaled).
  The exceptions are interact/build reach and swim depth (see "Reach and water"), which scale so a giant stays playable.
- Body parts are visual only. The player has one capsule collider, and only Overall changes it.
- No in-game UI, hotkeys or chat commands. The config file is the only control.
- Sizes are per installation (the config file), not per character: every character you play uses the same sizes.
- Players without the mod are not catered for: they see mod users at normal size (see "Open: vanilla visibility").
- The character-select screen preview is not scaled.
- No other creatures, only players.

## Anti-goals (a failure even if it "works")

- Changing prefab data or anything that persists in the world/character save: turning the mod off (`Enabled = false`
  or uninstalling) must give a fully vanilla player immediately.
- Breaking the game for unmodded players on the same server (errors, desync, kicks).
- Skewed/sheared meshes: all bone scales are **uniform** (x = y = z). Non-uniform scale under rotated bones shears.
- Per-frame cost that matters: lookups are cached, and ZDO reads/writes happen only when values change.

## Constraints and findings (decompiled assembly_valheim, current build)

- The game already scales characters by their root transform: `Character.Awake` path does
  `transform.localScale *= Game.m_enemySpeedSize` for non-players (Character.cs ~728). Collider, hitbox, eye and
  `GetRadius()` (`m_collider.radius * transform.localScale.magnitude`) all follow. **Overall = player root
  `transform.localScale`** uses the same mechanism.
- The player uses a Mecanim humanoid rig. Bones are found with `Utils.GetBoneTransform(m_animator, HumanBodyBones.X)`,
  the same call `Character` uses for `m_head`. The humanoid Animator writes bone rotations (and hips position), not
  localScale.
- Network: `ZDO` key/value per object. The local player owns its own ZDO, so it can write custom float keys; others read.
  Vanilla also has `ZSyncTransform.m_syncScale` / `ZNetView.m_syncInitialScale`, but whether the Player prefab has them
  on is prefab data, not code (unknown).
- GameCamera: third-person distance is `m_minDistance..m_maxDistance` (6) / `m_maxDistanceBoat`, and doesn't scale.
- Build/deploy/package: copy TameFollow's csproj layout (net472, Krafs.Publicizer on assembly_valheim, Deploy and
  Package targets writing `dist/Oddhatter-ResizePlayer-<ver>.zip`), plus manifest.json, README.md, icon.png, LICENSE (MIT),
  and `tasks/todo.md`.

## Approaches considered (for how sizes are set)

1. **Config file, live reload** (picked by the user). Least code, matches the user's other mods. Trade-off: same sizes on
   every character, and you leave the game window to edit.
2. In-game slider panel with per-character sizes. Best feel, most work (IMGUI window, saving to `Player.m_customData`).
   Not picked.
3. Chat commands (`/size head 2`). Not picked: fiddly to tune.

The user chose 1, with the requirement that edits apply live.

## Design

### Config (section `Size` unless noted)

| Key | Default | Range | Meaning |
|---|---|---|---|
| General.Enabled | true | | false = everyone you see (you included) is vanilla size. |
| Overall | 1.0 | 0.25–10.0 | Whole player: model, collision, hitbox, camera height. |
| Head, Neck, Chest, Belly, Hips, Arms, Hands, Legs, Feet | 1.0 | 0.25–10.0 | That part only. |

Use `AcceptableValueRange<float>` so r2modman shows sliders. Values are relative to vanilla (1 = normal).

### Bone mapping (HumanBodyBones)

- Head → Head. Neck → Neck. Chest → Chest (and UpperChest if the avatar has one). Belly → Spine. Hips → Hips.
- Arms → LeftUpperArm, RightUpperArm, LeftLowerArm, RightLowerArm.
- Hands → LeftHand, RightHand. Legs → Left/RightUpperLeg, Left/RightLowerLeg. Feet → Left/RightFoot (and Toes if present).
- A bone the avatar doesn't map (null) is skipped and logged once.

### Parts are independent

Scaling a bone scales everything under it. The mod counters this so each part's value is that part's own size:
each mapped bone's `localScale = originalLocalScale * (ownValue / parentPartValue)`, where `parentPartValue` is the value
of the nearest mapped ancestor bone (1 if none). Example: Arms 2, Hands 1 → normal hands on big arms. Hips 2 with
everything else 1 → a wide pelvis/butt region while legs and torso stay normal size (they attach slightly farther out,
because child positions scale with the parent). This is inherent to bone scaling, and the README says so.
Head also scales its face bones as a group (they're descendants, not mapped parts).

Arms and Legs map to upper and lower bones; the lower bone uses the same value, so its ratio to its parent is 1 and the
whole limb is the chosen size.

### Applying

- One component per Player (added in a `Player.Awake` postfix, or found lazily), for local and remote players alike.
- It caches the mapped bones and their original localScale (captured once, before the mod ever changes them) and the
  root's original localScale.
- `LateUpdate` (after the Animator): if the effective values changed since the last apply, write the bone localScales and
  the root localScale (`originalRoot * Overall`). Also re-apply when something else overwrote them (cheap check of the
  current localScale against the last value written), since equipment/ragdoll/animation code may reset them.
- `Enabled = false` (locally): restore originals on every player and stop applying.

### Sync

- The local player (ZDO owner) writes its values to its ZDO under keys `oddhatter.resizeplayer.<part>` (floats), when the
  config loads/reloads and when the player spawns. It writes only changed values. When `Enabled` is false it writes 1s
  (or removes the keys), so friends see you at normal size.
- Remote players' components read those keys from that player's ZDO, checking `ZDO.DataRevision` so they only re-read
  when it changes, and apply them the same way. Missing keys = 1.
- Values read from the network are clamped to the same 0.25–10 range.

### Camera

At Overall > 1 the camera's max distance (`m_maxDistance`, `m_maxDistanceBoat`) is multiplied by Overall so a giant isn't
filmed from inside their own body. Patch it in the getter/usage or set it each frame from the original. At Overall < 1
leave it vanilla. (Builder's call on the exact patch point.)

### Reach and water (user decision, 2026-11-14)

- **Interact and build reach scale up with Overall.** `Player.m_maxInteractDistance` and `m_maxPlaceDistance` (5 m, measured
  from `m_eye.position`, Player.cs ~171/173) are multiplied by Overall when Overall > 1, so a giant can still open
  chests, use stations and beds, pick up items and build on the ground. At Overall ≤ 1 they stay vanilla (5 m).
  Apply from the original values (not cumulatively) and restore them when Enabled is false. Only the local player needs it.
- **Swim depth scales with Overall in both directions.** `Character.m_swimDepth` (2 m, Character.cs ~167) is multiplied by
  Overall: a 4× giant wades through 8 m water, and a 0.25× player swims at 0.5 m. It applies to each player on whichever
  client simulates it (the owner, i.e. the local player for its own body), from the original value, restored when disabled.

## Left to the builder

- Exact Harmony patch points (Player.Awake vs. lazy component creation, camera patch).
- Whether to also scale the death ragdoll by Overall (nice to have: a giant shouldn't shrink when it dies).
- Logging: patched methods, loaded values, missing bones once. Same style as TameFollow.
- README wording, icon, manifest description.

## Open: vanilla visibility (check in-game, don't design around it)

If the Player prefab's `ZSyncTransform.m_syncScale` is on, unmodded players may see Overall (not body parts) for free.
The builder logs that flag once on spawn and notes the result in tasks/todo.md. Nothing depends on it.

## Verification

- Build: 0 warnings, 0 errors (TreatWarningsAsErrors), deploy to the r2modman profile, zip in dist/.
- In-game (by the user): change each setting in the config while playing and see it apply within a second; Overall 3 →
  can't fit a 1-wide doorway, camera not inside the body; Overall 0.5 → collider small; Hips 2 → wide butt with
  normal legs; Enabled false → vanilla immediately; with a friend who has the mod, each sees the other's sizes live.
- Overall 3: open a chest, use a workbench, sleep in a bed, place a floor piece on the ground. Wade into knee-deep
  water without swimming. Overall 0.25: start swimming in shallow water.

## Additions requested during implementation (2026-11-14)

- **Max size 10** (was 4) for every part; min stays 0.25. Network values clamp to 0.25–10.
- **Equipment follows size.** Weapons, shields, helmets, hair, beards and back items scale with the body part they're
  attached to (Overall × that part), no matter whether they were equipped before or after resizing.
- **Speed follows stride.** "Longer legs mean longer strides": walk, jog, run, crouch and swim speed (and acceleration)
  × Overall × Legs, clamped to 0.25–10. The locomotion animation is fed the vanilla speed so steps lengthen rather than
  quicken. Jump height is unchanged.
- **Camera keeps the character in view.** Zoom distance, min/max zoom and scroll speed × Overall (both ways, replacing
  the earlier max(1, Overall) rule for the camera only); near clip × min(1, Overall). The eye point (camera pivot and
  reach origin) follows head height when Legs or torso parts make the body taller or shorter.
- **Jumps, and shrinking both ways** ("change the jumps too. Make sure shrinking impacts are accounted for too"). Jump
  force × sqrt(Overall × Legs), so jump height × stride (distance grows faster, as ground speed also scales). Fall damage
  counts the drop divided by stride (a giant takes no damage from what it can jump onto; a tiny player is hurt by a
  table-height drop). Reach × max(1, Overall), plus the eye's lift (reach briefly
  shrank with tiny players too; reverted at the user's request, since 1.25 m reach made building impractical). Swimming starts at a water depth that scales with
  Overall (the game's fixed 0.4 m margin is scaled too). The game's 2 m "camera too close, hide the player" distance
  × min(1, Overall), so a tiny player stays visible.
- **Review fixes.** Feet stay on the ground in any pose (the lowest sole is kept where vanilla's would be, using the sole
  from the Player prefab's rest pose; skipped while swimming); Hips and Feet are compensated too. The camera pivot
  follows that lift through a 0.5 s low-pass so long legs don't shake the camera. Rigid armour pieces scale like
  weapons. Hover/build rays from the camera are 50 m × max(1, Overall). Player GetRadius/GetHeight follow Overall, so
  monsters can reach a giant.
- **Giant ground, giant water, tiny jump** ("when player is big, the falling animation is often activated and when in
  water, movement is frozen"; "The jump when small is also too diminished"). The game's ground-contact test
  (Character.OnCollisionStay) compares contact height with the unscaled collider radius and centre; it now uses the
  local player's scaled capsule. Wading drag (Character.ApplyLiquidResistance) is speed - speed^2 x 0.05 x immersion,
  which is zero at 20 m/s; for the scaled local player it is computed at vanilla speed and against the scaled body
  height, then scaled back. Jump force x sqrt(max(1, stride)): a tiny player keeps the vanilla jump (as with reach), and
  fall damage divides the drop by jump height (max(1, stride)), so a tiny player takes vanilla fall damage.
- **Gravity and the feather cape** ("The falling speed when giant is way too slow and the falling speed of tiny is too
  fast. When running on a hill with the feather cape, the character starts kind of flying"). The local player gets extra
  gravity (stride - 1) x g while the game applies gravity to a free body (not swimming, attached, debug flying,
  teleporting, dead or asleep). Jump speed becomes sqrt(stride x max(1, stride)) so heights stay as above under that
  gravity. The feather cape's fall-speed cap (applied every frame, even on the ground) is multiplied by stride for the
  local player through a ModifyWalkVelocity prefix; the field itself is untouched. Terrain is not scaled, so a giant
  sprinting over a sharp crest can still leave the ground briefly.
- **Tiny crouch** ("when tiny, the character looks like they are crouched"). The game's foot IK
  (CharacterAnimEvent.UpdateFootIK) plants each ankle a fixed 0.1 m above the ground and casts from 1 m above the foot;
  on a quarter-size body that lifts the feet and bends the knees. Those world-metre distances (component and per-foot
  m_footOffset, m_footStepHeight, m_stabalizeDistance) are multiplied by the foot bone's scale for every scaled player;
  m_footDownMax is compared in the body's scaled local space and is left alone.
- **Feather cape in the air** ("now when the feather cape is equipped when giant, the fall speed is too fast"). The cap
  stays x stride while IsOnGround() (slopes), and is x min(stride, sqrt(stride)) in the air: a stride-10 giant floats
  down at about 3x the vanilla cape speed instead of 10x, and a tiny player is unchanged.
- **Tiny gravity** ("tiny player fall too slow", after "the falling speed of tiny is too fast" with vanilla gravity).
  Gravity factor is stride above 1 and sqrt(stride) below (0.5 g at 0.25); jump speed is sqrt(gravity factor x jump
  height), so jump heights are unchanged.
- **Weapon effects** ("weapons that have animated effects like frostfire or blood lightning, the emination animation does
  not actually shrink properly"). Most item particle systems use Local scaling mode, which ignores parent scale. For
  every attached item (AttachItem, including skinned ones, and AttachArmor) each Local-mode system's own scale is set to
  original x ScaleFactorAt(joint) every frame, and transforms below it divide that back out (scale and position). No
  particle module values are touched (ParticleIntensityScaler rewrites those). Unverified: whether world-space motion
  (gravity, velocity, noise) and skinned-mesh emission points follow the system's own scale (a Unity bug fixed in 2017.1
  suggests world velocity and force do); check FrostFire flame rise and Berzerkr particles on the blade in game.
