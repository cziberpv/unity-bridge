# Bug: HandleInteract uses action path for re-describe

**Status:** Identified, not fixed
**Severity:** High (breaks core interaction loop)
**Found:** 2026-02-15 during TreasureRoom playtest

## Description

After successful interaction, `HandleInteract` attempts to re-describe the scene, but passes the original `request` object (which contains the action path) to `HandleDescribe`. This causes `HandleDescribe` to look for IDescribable at the action path instead of describing the full scene.

## Expected Behavior

```
{"type": "interact", "path": "/Chest/Open"}
```

Should return:

```markdown
**Result:** You open the chest — gold coins spill light across the floor.

# Screen

Treasure Chest
  The lid is wide open, gleaming gold coins visible inside
Lever
  A rusty lever protruding from the wall.
  [interact] /Lever/Pull — "See what happens"
...
```

## Actual Behavior

Returns:

```markdown
**Result:** You open the chest — gold coins spill light across the floor.

No IDescribable found at `/Chest/Open` or its children.
```

## Root Cause

In `UnityBridge.Describe.cs`, `HandleInteract`:

```csharp
private static string HandleInteract(BridgeRequest request)
{
    // ... execute action ...

    // Return result + full describe (vdoh-vydoh: interaction changes may ripple across the whole screen)
    var sb = new StringBuilder();

    if (!string.IsNullOrEmpty(result))
    {
        sb.AppendLine($"**Result:** {result}");
        sb.AppendLine();
    }

    sb.Append(HandleDescribe(request));  // BUG: request.path = "/Chest/Open"

    return sb.ToString();
}
```

`HandleDescribe(request)` receives the same request with `path = "/Chest/Open"`, which it interprets as "describe only IDescribable at this path".

## Fix

Create a new `BridgeRequest` with empty path for full scene describe:

```csharp
// Create fresh request for full scene describe
var describeRequest = new BridgeRequest { type = "describe", path = null };
sb.Append(HandleDescribe(describeRequest));
```

Or call overload if available:

```csharp
sb.Append(HandleDescribe(null));  // null path = full scene
```

## Impact

This breaks the "vdoh-vydoh" (inhale-exhale) pattern that is core to the protocol philosophy:
- interact = action (exhale)
- describe = perception (inhale)

Without automatic re-describe, agents must manually call `describe` after each `interact`, which:
- Adds friction
- Breaks flow
- Makes interaction feel incomplete

## Workaround

Call `describe` separately after each `interact`:

```json
{"type": "interact", "path": "/Chest/Open"}
{"type": "describe"}
```

This works but defeats the purpose of the integrated loop.

## Test Case

1. Create scene with IDescribable widget
2. Call `{"type": "interact", "path": "/Widget/Action"}`
3. Verify response includes full scene describe (not error)

## References

- Playtest report: docs/playtest/2026-02-15-treasure-room.md
- Code: Editor/UnityBridge.Describe.cs, HandleInteract method
