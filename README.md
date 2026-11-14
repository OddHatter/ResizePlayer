# ResizePlayer

Change the size of your character: become a giant or a tiny, or just give yourself a big head, a big butt, long legs
or huge hands.

- **Overall** scales your whole character, including the hitbox and collision, so a giant really is big and a tiny
  player really fits through small gaps.
- Each **body part** (head, neck, chest, belly, hips, arms, hands, legs, feet) has its own size. Parts are independent:
  a big head on a tiny body works.
- When you change **Legs**, **Hips** or **Feet**, your body is raised or lowered so your feet stay on the ground, in
  whatever pose you're in (standing, sneaking, sitting, lying in bed).
- Weapons, shields, helmets, hair and anything else you carry grow and shrink with the body part holding them (so with
  Overall, and with Hands or Head). Their particle effects (FrostFire flames, BloodLightning sparks, Berzerkr swirls,
  torch fire) are sized with them.
- You move faster or slower with your stride: speed follows your leg length (Overall x Legs), at the normal step rate.
  A bigger stride jumps that many times as high, and fall damage counts drops in those jump heights; a tiny player
  jumps as high as a normal one and takes normal fall damage. Gravity follows your size, so falls look natural.
- The camera follows your size, so you always see your character the same way.
- Edits to the config file apply right away, while the game is running.
- Multiplayer: friends who also have the mod see your sizes, and you see theirs.

## Config (`BepInEx/config/com.oddhatter.resizeplayer.cfg`)

Sizes are multipliers: 1 = normal, 2 = twice as big, 0.5 = half. Each size allows 0.25 to 10.

| Setting | Default | Meaning |
|---|---|---|
| General.Enabled | true | false = everyone looks vanilla to you, and other players see you at normal size. |
| Size.Overall | 1 | Whole body, including hitbox, collision, equipment, speed, jumps, reach, swim depth and camera. |
| Size.Head | 1 | Head (with hair, beard and helmet). |
| Size.Neck | 1 | Neck. |
| Size.Chest | 1 | Chest and shoulders area. |
| Size.Belly | 1 | Belly and lower back. |
| Size.Hips | 1 | Pelvis and butt. Your legs are kept on the ground. |
| Size.Arms | 1 | Upper and lower arms. |
| Size.Hands | 1 | Hands. |
| Size.Legs | 1 | Upper and lower legs. Your body is raised or lowered to keep your feet on the ground, and longer legs move you faster and jump higher. |
| Size.Feet | 1 | Feet. You stand on their soles. |

## What changes with Overall

- **Hitbox and collision** scale with you.
- **Reach** (using things, building) grows with a giant, so it can still open the chest at its feet; a tiny player
  keeps normal reach, so building with full-size pieces still works. Long legs or a long torso add the extra height of
  your eyes, so a tall character still reaches the ground.
- **Swim depth** scales both ways: a giant wades through water a normal player swims in, and a tiny player starts
  swimming in water about as deep, for its size, as a normal player does (not in every puddle).
- **Speed**: walking, jogging, running, sneaking and swimming speed are multiplied by Overall x Legs (kept within
  0.25 to 10). The legs step at the normal rate; each stride is longer or shorter, so nothing slides.
- **Jumps** go Overall x Legs times as high when that is above 1; their length changes even more, since your speed does
  too. **Fall damage** scales the same way: it starts at a drop of 4 m times that factor, so a giant can jump down from
  anything it can jump onto. Below 1 a tiny player keeps the normal jump height (small animals jump about as high as
  big ones) and normal fall damage, so it can still hop onto full-size steps and land its own jumps unhurt.
- **Gravity** scales with Overall x Legs too, so falls look normal for your size: a giant drops briskly instead of in
  slow motion, and a tiny player falls a bit slower than normal instead of plummeting (half gravity at 0.25, so its
  normal-height jump hangs in the air longer).
  The **feather cape**'s fall-speed limit scales the same way on the ground, so a giant running downhill stays on the
  slope; in the air a giant's limit grows only with the square root of that factor, so it still floats down gently.
- **Feet** are planted by the game's foot IK at a height that follows your foot size, so a tiny player stands up
  straight instead of looking crouched.
- **Ground and water**: a giant stands firmly on slopes and bumps, and wades through water slowed by the same share as a
  normal player in water as deep for its size (the game's water drag would otherwise stop a fast giant dead).
- **Monsters** reach a giant's body: its hit capsule is giant too.
- **Camera**: zoom distance, zoom limits and scroll speed scale with you, so the framing stays the same as a giant or a
  tiny. The camera also follows your head height when long legs or a long torso make you taller. A giant can still
  point at, use and build on things all the way out at the furthest zoom, and a tiny player stays visible with the camera
  close. The camera follows height changes smoothly, so long legs don't shake it while running.

What doesn't change: damage, carry weight, stamina and melee weapon reach.

## Notes

- Body parts are visual only; only Overall changes your hitbox. Legs also change speed and jumps.
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
