# Height readability: options

Prompted by a player's comment:

> And because DOOM is a far more vertical creature than Wolfenstein 3D, there are moments when the architecture folds in on itself and it becomes difficult to tell what is above you, below you, or merely waiting for you with a fireball.

The goal: make it clearer whether enemies can attack you, and whether you can hit them.

## What the rules actually say

The comment is about two questions, and Doom's rules answer them differently:

- **Can I hit it?** The player can't aim up or down. Vanilla's vertical autoaim (`P_AimLineAttack`) hits a target only if the aim line reaches it, up to about ±32° up or down (±100/160) and not blocked by a ledge. The aim assist (T6.7) only picks the horizontal angle, so a monster on a high ledge that the line can't reach just can't be hit. Nothing on screen says so.
- **Can it hit me?** This doesn't depend on height. Monsters attack when `P_CheckSight` succeeds, which is a 3D check through openings. Their missiles aim at your height, and vanilla's melee range check (`P_CheckMeleeRange`) ignores height entirely. A monster "too high or too low" to attack you is really one that has no line of sight.

So a height display alone won't answer the comment. The game needs to show whether each side can hit the other.

## Options

### 1. Show who you can hit (best fit for the complaint)

- Each tic, work out what a shot would hit right now: the same aim assist and `P_AimLineAttack` result the next shot would use. Put a small target mark on that monster.
- Show monsters inside the aim cone that the aim line can't reach differently: a crossed-out mark, a faded outline, or an up/down chevron saying "above you" or "below you".
- **Determinism catch:** `P_AimLineAttack` changes shared sim state (`validcount`, `linetarget`, the slope values). This has to run inside the sim's tic as an output, or as a read-only copy outside the sim. The presentation layer must not call it directly.

### 2. Show who can hit you

- Mark monsters that are targeting the player and have line of sight, for example with a red outline instead of the black one, or a ring at their feet. Most of this is already in mobj state (`target`, see/missile state); line of sight would need computing each tic like option 1.
- A cheaper version: a short flash on a monster as it starts a missile or melee attack. That's a "fireball incoming" warning with no line-of-sight check.

### 3. General height cues (mostly options we already have)

- Turn blob shadows on by default (`--level-sprite-shadow`, off now). A shadow on the monster's own floor shows at once whether it's standing on a ledge.
- A drop line from a raised monster down to its floor.
- Turn on enemy tracers (`--level-tracers=all`) so a hit shows where it came from.

### 4. Make the level geometry easier to read

- Height-based floor shading, or a bright line along the edge of each ledge. This helps with "the architecture folds in on itself" more broadly, not just in fights.

### 5. Arrows at the screen edge

- Point to monsters that are attacking you from off-screen or behind walls the cutaway didn't open.
- Watch for conflicts with the fog of war (T6.13l), which hides those monsters on purpose.

## Recommendation

Do **1** and **2** together as one tweak-flagged presentation task, similar to T6.13j/k (for example "T6.13s Hit and threat indicators"). Then try shadows on by default (**3**), since that's only a default change.

Options 1 and 2 answer the actual question ("can we hurt each other?") rather than just "how high is it?". They also fit the twin-stick aiming, where the player gets no vertical feedback.
