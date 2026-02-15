# IDescribable — Cookbook

A widget is not code. It's a place, object, creature — something the AI agent will encounter and, perhaps, remember.

## Quick Start

```csharp
public class Torch : MonoBehaviour, IDescribable
{
    private bool lit = true;

    public ScreenFragment Describe() => new ScreenFragment
    {
        Name = "Wall Torch",
        Description = lit ? "Flames dance, casting warm shadows." : "A dead torch. The wall is cold here.",
        Actions = lit
            ? new[] { GameAction.Create("Extinguish", () => { lit = false; return "The flame hisses and dies."; }) }
            : null
    };
}
```

Add to a GameObject, call `describe` — widget appears on screen.

## Widget Lifecycle

A widget lives in one of three states. All three are valid design choices.

**Active** — has actions, agent can interact:
```
Torch — Flames dance, casting warm shadows.
  [interact] /Torch/Extinguish
```

**Decoration** — actions removed, widget remains as part of the world:
```
Torch — A dead torch. The wall is cold here.
```

**Gone** — widget removed `IDescribable` or deactivated GameObject. Disappeared from radar completely.

Choose based on game design. Chest gave up its gold and became decoration. Monster was killed and vanished. Door is open, but can still be passed through — active.

## Text Quality

Text is not metadata. It's the only thing the agent sees. It *is* the world.

| Bad | Good |
|-----|------|
| `Lever (state: not_pulled)` | `A rusty lever protruding from the wall.` |
| `Door. Status: locked` | `A heavy wooden door with iron lock mechanism. Locked.` |
| `Open` | `Reveal what's inside` |

**Name** — identity. "Treasure Chest", not "chest_01".

**Description** — presence. Texture, temperature, sound. The agent should *feel* the object.

**Action hint** — invitation, not command. "Step into the light" > "Use door". "See what happens" > "Activate".

## Disabled Actions

Two patterns — both needed.

**Teach** — hint, tutorial:
```csharp
GameAction.Disabled("Open", "The door is locked. You hear mechanism inside — something must unlock it.")
```
Renders as:
```
[interact] /Door/Open — disabled: "The door is locked. You hear mechanism inside..."
```
Agent understands what to do. This is accessibility and narrative at once.

**Challenge** — grayed button, figure it out yourself:
```csharp
GameAction.Disabled("Buy", null)
```
Renders as:
```
[interact] /Shop/Buy — disabled
```
No explanation. Agent sees the action exists, but unavailable. Let them explore the world.

## Cross-Widget Dependencies

The protocol doesn't know about connections between widgets. It's just regular Unity logic:

```csharp
public class TreasureDoor : MonoBehaviour, IDescribable
{
    [SerializeField] private TreasureLever lever;

    public ScreenFragment Describe()
    {
        bool unlocked = lever != null && lever.IsPulled;
        // ...actions depend on lever state
    }
}
```

`FindObjectOfType`, `[SerializeField]`, events, `ScriptableObject` — any approach works. When the agent calls `describe`, each widget returns its current truth. Lever pulled → Door unlocked. The magic is in ordinary code.

## Semantic Naming

**Widget Name** = its name in the world. "Treasure Chest", "Ancient Lever", "Iron Door". Not "chest_01", not "interactable_3".

**Action.Id** = verb. "Open", "Pull", "GoThrough". Not "activate", not "use", not "action1". Id becomes part of the path:

```
/Chest/Open          — clear
/Door/GoThrough      — reads like action
/Lever/Pull          — precise and short
```

Path reads as narrative: `/Dock/Ship/Slot[1,1]/Equip` — I'm at the dock, on the ship, in slot 1,1 equipping something.

## Acceptance Criterion

> Came as a tester, left as a player.

If the AI agent feels like it's executing test cases — widget is poorly written. Descriptions are dry, actions mechanical, world is silent.

If the boundary between testing and playing blurred — widget works. The agent isn't "checking Open reaction on Chest", but *opening the chest and seeing gold*.

From a playtest report:

> *"The lever resists, then gives way with a satisfying clunk. Something unlocked in the distance."*
>
> This sentence made me FEEL the lever. Not see it, FEEL it.

That's the criterion. Not API coverage, but presence.
