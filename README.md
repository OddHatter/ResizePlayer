# ResizePlayer

Change the size of your character: become a giant or a tiny, or just give yourself a big head, a big butt, long legs
or huge hands.

- **Overall** scales your whole character, including the hitbox and collision, so a giant really is big and a tiny
  player really fits through small gaps.
- Each **body part** (head, neck, chest, belly, hips, arms, hands, legs, feet) has its own size. Parts are independent:
  a big head on a tiny body works.
- When you change **Legs**, your body is raised or lowered so your feet stay on the ground.
- Weapons, shields, helmets, hair and anything else you carry grow and shrink with the body part holding them (so with
  Overall, and with Hands or Head).
- You move faster or slower with your stride: speed follows your leg length (Overall x Legs), at the normal step rate.
- The camera follows your size, so you always see your character the same way.
- Edits to the config file apply right away, while the game is running.
- Multiplayer: friends who also have the mod see your sizes, and you see theirs.

## Config (`BepInEx/config/com.oddhatter.resizeplayer.cfg`)

Sizes are multipliers: 1 = normal, 2 = twice as big, 0.5 = half. Each size allows 0.25 to 10.

| Setting | Default | Meaning |
|---|---|---|
| General.Enabled | true | false = everyone looks vanilla to you, and other players see you at normal size. |
| Size.Overall | 1 | Whole body, including hitbox, collision, equipment, speed, reach, swim depth and camera. |
| Size.Head | 1 | Head (with hair, beard and helmet). |
| Size.Neck | 1 | Neck. |
| Size.Chest | 1 | Chest and shoulders area. |
| Size.Belly | 1 | Belly and lower back. |
| Size.Hips | 1 | Pelvis and butt. |
| Size.Arms | 1 | Upper and lower arms. |
| Size.Hands | 1 | Hands. |
| Size.Legs | 1 | Upper and lower legs. Your body is raised or lowered to keep your feet on the ground, and longer legs move you faster. |
| Size.Feet | 1 | Feet. |

## What changes with Overall

- **Hitbox and collision** scale with you.
- **Reach** (using things, building) grows for a giant, so it can still open the chest at its feet. A tiny player
  keeps normal reach.
- **Swim depth** scales both ways: a giant wades through water a normal player swims in, and a tiny player swims in
  shallow water.
- **Speed**: walking, jogging, running, sneaking and swimming speed are multiplied by Overall x Legs (kept within
  0.25 to 10). The legs step at the normal rate; each stride is longer or shorter, so nothing slides.
- **Camera**: zoom distance, zoom limits and scroll speed scale with you, so the framing stays the same as a giant or a
  tiny. The camera also follows your head height when long legs or a long torso make you taller.

What doesn't change: damage, carry weight, stamina, jump height and melee weapon reach.

## Notes

- Body parts are visual only; only Overall changes your hitbox. Legs also change speed.
- There is no butt bone in the character's skeleton, so "big butt" is Hips: a wider pelvis.
- Sizes are the same on every character you play (the config is per install, not per character).
- Friends need the mod to see your sizes. Players without it see you at normal size.
- Your ragdoll (when you die) is normal size.

## Build

```
dotnet build -c Release -p:Deploy=true -p:Package=true
```

Deploys to the r2modman profile (`BepInEx/plugins/Oddhatter-ResizePlayer`) and writes `dist/Oddhatter-ResizePlayer-<version>.zip`.

Unit tests for the size rules: `dotnet test tests/ResizePlayer.Tests`.

## Install

Install with r2modman or Thunderstore Mod Manager; they also install BepInExPack_Valheim, which this mod needs.
Manual install: put `ResizePlayer.dll` in `BepInEx/plugins`. Everyone who should see sizes installs it.

## License

MIT. See [LICENSE](https://github.com/OddHatter/ResizePlayer/blob/main/LICENSE). Source: https://github.com/OddHatter/ResizePlayer
