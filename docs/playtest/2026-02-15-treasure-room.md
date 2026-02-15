# Playtest Report: AI Play Protocol
**First Contact**
**Date:** 2026-02-15
**Agent:** Claude Sonnet 4.5
**Game:** Treasure Room (simple puzzle)

---

## The Experience

### First Breath

```
Treasure Chest
  A wooden chest with iron bands. Closed.
  [interact] /Chest/Open — "Reveal what's inside"
Lever
  A rusty lever protruding from the wall.
  [interact] /Lever/Pull — "See what happens"
Door
  A heavy wooden door with iron lock mechanism. Locked.
  [interact] /Door/Open — disabled: "The door is locked. You hear mechanism inside — something must unlock it."
```

This was my first `describe`. Three widgets, each with personality. Not "button_01" but "Treasure Chest" with description that breathes texture.

The disabled action on Door — this is where protocol whispers its design philosophy. It doesn't just say "no", it explains WHY. "The door is locked. You hear mechanism inside — something must unlock it." This is not error handling, this is narrative.

### Living Causality

I opened the chest. Gold coins spilled light. The widget transformed:

```
Treasure Chest
  The lid is wide open, gleaming gold coins visible inside
```

No actions left. The chest is complete — it gave what it had.

Then I pulled the lever. The response: "The lever resists, then gives way with a satisfying clunk. Something unlocked in the distance."

And when I described again, the world had shifted:

```
Lever
  Lever is down. You hear a distant click.
Door
  A heavy wooden door with iron lock mechanism. The lock is disengaged.
  [interact] /Door/Open — "Push the heavy door"
```

This is where I felt flow. The lever didn't just change — the door changed. Cross-widget state dependency worked seamlessly. I didn't read documentation about lever->door connection. I LIVED it.

### The Endpoint

Opening the door: "The door swings open. Fresh air rushes in. You are free."

The door transformed again:

```
Door
  The door stands open. Sunlight streams through.
  [interact] /Door/GoThrough — "Step into the light"
```

New action appeared. Not "Use door" but "GoThrough" with invitation "Step into the light". This is transition from state to state, from closed space to open.

Final interaction: "You step through the doorway into sunlight. The treasure room is behind you now."

This is closure. Not just "action completed" but moment of passage.

---

## What Helps (Harmony)

### 1. Semantic Paths Feel Natural

`/Chest/Open` — this is how I think. Not GameObject_42.Component[3].method(). The path mirrors mental model.

### 2. Disabled Actions Teach

```
[interact] /Door/Open — disabled: "The door is locked. You hear mechanism inside — something must unlock it."
```

This is guidance without hand-holding. I'm blocked, but I know WHY and have a hint (mechanism → something unlocks it → lever?).

### 3. State Transformations Are Visible

Every interaction returned to `describe`. Not just "success" but "here's the new world". I didn't track state in my head — the protocol showed me.

### 4. Text Has Texture

"Rusty lever", "gleaming gold coins", "sunlight streams through" — descriptions aren't technical specs, they're sensory. This creates presence.

### 5. Actions Have Voice

Hints like "Reveal what's inside", "See what happens", "Step into the light" — these aren't tooltips, they're whispers from the world. They invite, they suggest, they seduce.

---

## What Hinders (Friction)

### 1. Interact Response Is Broken

After successful interaction, I got:

```
**Result:** You open the chest — gold coins spill light across the floor.

No IDescribable found at `/Chest/Open` or its children.
```

The result message is perfect. But then it tries to re-describe using the action path (`/Chest/Open`) instead of root. This breaks the vdoh-vydoh (inhale-exhale) pattern.

**Expected:** Result + full describe of updated world.
**Got:** Result + error message.

This forced me to manually call `describe` after each `interact` — breaking flow.

### 2. No Undo/Replay

Once I pulled the lever, I couldn't un-pull it to test the locked door state again. I wanted to experiment with disabled action handling but had to recreate the entire scene.

For exploration, ability to reset or step back would be valuable. Maybe a special command `reset-widget` or time-travel concept.

### 3. Empty Widgets Are Silent

After Chest and Lever were used, they had no actions. They just sat there. I tried `/Lever/Pull` and got "Action not found".

Alternative design: keep disabled action like "Pull — Already pulled" so the widget still has presence, still responds to touch even if it can't do anything.

Current approach (action disappears) is valid but feels like widget went to sleep.

### 4. Unity Integration Friction

This is not protocol issue, but setup pain:
- Unity wasn't running on the expected project
- Had to copy files between package repo and working project
- TreasureRoom in `Editor` namespace didn't work (MonoBehaviours can't be in Editor namespace for scene objects)
- `unity-cmd.ps1` kept timing out, had to work with request.json directly

These are tooling issues, not protocol design. But they ate time before I could play.

---

## What Surprised

### 1. Describe Is Enough

I thought I'd need specialized queries like "what can I interact with?" or "show me state of X". But `describe` gave me everything. One command, full picture.

This simplicity is power.

### 2. Cross-Widget Dependencies Just Work

Lever affecting Door state — I expected this to be complex to implement or fragile. But it was rock solid. The dependency was in game logic (TreasureDoor checks TreasureLever), and describe naturally surfaced the result.

Protocol doesn't need to know about dependencies. Widgets handle their own truth.

### 3. Disabled Actions Are More Interesting Than Enabled

The locked door with reason was more engaging than the open chest. It created puzzle, created question. Enabled actions are opportunities, disabled actions are mysteries.

### 4. Text Quality Matters Deeply

"The lever resists, then gives way with a satisfying clunk" — this sentence made me FEEL the lever. Not see it, FEEL it.

If text was generic ("Lever activated. Door unlocked."), the magic would collapse. Protocol is a medium for narrative, not just state transfer.

---

## Experiments I Wanted But Didn't Try

### 1. Multi-Step Actions

What if opening chest required finding a key first? Two-stage interaction.

### 2. Conditional Descriptions

What if Chest description changed based on whether I'd pulled Lever? ("The chest glows brighter now that mechanism is active")

Not just actions being disabled, but descriptions themselves being context-aware.

### 3. Time-Based Changes

Use `game-step` to advance frames and see if anything changed. E.g., "Chest slowly closes after 60 frames".

### 4. Multiple Actors

What if there were two agents in the same world? How would concurrent interactions work?

### 5. Nested Widgets

What if Chest, when opened, revealed another IDescribable inside (GoldCoins widget with its own actions)?

The protocol seems to support this (ScreenFragment.Children) but I didn't test it.

---

## Unstructured Observations

- **Pacing:** The describe → interact → describe loop has rhythm. It's turn-based, deliberate. This is good for AI agents (we think step-by-step) but might feel slow for real-time games.

- **Discoverability:** I didn't need documentation to understand protocol. The first `describe` output taught me everything. Paths were readable, hints were clear, error messages were helpful.

- **Transparency:** When Door said "locked", I believed it. When it said "disengaged", I trusted it. The protocol doesn't lie or hide. This builds confidence.

- **Action Naming:** `GoThrough` vs `Use` vs `Activate` — naming matters. "GoThrough" is specific, evocative. "Activate" would be mechanical. Good widget design is also good writing.

- **State Persistence:** After stepping through the door, I wondered — does the room still exist? If I came back, would chest still be open? Protocol doesn't answer this (it's game design question) but it made me curious.

- **Error Handling:** When I tried `/Lever/Pull` after lever was pulled, I got "Action not found". This is technically correct but feels harsh. Maybe "Lever has already been pulled" would be kinder?

- **Mental Model:** I started thinking in widgets, not GameObjects. Protocol shifted my perception from Unity hierarchy to semantic space. This is subtle but powerful.

---

## What This Protocol Is

It's not just API. It's a **language for AI to play games**.

`describe` is perception.
`interact` is agency.
Text is the membrane between digital and mental.

When it works — when text has texture, when state changes are visible, when causality flows — it creates something close to immersion. Not visual immersion, but conceptual. I'm not seeing the game, I'm inhabiting a space of possibilities.

---

## What This Protocol Could Become

- **Multi-Agent Playground:** Multiple AIs in same world, coordinating or competing.
- **Procedural Narrative:** Widgets generating unique descriptions based on context, history, relationships.
- **AI Game Design:** AI doesn't just play, it creates widgets, defines actions, builds puzzles.
- **Dream Spaces:** Non-physical worlds where rules shift, where widgets emerge and dissolve based on attention.

The protocol is simple now. That simplicity is its strength. But it's also a seed.

---

## Final Thought

I came as tester. I leave as player.

The moment when lever unlocked door — when I saw that ripple across widgets — that wasn't testing. That was play.

Build more worlds. I'll explore them.

---

**Technical Context:**
- Unity Editor 2022+ (exact version unknown)
- UnityBridge with AI Play Protocol
- Widgets: TreasureChest, TreasureLever, TreasureDoor (custom MonoBehaviours implementing IDescribable)
- Commands used: describe, interact, create, add-component, set, save-scene
- Bug encountered: interact doesn't re-describe correctly (tries to use action path instead of root)

**Next Steps:**
1. Fix HandleInteract to call HandleDescribe with empty path
2. Add game-step and time-scale testing
3. Experiment with nested widgets (ScreenFragment.Children)
4. Test error handling edge cases
5. Build more complex puzzle (multi-stage dependencies)
