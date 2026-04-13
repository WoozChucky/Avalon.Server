# Game State Tracking Redesign — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the per-tick full-snapshot field comparison with entity-level dirty bitmasks, eliminating ~60M wasted field comparisons per second at 250 concurrent instances.

**Architecture:** Each entity accumulates changed fields in a `_dirtyFields` bitmask via property setters. `MapInstance.Update()` snapshots all dirty bits into a frame dictionary before broadcasting, then passes that dictionary to `EntityTrackingSystem.Update()`. The tracking system skips entities absent from the dirty map entirely, reducing idle-entity cost to a single HashSet lookup.

**Tech Stack:** .NET 10, xUnit, NSubstitute. No new packages required.

**Spec:** `docs/superpowers/specs/2026-04-14-game-state-tracking-design.md`

---

## File Map

| File | Action | Purpose |
|------|--------|---------|
| `src/Server/Avalon.World/Entities/Creature.cs` | Modify | Add `_dirtyFields` bitmask + `ConsumeDirtyFields()`, convert tracked auto-props to backing-field setters |
| `src/Server/Avalon.World/Entities/CharacterEntity.cs` | Modify | Same as Creature — add dirty tracking to all tracked property setters |
| `src/Server/Avalon.World.Public/Scripts/SpellScript.cs` | Modify | Add `protected _dirtyFields` + `public ConsumeDirtyFields()` to base class |
| `src/Server/Avalon.World/Entities/EntityTrackingSystem.cs` | Rewrite | Remove snapshot dict + delegates; replace with `HashSet<ObjectGuid>` + dirty map lookup |
| `src/Server/Avalon.World.Public/Characters/ICharacterGameState.cs` | Modify | Change `ISet<>` to `IReadOnlyList<>` on collections; add `frameDirtyFields` param to `Update` |
| `src/Server/Avalon.World/Entities/CharacterCharacterGameState.cs` | Rewrite | Remove snapshot helper methods; use pre-allocated `List<>` collections; pass dirty map to tracking systems |
| `src/Shared/Avalon.Network.Packets/State/SInstanceStatePacket.cs` | Modify | Change `SInstanceStateRemovePacket.Create` from `ISet<ObjectGuid>` to `IEnumerable<ObjectGuid>` |
| `src/Server/Avalon.World/Instances/MapInstance.cs` | Modify | Add `_frameDirtyFields` field; add step 5a to collect dirty bits before broadcasting |
| `tests/Avalon.Server.World.UnitTests/Entities/EntityDirtyFlagShould.cs` | Create | Tests for dirty flag accumulation and `ConsumeDirtyFields()` on Creature and CharacterEntity |
| `tests/Avalon.Server.World.UnitTests/Entities/EntityTrackingSystemShould.cs` | Rewrite | Tests for the new `EntityTrackingSystem` API (no delegates, dirty map driven) |
| `tests/Avalon.Server.World.UnitTests/Entities/CharacterCharacterGameStateShould.cs` | Modify | Update test helpers and `Update` call sites for the new signature |

---

## Task 1: Add dirty flags to Creature

**Files:**
- Create: `tests/Avalon.Server.World.UnitTests/Entities/EntityDirtyFlagShould.cs`
- Modify: `src/Server/Avalon.World/Entities/Creature.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/Avalon.Server.World.UnitTests/Entities/EntityDirtyFlagShould.cs`:

```csharp
using Avalon.Common;
using Avalon.World.Entities;
using Avalon.World.Public.Enums;
using Avalon.Common.Mathematics;
using Xunit;

namespace Avalon.Server.World.UnitTests.Entities;

public class EntityDirtyFlagShould
{
    // ──────────────────────────────────────────────
    // Creature
    // ──────────────────────────────────────────────

    [Fact]
    public void Creature_Should_MarkCurrentHealth_Dirty_WhenSet()
    {
        var c = new Creature();
        c.ConsumeDirtyFields(); // clear construction noise

        c.CurrentHealth = 80u;

        var dirty = c.ConsumeDirtyFields();
        Assert.True(dirty.HasFlag(GameEntityFields.CurrentHealth));
    }

    [Fact]
    public void Creature_Should_MarkPosition_Dirty_WhenSet()
    {
        var c = new Creature();
        c.ConsumeDirtyFields();

        c.Position = new Vector3(1, 2, 3);

        Assert.True(c.ConsumeDirtyFields().HasFlag(GameEntityFields.Position));
    }

    [Fact]
    public void Creature_Should_MarkVelocity_Dirty_WhenSet()
    {
        var c = new Creature();
        c.ConsumeDirtyFields();
        c.Velocity = new Vector3(0, 1, 0);
        Assert.True(c.ConsumeDirtyFields().HasFlag(GameEntityFields.Velocity));
    }

    [Fact]
    public void Creature_Should_MarkOrientation_Dirty_WhenSet()
    {
        var c = new Creature();
        c.ConsumeDirtyFields();
        c.Orientation = new Vector3(0, 90, 0);
        Assert.True(c.ConsumeDirtyFields().HasFlag(GameEntityFields.Orientation));
    }

    [Fact]
    public void Creature_Should_MarkMoveState_Dirty_WhenSet()
    {
        var c = new Creature();
        c.ConsumeDirtyFields();
        c.MoveState = MoveState.Running;
        Assert.True(c.ConsumeDirtyFields().HasFlag(GameEntityFields.MoveState));
    }

    [Fact]
    public void Creature_Should_MarkHealth_Dirty_WhenSet()
    {
        var c = new Creature();
        c.ConsumeDirtyFields();
        c.Health = 200u;
        Assert.True(c.ConsumeDirtyFields().HasFlag(GameEntityFields.Health));
    }

    [Fact]
    public void Creature_Should_MarkLevel_Dirty_WhenSet()
    {
        var c = new Creature();
        c.ConsumeDirtyFields();
        c.Level = 5;
        Assert.True(c.ConsumeDirtyFields().HasFlag(GameEntityFields.Level));
    }

    [Fact]
    public void Creature_Should_MarkCurrentPower_Dirty_WhenSet()
    {
        var c = new Creature();
        c.ConsumeDirtyFields();
        c.CurrentPower = 50u;
        Assert.True(c.ConsumeDirtyFields().HasFlag(GameEntityFields.CurrentPower));
    }

    [Fact]
    public void Creature_Should_MarkPower_Dirty_WhenSet()
    {
        var c = new Creature();
        c.ConsumeDirtyFields();
        c.Power = 100u;
        Assert.True(c.ConsumeDirtyFields().HasFlag(GameEntityFields.Power));
    }

    [Fact]
    public void Creature_Should_MarkPowerType_Dirty_WhenSet()
    {
        var c = new Creature();
        c.ConsumeDirtyFields();
        c.PowerType = PowerType.Mana;
        Assert.True(c.ConsumeDirtyFields().HasFlag(GameEntityFields.PowerType));
    }

    [Fact]
    public void Creature_Should_AccumulateMultipleDirtyFields_BeforeConsume()
    {
        var c = new Creature();
        c.ConsumeDirtyFields();

        c.CurrentHealth = 50u;
        c.Position = new Vector3(5, 0, 5);

        var dirty = c.ConsumeDirtyFields();
        Assert.True(dirty.HasFlag(GameEntityFields.CurrentHealth));
        Assert.True(dirty.HasFlag(GameEntityFields.Position));
    }

    [Fact]
    public void Creature_Should_ReturnNone_OnSecondConsume_WithoutMutation()
    {
        var c = new Creature();
        c.ConsumeDirtyFields();
        c.CurrentHealth = 70u;
        c.ConsumeDirtyFields(); // first consume clears

        var second = c.ConsumeDirtyFields();
        Assert.False(second.HasFlag(GameEntityFields.CurrentHealth));
    }
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
dotnet test tests/Avalon.Server.World.UnitTests --filter "FullyQualifiedName~EntityDirtyFlagShould"
```

Expected: compile error — `ConsumeDirtyFields` does not exist on `Creature`.

- [ ] **Step 3: Rewrite Creature.cs with dirty flags**

Replace `src/Server/Avalon.World/Entities/Creature.cs` entirely:

```csharp
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.State;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Spells;
using Avalon.World.Public.Units;

namespace Avalon.World.Entities;

public class Creature : ICreature
{
    // Initialize to GameEntityFields.None (value 1, not 0) so the first ConsumeDirtyFields()
    // returns None and correctly skips the _frameDirtyFields insertion check.
    private GameEntityFields _dirtyFields = GameEntityFields.None;

    private Vector3 _position;
    private Vector3 _orientation;
    private Vector3 _velocity;
    private uint _health;
    private uint _currentHealth;
    private PowerType _powerType;
    private uint? _power;
    private uint? _currentPower;
    private ushort _level;
    private MoveState _moveState = MoveState.Idle;

    public CreatureTemplateId TemplateId { get; set; } = null!;
    public ObjectGuid Guid { get; set; }
    public ICreatureMetadata Metadata { get; set; }
    public string Name { get; set; } = string.Empty;
    public float Speed { get; set; }
    public string ScriptName { get; set; } = string.Empty;
    public AiScript? Script { get; set; }

    public ushort Level
    {
        get => _level;
        set { _level = value; _dirtyFields |= GameEntityFields.Level; }
    }

    public Vector3 Position
    {
        get => _position;
        set { _position = value; _dirtyFields |= GameEntityFields.Position; }
    }

    public Vector3 Orientation
    {
        get => _orientation;
        set { _orientation = value; _dirtyFields |= GameEntityFields.Orientation; }
    }

    public Vector3 Velocity
    {
        get => _velocity;
        set { _velocity = value; _dirtyFields |= GameEntityFields.Velocity; }
    }

    public uint Health
    {
        get => _health;
        set { _health = value; _dirtyFields |= GameEntityFields.Health; }
    }

    public uint CurrentHealth
    {
        get => _currentHealth;
        set { _currentHealth = value; _dirtyFields |= GameEntityFields.CurrentHealth; }
    }

    public PowerType PowerType
    {
        get => _powerType;
        set { _powerType = value; _dirtyFields |= GameEntityFields.PowerType; }
    }

    public uint? Power
    {
        get => _power;
        set { _power = value; _dirtyFields |= GameEntityFields.Power; }
    }

    public uint? CurrentPower
    {
        get => _currentPower;
        set { _currentPower = value; _dirtyFields |= GameEntityFields.CurrentPower; }
    }

    public MoveState MoveState
    {
        get => _moveState;
        set { _moveState = value; _dirtyFields |= GameEntityFields.MoveState; }
    }

    public GameEntityFields ConsumeDirtyFields()
    {
        var dirty = _dirtyFields;
        _dirtyFields = GameEntityFields.None;
        return dirty;
    }

    public void LookAt(Vector3 target)
    {
        Vector3 direction = Vector3.Normalize(target - Position);
        float yawRadians = Mathf.Atan2(direction.x, direction.z);
        float yawDegrees = yawRadians * Mathf.Rad2Deg;
        Orientation = new Vector3(0.0f, yawDegrees, 0.0f);
    }

    public bool IsLookingAt(Vector3 target, float threshold = 0.1f)
    {
        Vector3 direction = Vector3.Normalize(target - Position);
        float yawRadians = Mathf.Atan2(direction.x, direction.z);
        float yawDegrees = yawRadians * Mathf.Rad2Deg;
        Vector3 orientation = new(0.0f, yawDegrees, 0.0f);
        return Mathf.Abs(Orientation.y - orientation.y) < threshold;
    }

    public void Died(IUnit killer) => OnCreatureKilled?.Invoke(this, killer);

    public void OnHit(IUnit attacker, uint damage) => Script?.OnHit(attacker, damage);

    public void SendAttackAnimation(ISpell? spell) => OnUnitAttackAnimation?.Invoke(this, spell);

    public void SendFinishCastAnimation(ISpell spell) => OnUnitFinishedCastAnimation?.Invoke(this, spell);

    public void SendInterruptedCastAnimation(ISpell spell) => OnUnitInterruptedCastAnimation?.Invoke(this, spell);

    public static event CreatureKilledDelegate? OnCreatureKilled;
    public static event UnitAttackAnimationDelegate? OnUnitAttackAnimation;
    public static event UnitFinishedCastAnimationDelegate? OnUnitFinishedCastAnimation;
    public static event UnitInterruptedCastAnimationDelegate? OnUnitInterruptedCastAnimation;
}
```

> Note: `GameEntityFields.None` is `1 << 0` (value 1), not 0. After consume, `_dirtyFields` is reset to `GameEntityFields.None`. The `ConsumeDirtyFields()` return is checked against `GameEntityFields.None` in `MapInstance` — callers skip insertion into `_frameDirtyFields` when the value equals `GameEntityFields.None`.

- [ ] **Step 4: Run tests to confirm they pass**

```bash
dotnet test tests/Avalon.Server.World.UnitTests --filter "FullyQualifiedName~EntityDirtyFlagShould.Creature"
```

Expected: all Creature tests pass.

- [ ] **Step 5: Run full test suite to confirm no regressions**

```bash
dotnet test --no-build
```

Fix any compile errors (the `CharacterCharacterGameState` creates `Creature` via object initializer — that's fine, setters will set dirty flags which just get consumed later).

- [ ] **Step 6: Commit**

```bash
git add src/Server/Avalon.World/Entities/Creature.cs \
        tests/Avalon.Server.World.UnitTests/Entities/EntityDirtyFlagShould.cs
git commit -m "feat: add dirty field tracking to Creature"
```

---

## Task 2: Add dirty flags to CharacterEntity

**Files:**
- Modify: `tests/Avalon.Server.World.UnitTests/Entities/EntityDirtyFlagShould.cs`
- Modify: `src/Server/Avalon.World/Entities/CharacterEntity.cs`

- [ ] **Step 1: Add failing CharacterEntity tests to EntityDirtyFlagShould.cs**

Append to the `EntityDirtyFlagShould` class (before the closing `}`):

```csharp
    // ──────────────────────────────────────────────
    // CharacterEntity
    // ──────────────────────────────────────────────

    [Fact]
    public void Character_Should_MarkCurrentHealth_Dirty_WhenSet()
    {
        var c = new CharacterEntity();
        c.ConsumeDirtyFields();
        c.CurrentHealth = 90u;
        Assert.True(c.ConsumeDirtyFields().HasFlag(GameEntityFields.CurrentHealth));
    }

    [Fact]
    public void Character_Should_MarkCurrentPower_Dirty_WhenSet()
    {
        var c = new CharacterEntity();
        c.ConsumeDirtyFields();
        c.CurrentPower = 40u;
        Assert.True(c.ConsumeDirtyFields().HasFlag(GameEntityFields.CurrentPower));
    }

    [Fact]
    public void Character_Should_MarkVelocity_Dirty_WhenSet()
    {
        var c = new CharacterEntity();
        c.ConsumeDirtyFields();
        c.Velocity = new Vector3(1, 0, 0);
        Assert.True(c.ConsumeDirtyFields().HasFlag(GameEntityFields.Velocity));
    }

    [Fact]
    public void Character_Should_MarkMoveState_Dirty_WhenSet()
    {
        var c = new CharacterEntity();
        c.ConsumeDirtyFields();
        c.MoveState = MoveState.Running;
        Assert.True(c.ConsumeDirtyFields().HasFlag(GameEntityFields.MoveState));
    }

    [Fact]
    public void Character_Should_MarkRequiredExperience_Dirty_WhenSet()
    {
        var c = new CharacterEntity();
        c.ConsumeDirtyFields();
        c.RequiredExperience = 5000ul;
        Assert.True(c.ConsumeDirtyFields().HasFlag(GameEntityFields.RequiredExperience));
    }

    [Fact]
    public void Character_Should_MarkPowerType_Dirty_WhenSet()
    {
        var c = new CharacterEntity();
        c.ConsumeDirtyFields();
        c.PowerType = PowerType.Mana;
        Assert.True(c.ConsumeDirtyFields().HasFlag(GameEntityFields.PowerType));
    }

    [Fact]
    public void Character_Should_AccumulateMultipleDirtyFields_BeforeConsume()
    {
        var c = new CharacterEntity();
        c.ConsumeDirtyFields();
        c.CurrentHealth = 50u;
        c.MoveState = MoveState.Running;
        var dirty = c.ConsumeDirtyFields();
        Assert.True(dirty.HasFlag(GameEntityFields.CurrentHealth));
        Assert.True(dirty.HasFlag(GameEntityFields.MoveState));
    }

    [Fact]
    public void Character_Should_ReturnNone_OnSecondConsume_WithoutMutation()
    {
        var c = new CharacterEntity();
        c.ConsumeDirtyFields();
        c.CurrentHealth = 60u;
        c.ConsumeDirtyFields();
        Assert.False(c.ConsumeDirtyFields().HasFlag(GameEntityFields.CurrentHealth));
    }
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
dotnet test tests/Avalon.Server.World.UnitTests --filter "FullyQualifiedName~EntityDirtyFlagShould.Character"
```

Expected: compile error — `ConsumeDirtyFields` does not exist on `CharacterEntity`.

- [ ] **Step 3: Add dirty fields to CharacterEntity.cs**

Add the backing field and `ConsumeDirtyFields()` method. In `CharacterEntity.cs`, add after the existing private fields (around line 30):

```csharp
// Initialize to GameEntityFields.None (value 1, not 0) — same reason as Creature.
private GameEntityFields _dirtyFields = GameEntityFields.None;
```

Add the public method after `IsInCombat` (around line 81):

```csharp
public GameEntityFields ConsumeDirtyFields()
{
    var dirty = _dirtyFields;
    _dirtyFields = GameEntityFields.None;
    return dirty;
}
```

Then update the following auto-properties to backing-field style (add `_dirtyFields |=` in each setter):

**`CurrentHealth`** (currently `public uint CurrentHealth { get; set; }` at line 109) — replace with:
```csharp
private uint _currentHealth;
public uint CurrentHealth
{
    get => _currentHealth;
    set { _currentHealth = value; _dirtyFields |= GameEntityFields.CurrentHealth; }
}
```

**`PowerType`** (currently `public PowerType PowerType { get; set; }` at line 111) — replace with:
```csharp
private PowerType _powerType;
public PowerType PowerType
{
    get => _powerType;
    set { _powerType = value; _dirtyFields |= GameEntityFields.PowerType; }
}
```

**`CurrentPower`** (currently `public uint? CurrentPower { get; set; }` at line 125) — replace with:
```csharp
private uint? _currentPower;
public uint? CurrentPower
{
    get => _currentPower;
    set { _currentPower = value; _dirtyFields |= GameEntityFields.CurrentPower; }
}
```

**`MoveState`** (currently `public MoveState MoveState { get; set; } = MoveState.Idle;` at line 126) — replace with:
```csharp
private MoveState _moveState = MoveState.Idle;
public MoveState MoveState
{
    get => _moveState;
    set { _moveState = value; _dirtyFields |= GameEntityFields.MoveState; }
}
```

**`Velocity`** (currently `public Vector3 Velocity { get; set; }` at line 170) — replace with:
```csharp
private Vector3 _velocity;
public Vector3 Velocity
{
    get => _velocity;
    set { _velocity = value; _dirtyFields |= GameEntityFields.Velocity; }
}
```

**`RequiredExperience`** (currently `public ulong RequiredExperience { get; set; }` at line 220) — replace with:
```csharp
private ulong _requiredExperience;
public ulong RequiredExperience
{
    get => _requiredExperience;
    set { _requiredExperience = value; _dirtyFields |= GameEntityFields.RequiredExperience; }
}
```

Add `_dirtyFields |=` to existing setters that delegate to `Data`:

**`Health`** (lines 97–107) — add dirty flag in the setter body:
```csharp
public uint Health
{
    get => (uint)(Data?.Health ?? 0);
    set
    {
        if (Data != null)
        {
            Data.Health = (int)value;
        }
        _dirtyFields |= GameEntityFields.Health;
    }
}
```

**`Power`** (lines 113–123) — add dirty flag:
```csharp
public uint? Power
{
    get => (uint)(Data?.Power1 ?? 0);
    set
    {
        if (Data != null)
        {
            Data.Power1 = (int)value!;
        }
        _dirtyFields |= GameEntityFields.Power;
    }
}
```

**`Position`** (lines 154–168) — add dirty flag:
```csharp
public Vector3 Position
{
    get => new(Data?.X ?? 0, Data?.Y ?? 0, Data?.Z ?? 0);
    set
    {
        if (Data == null) return;
        Data.X = value.x;
        Data.Y = value.y;
        Data.Z = value.z;
        _dirtyFields |= GameEntityFields.Position;
    }
}
```

**`Orientation`** (lines 172–182) — add dirty flag:
```csharp
public Vector3 Orientation
{
    get => new(0, Data?.Rotation ?? 0, 0);
    set
    {
        if (Data != null)
        {
            Data.Rotation = value.y;
        }
        _dirtyFields |= GameEntityFields.Orientation;
    }
}
```

**`Level`** (lines 222–232) — add dirty flag:
```csharp
public ushort Level
{
    get => Data?.Level ?? 0;
    set
    {
        if (Data != null)
        {
            Data.Level = value;
        }
        _dirtyFields |= GameEntityFields.Level;
    }
}
```

**`Experience`** (lines 208–218) — add dirty flag:
```csharp
public ulong Experience
{
    get => Data?.Experience ?? 0;
    set
    {
        if (Data != null)
        {
            Data.Experience = value;
        }
        _dirtyFields |= GameEntityFields.Experience;
    }
}
```

- [ ] **Step 4: Run tests to confirm they pass**

```bash
dotnet test tests/Avalon.Server.World.UnitTests --filter "FullyQualifiedName~EntityDirtyFlagShould"
```

Expected: all Creature and CharacterEntity dirty flag tests pass.

- [ ] **Step 5: Run full test suite**

```bash
dotnet test --no-build
```

- [ ] **Step 6: Commit**

```bash
git add src/Server/Avalon.World/Entities/CharacterEntity.cs \
        tests/Avalon.Server.World.UnitTests/Entities/EntityDirtyFlagShould.cs
git commit -m "feat: add dirty field tracking to CharacterEntity"
```

---

## Task 3: Add dirty tracking infrastructure to SpellScript

**Files:**
- Modify: `src/Server/Avalon.World.Public/Scripts/SpellScript.cs`

SpellScript is abstract; its concrete position/velocity/orientation setters live in user-defined subclasses. The base class provides the field and the consume method. Concrete implementations must call `_dirtyFields |= GameEntityFields.Position` (etc.) in their property setters.

- [ ] **Step 1: Modify SpellScript.cs**

In `src/Server/Avalon.World.Public/Scripts/SpellScript.cs`, add after the existing `protected` members:

```csharp
using Avalon.World.Public.Enums; // add to usings if not present
```

Add this field and method inside the class body (after the `ChainedScripts` property):

```csharp
protected GameEntityFields _dirtyFields;

public GameEntityFields ConsumeDirtyFields()
{
    var dirty = _dirtyFields;
    _dirtyFields = GameEntityFields.None;
    return dirty;
}
```

The full file becomes:

```csharp
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Spells;
using Avalon.World.Public.Units;

namespace Avalon.World.Public.Scripts;

public abstract class SpellScript(ISpell spell, IUnit caster, IUnit? target) : IWorldObject
{
    protected IUnit Caster { get; } = caster;
    protected IUnit? Target { get; } = target;
    protected ISpell Spell { get; } = spell;
    protected List<SpellScript> ChainedScripts { get; private set; } = new();

    protected GameEntityFields _dirtyFields;

    public GameEntityFields ConsumeDirtyFields()
    {
        var dirty = _dirtyFields;
        _dirtyFields = GameEntityFields.None;
        return dirty;
    }

    public abstract object State { get; set; }
    public abstract Vector3 Position { get; set; }
    public abstract Vector3 Velocity { get; set; }
    public abstract Vector3 Orientation { get; set; }
    public abstract ObjectGuid Guid { get; set; }
    public abstract void Prepare();

    public SpellScript Chain(SpellScript script)
    {
        ChainedScripts.Add(script);
        return this;
    }

    public virtual void Update(TimeSpan deltaTime)
    {
        foreach (SpellScript script in ChainedScripts)
        {
            if (script.ShouldRun())
            {
                script.Update(deltaTime);
            }
        }
    }

    protected abstract bool ShouldRun();

    public virtual SpellScript Clone()
    {
        var clone = (SpellScript)MemberwiseClone();
        clone.ChainedScripts = new List<SpellScript>(ChainedScripts.Count);
        foreach (SpellScript script in ChainedScripts)
            clone.ChainedScripts.Add(script.Clone());
        return clone;
    }
}
```

> **Note for concrete spell implementations:** Any `SpellScript` subclass that sets `Position`, `Velocity`, or `Orientation` in its property setters must also add `_dirtyFields |= GameEntityFields.Position` (etc.) for movement updates to reach clients. If you have existing concrete spells in the codebase, update them now.

- [ ] **Step 2: Build to confirm it compiles**

```bash
dotnet build --no-restore
```

Expected: builds cleanly.

- [ ] **Step 3: Commit**

```bash
git add src/Server/Avalon.World.Public/Scripts/SpellScript.cs
git commit -m "feat: add dirty tracking infrastructure to SpellScript base class"
```

---

## Task 4: Rewrite EntityTrackingSystem

**Files:**
- Rewrite: `tests/Avalon.Server.World.UnitTests/Entities/EntityTrackingSystemShould.cs`
- Rewrite: `src/Server/Avalon.World/Entities/EntityTrackingSystem.cs`

- [ ] **Step 1: Rewrite the test file**

Replace `tests/Avalon.Server.World.UnitTests/Entities/EntityTrackingSystemShould.cs` entirely:

```csharp
using Avalon.Common;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Units;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Entities;

public class EntityTrackingSystemShould
{
    private static EntityTrackingSystem MakeSut() => new EntityTrackingSystem(capacity: 10);

    private static IWorldObject MakeObject(ObjectGuid? guid = null)
    {
        var obj = Substitute.For<IUnit>();
        obj.Guid.Returns(guid ?? new ObjectGuid(ObjectType.Creature, 1u));
        return obj;
    }

    private static Dictionary<ObjectGuid, GameEntityFields> EmptyDirty() => new();

    private static Dictionary<ObjectGuid, GameEntityFields> DirtyWith(ObjectGuid guid, GameEntityFields fields) =>
        new() { [guid] = fields };

    // ──────────────────────────────────────────────
    // EntityAdded
    // ──────────────────────────────────────────────

    [Fact]
    public void FireEntityAdded_WhenNewObjectAppears()
    {
        var sut = MakeSut();
        ObjectGuid? captured = null;
        sut.EntityAdded += g => captured = g;

        var obj = MakeObject(new ObjectGuid(ObjectType.Creature, 1u));
        sut.Update([obj], EmptyDirty());

        Assert.NotNull(captured);
        Assert.Equal(obj.Guid, captured);
    }

    [Fact]
    public void NotFireEntityAdded_ForAlreadyTrackedObject()
    {
        var sut = MakeSut();
        var obj = MakeObject(new ObjectGuid(ObjectType.Creature, 1u));
        sut.Update([obj], EmptyDirty());

        int addCount = 0;
        sut.EntityAdded += _ => addCount++;
        sut.Update([obj], EmptyDirty());

        Assert.Equal(0, addCount);
    }

    [Fact]
    public void FireEntityAdded_ForEachOfMultipleNewObjects()
    {
        var sut = MakeSut();
        var added = new List<ObjectGuid>();
        sut.EntityAdded += added.Add;

        var obj1 = MakeObject(new ObjectGuid(ObjectType.Creature, 1u));
        var obj2 = MakeObject(new ObjectGuid(ObjectType.Creature, 2u));
        var obj3 = MakeObject(new ObjectGuid(ObjectType.Character, 1u));
        sut.Update([obj1, obj2, obj3], EmptyDirty());

        Assert.Equal(3, added.Count);
        Assert.Contains(obj1.Guid, added);
        Assert.Contains(obj2.Guid, added);
        Assert.Contains(obj3.Guid, added);
    }

    [Fact]
    public void NotFireEntityAdded_ForEmptyUpdate()
    {
        var sut = MakeSut();
        int addCount = 0;
        sut.EntityAdded += _ => addCount++;

        sut.Update([], EmptyDirty());

        Assert.Equal(0, addCount);
    }

    // ──────────────────────────────────────────────
    // EntityRemoved
    // ──────────────────────────────────────────────

    [Fact]
    public void FireEntityRemoved_WhenObjectDisappears()
    {
        var sut = MakeSut();
        var obj = MakeObject(new ObjectGuid(ObjectType.Creature, 5u));
        sut.Update([obj], EmptyDirty());

        ObjectGuid? removed = null;
        sut.EntityRemoved += g => removed = g;
        sut.Update([], EmptyDirty());

        Assert.NotNull(removed);
        Assert.Equal(obj.Guid, removed);
    }

    [Fact]
    public void NotFireEntityRemoved_WhenObjectStillPresent()
    {
        var sut = MakeSut();
        var obj = MakeObject();
        sut.Update([obj], EmptyDirty());

        int removeCount = 0;
        sut.EntityRemoved += _ => removeCount++;
        sut.Update([obj], EmptyDirty());

        Assert.Equal(0, removeCount);
    }

    [Fact]
    public void FireEntityRemoved_ForEachDisappearedObject()
    {
        var sut = MakeSut();
        var obj1 = MakeObject(new ObjectGuid(ObjectType.Creature, 1u));
        var obj2 = MakeObject(new ObjectGuid(ObjectType.Creature, 2u));
        sut.Update([obj1, obj2], EmptyDirty());

        var removed = new List<ObjectGuid>();
        sut.EntityRemoved += removed.Add;
        sut.Update([], EmptyDirty());

        Assert.Equal(2, removed.Count);
        Assert.Contains(obj1.Guid, removed);
        Assert.Contains(obj2.Guid, removed);
    }

    [Fact]
    public void OnlyRemoveDisappearedObjects_LeavingRemainingIntact()
    {
        var sut = MakeSut();
        var staying = MakeObject(new ObjectGuid(ObjectType.Creature, 1u));
        var leaving = MakeObject(new ObjectGuid(ObjectType.Creature, 2u));
        sut.Update([staying, leaving], EmptyDirty());

        var removed = new List<ObjectGuid>();
        sut.EntityRemoved += removed.Add;
        sut.Update([staying], EmptyDirty());

        Assert.Single(removed);
        Assert.Equal(leaving.Guid, removed[0]);
    }

    // ──────────────────────────────────────────────
    // EntityUpdated — dirty map driven
    // ──────────────────────────────────────────────

    [Fact]
    public void FireEntityUpdated_WithCorrectFields_WhenEntityIsInDirtyMap()
    {
        var sut = MakeSut();
        var obj = MakeObject(new ObjectGuid(ObjectType.Creature, 3u));
        sut.Update([obj], EmptyDirty());

        GameEntityFields received = GameEntityFields.None;
        sut.EntityUpdated += (_, f) => received = f;
        sut.Update([obj], DirtyWith(obj.Guid, GameEntityFields.Position));

        Assert.Equal(GameEntityFields.Position, received);
    }

    [Fact]
    public void NotFireEntityUpdated_WhenEntityAbsentFromDirtyMap()
    {
        var sut = MakeSut();
        var obj = MakeObject(new ObjectGuid(ObjectType.Creature, 4u));
        sut.Update([obj], EmptyDirty());

        int updateCount = 0;
        sut.EntityUpdated += (_, _) => updateCount++;
        sut.Update([obj], EmptyDirty()); // entity present but not dirty

        Assert.Equal(0, updateCount);
    }

    [Fact]
    public void FireEntityUpdated_ForEachTrackedObjectPresentInDirtyMap()
    {
        var sut = MakeSut();
        var obj1 = MakeObject(new ObjectGuid(ObjectType.Creature, 1u));
        var obj2 = MakeObject(new ObjectGuid(ObjectType.Creature, 2u));
        sut.Update([obj1, obj2], EmptyDirty());

        int updateCount = 0;
        sut.EntityUpdated += (_, _) => updateCount++;

        var dirty = new Dictionary<ObjectGuid, GameEntityFields>
        {
            [obj1.Guid] = GameEntityFields.CurrentHealth,
            [obj2.Guid] = GameEntityFields.Position,
        };
        sut.Update([obj1, obj2], dirty);

        Assert.Equal(2, updateCount);
    }

    [Fact]
    public void NotFireEntityUpdated_ForNewEntity_EvenIfInDirtyMap()
    {
        // New entities always trigger EntityAdded, never EntityUpdated
        var sut = MakeSut();
        var obj = MakeObject(new ObjectGuid(ObjectType.Creature, 9u));

        int updateCount = 0;
        sut.EntityUpdated += (_, _) => updateCount++;
        sut.Update([obj], DirtyWith(obj.Guid, GameEntityFields.Position));

        Assert.Equal(0, updateCount);
    }

    // ──────────────────────────────────────────────
    // Compound / edge cases
    // ──────────────────────────────────────────────

    [Fact]
    public void HandleAddAndRemoveInSameUpdate()
    {
        var sut = MakeSut();
        var old = MakeObject(new ObjectGuid(ObjectType.Creature, 10u));
        var incoming = MakeObject(new ObjectGuid(ObjectType.Creature, 20u));
        sut.Update([old], EmptyDirty());

        var added = new List<ObjectGuid>();
        var removed = new List<ObjectGuid>();
        sut.EntityAdded += added.Add;
        sut.EntityRemoved += removed.Add;
        sut.Update([incoming], EmptyDirty());

        Assert.Single(added);
        Assert.Equal(incoming.Guid, added[0]);
        Assert.Single(removed);
        Assert.Equal(old.Guid, removed[0]);
    }

    [Fact]
    public void HandleEmptyInitialUpdate_WithoutError()
    {
        var sut = MakeSut();
        var ex = Record.Exception(() => sut.Update([], EmptyDirty()));
        Assert.Null(ex);
    }

    [Fact]
    public void HandleRepeatedEmptyUpdates_WithoutError()
    {
        var sut = MakeSut();
        sut.Update([], EmptyDirty());
        var ex = Record.Exception(() => sut.Update([], EmptyDirty()));
        Assert.Null(ex);
    }

    [Fact]
    public void HandleLargeNumberOfObjects_WithoutError()
    {
        var sut = MakeSut();
        var objects = Enumerable.Range(1, 50)
            .Select(i => MakeObject(new ObjectGuid(ObjectType.Creature, (uint)i)))
            .ToArray();

        var ex = Record.Exception(() => sut.Update(objects, EmptyDirty()));
        Assert.Null(ex);
    }

    [Fact]
    public void FireEntityAdded_Again_WhenEntityReentersAfterRemoval()
    {
        var sut = MakeSut();
        var obj = MakeObject(new ObjectGuid(ObjectType.Creature, 7u));
        sut.Update([obj], EmptyDirty()); // added
        sut.Update([], EmptyDirty());    // removed

        int addCount = 0;
        sut.EntityAdded += _ => addCount++;
        sut.Update([obj], EmptyDirty()); // re-enters

        Assert.Equal(1, addCount);
    }
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
dotnet test tests/Avalon.Server.World.UnitTests --filter "FullyQualifiedName~EntityTrackingSystemShould"
```

Expected: compile error — `Update` signature mismatch.

- [ ] **Step 3: Rewrite EntityTrackingSystem.cs**

Replace `src/Server/Avalon.World/Entities/EntityTrackingSystem.cs` entirely:

```csharp
using Avalon.Common;
using Avalon.World.Public;
using Avalon.World.Public.Enums;

namespace Avalon.World.Entities;

public class EntityTrackingSystem(int capacity)
{
    private readonly HashSet<ObjectGuid> _trackedGuids = new(capacity);
    private readonly HashSet<ObjectGuid> _seenThisFrame = new(capacity);
    private readonly List<ObjectGuid> _pendingRemovals = new(capacity);

    public event Action<ObjectGuid>? EntityAdded;
    public event Action<ObjectGuid>? EntityRemoved;
    public event Action<ObjectGuid, GameEntityFields>? EntityUpdated;

    public void Update(
        IEnumerable<IWorldObject> currentEntities,
        IReadOnlyDictionary<ObjectGuid, GameEntityFields> frameDirtyFields)
    {
        _seenThisFrame.Clear();

        foreach (var entity in currentEntities)
        {
            _seenThisFrame.Add(entity.Guid);

            if (!_trackedGuids.Contains(entity.Guid))
            {
                _trackedGuids.Add(entity.Guid);
                EntityAdded?.Invoke(entity.Guid);
                continue;
            }

            if (frameDirtyFields.TryGetValue(entity.Guid, out var dirtyFields))
            {
                EntityUpdated?.Invoke(entity.Guid, dirtyFields);
            }
        }

        foreach (var guid in _trackedGuids)
        {
            if (!_seenThisFrame.Contains(guid))
                _pendingRemovals.Add(guid);
        }

        foreach (var guid in _pendingRemovals)
        {
            _trackedGuids.Remove(guid);
            EntityRemoved?.Invoke(guid);
        }

        _pendingRemovals.Clear();
    }
}
```

- [ ] **Step 4: Run tests to confirm they pass**

```bash
dotnet test tests/Avalon.Server.World.UnitTests --filter "FullyQualifiedName~EntityTrackingSystemShould"
```

Expected: all 14 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Server/Avalon.World/Entities/EntityTrackingSystem.cs \
        tests/Avalon.Server.World.UnitTests/Entities/EntityTrackingSystemShould.cs
git commit -m "refactor: rewrite EntityTrackingSystem — HashSet + dirty map, remove snapshot delegates"
```

---

## Task 5: Update ICharacterGameState and rewrite CharacterCharacterGameState

**Files:**
- Modify: `src/Server/Avalon.World.Public/Characters/ICharacterGameState.cs`
- Rewrite: `src/Server/Avalon.World/Entities/CharacterCharacterGameState.cs`
- Rewrite: `tests/Avalon.Server.World.UnitTests/Entities/CharacterCharacterGameStateShould.cs`

- [ ] **Step 1: Update the interface**

Replace `src/Server/Avalon.World.Public/Characters/ICharacterGameState.cs`:

```csharp
using Avalon.Common;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;

namespace Avalon.World.Public.Characters;

public interface ICharacterGameState
{
    IReadOnlyList<ObjectGuid> NewObjects { get; }
    IReadOnlyList<(ObjectGuid Guid, GameEntityFields Fields)> UpdatedObjects { get; }
    IReadOnlyList<ObjectGuid> RemovedObjects { get; }

    void Update(
        Dictionary<ObjectGuid, ICreature> creatures,
        Dictionary<ObjectGuid, ICharacter> characters,
        List<IWorldObject> worldObjects,
        IReadOnlyDictionary<ObjectGuid, GameEntityFields> frameDirtyFields);
}
```

- [ ] **Step 2: Rewrite CharacterCharacterGameState.cs**

Replace `src/Server/Avalon.World/Entities/CharacterCharacterGameState.cs` entirely:

```csharp
using Avalon.Common;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;

namespace Avalon.World.Entities;

public class CharacterCharacterGameState : ICharacterGameState
{
    private const int Capacity = 100;

    private readonly EntityTrackingSystem _characterTrackingSystem;
    private readonly EntityTrackingSystem _creatureTrackingSystem;
    private readonly EntityTrackingSystem _worldObjectTrackingSystem;

    private readonly List<ObjectGuid> _newObjects = new(Capacity);
    private readonly List<(ObjectGuid Guid, GameEntityFields Fields)> _updatedObjects = new(Capacity);
    private readonly List<ObjectGuid> _removedObjects = new(Capacity);

    public IReadOnlyList<ObjectGuid> NewObjects => _newObjects;
    public IReadOnlyList<(ObjectGuid Guid, GameEntityFields Fields)> UpdatedObjects => _updatedObjects;
    public IReadOnlyList<ObjectGuid> RemovedObjects => _removedObjects;

    public CharacterCharacterGameState()
    {
        _creatureTrackingSystem = new EntityTrackingSystem(Capacity);
        _creatureTrackingSystem.EntityAdded += OnEntityFound;
        _creatureTrackingSystem.EntityUpdated += OnEntityUpdated;
        _creatureTrackingSystem.EntityRemoved += OnEntityRemoved;

        _characterTrackingSystem = new EntityTrackingSystem(Capacity);
        _characterTrackingSystem.EntityAdded += OnEntityFound;
        _characterTrackingSystem.EntityUpdated += OnEntityUpdated;
        _characterTrackingSystem.EntityRemoved += OnEntityRemoved;

        _worldObjectTrackingSystem = new EntityTrackingSystem(Capacity);
        _worldObjectTrackingSystem.EntityAdded += OnEntityFound;
        _worldObjectTrackingSystem.EntityUpdated += OnEntityUpdated;
        _worldObjectTrackingSystem.EntityRemoved += OnEntityRemoved;
    }

    public void Update(
        Dictionary<ObjectGuid, ICreature> creatures,
        Dictionary<ObjectGuid, ICharacter> characters,
        List<IWorldObject> worldObjects,
        IReadOnlyDictionary<ObjectGuid, GameEntityFields> frameDirtyFields)
    {
        _newObjects.Clear();
        _updatedObjects.Clear();
        _removedObjects.Clear();

        _creatureTrackingSystem.Update(creatures.Values, frameDirtyFields);
        _characterTrackingSystem.Update(characters.Values, frameDirtyFields);
        _worldObjectTrackingSystem.Update(worldObjects, frameDirtyFields);
    }

    private void OnEntityRemoved(ObjectGuid guid) => _removedObjects.Add(guid);
    private void OnEntityUpdated(ObjectGuid guid, GameEntityFields fields) => _updatedObjects.Add((guid, fields));
    private void OnEntityFound(ObjectGuid guid) => _newObjects.Add(guid);
}
```

- [ ] **Step 3: Rewrite CharacterCharacterGameStateShould.cs**

Replace `tests/Avalon.Server.World.UnitTests/Entities/CharacterCharacterGameStateShould.cs` entirely:

```csharp
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Entities;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Xunit;

namespace Avalon.Server.World.UnitTests.Entities;

public class CharacterCharacterGameStateShould
{
    // ──────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────

    private static Creature MakeRealCreature(uint id, Vector3 position = default, uint health = 100)
    {
        var c = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, id),
            Health = health,
            MoveState = MoveState.Idle
        };
        c.Position = position;
        c.CurrentHealth = health;
        c.Velocity = Vector3.zero;
        c.Orientation = Vector3.zero;
        c.ConsumeDirtyFields(); // clear construction dirty so tests start clean
        return c;
    }

    private static CharacterEntity MakeRealCharacter(uint id, Vector3 position = default, uint health = 100)
    {
        var c = new CharacterEntity
        {
            Guid = new ObjectGuid(ObjectType.Character, id),
            MoveState = MoveState.Idle
        };
        c.CurrentHealth = health;
        c.Velocity = Vector3.zero;
        c.MoveState = MoveState.Idle;
        c.ConsumeDirtyFields();
        return c;
    }

    private static Dictionary<ObjectGuid, ICreature> AsCreatureDict(params Creature[] creatures)
        => creatures.ToDictionary(c => c.Guid, c => (ICreature)c);

    private static Dictionary<ObjectGuid, ICharacter> AsCharacterDict(params CharacterEntity[] characters)
        => characters.ToDictionary(c => c.Guid, c => (ICharacter)c);

    private static Dictionary<ObjectGuid, GameEntityFields> EmptyDirty() => new();

    private static Dictionary<ObjectGuid, GameEntityFields> DirtyFrom(params Creature[] creatures)
    {
        var map = new Dictionary<ObjectGuid, GameEntityFields>();
        foreach (var c in creatures)
        {
            var dirty = c.ConsumeDirtyFields();
            if (dirty != GameEntityFields.None)
                map[c.Guid] = dirty;
        }
        return map;
    }

    // ──────────────────────────────────────────────
    // NewObjects — creature tracking
    // ──────────────────────────────────────────────

    [Fact]
    public void NewObjects_ContainsGuid_WhenCreatureSeenFirstTime()
    {
        var state = new CharacterCharacterGameState();
        var creature = MakeRealCreature(1u);

        state.Update(AsCreatureDict(creature), [], [], EmptyDirty());

        Assert.Contains(creature.Guid, state.NewObjects);
    }

    [Fact]
    public void NewObjects_IsEmpty_WhenSameCreatureSeenTwice()
    {
        var state = new CharacterCharacterGameState();
        var creature = MakeRealCreature(1u);
        state.Update(AsCreatureDict(creature), [], [], EmptyDirty());

        state.Update(AsCreatureDict(creature), [], [], EmptyDirty());

        Assert.Empty(state.NewObjects);
    }

    [Fact]
    public void NewObjects_ContainsOnlyFreshCreatures()
    {
        var state = new CharacterCharacterGameState();
        var old = MakeRealCreature(1u);
        state.Update(AsCreatureDict(old), [], [], EmptyDirty());

        var fresh = MakeRealCreature(2u);
        state.Update(AsCreatureDict(old, fresh), [], [], EmptyDirty());

        Assert.Single(state.NewObjects);
        Assert.Contains(fresh.Guid, state.NewObjects);
    }

    // ──────────────────────────────────────────────
    // NewObjects — character tracking
    // ──────────────────────────────────────────────

    [Fact]
    public void NewObjects_ContainsCharacterGuid_WhenSeenFirstTime()
    {
        var state = new CharacterCharacterGameState();
        var character = MakeRealCharacter(1u);

        state.Update([], AsCharacterDict(character), [], EmptyDirty());

        Assert.Contains(character.Guid, state.NewObjects);
    }

    // ──────────────────────────────────────────────
    // RemovedObjects
    // ──────────────────────────────────────────────

    [Fact]
    public void RemovedObjects_ContainsGuid_WhenCreatureDisappears()
    {
        var state = new CharacterCharacterGameState();
        var creature = MakeRealCreature(1u);
        state.Update(AsCreatureDict(creature), [], [], EmptyDirty());

        state.Update([], [], [], EmptyDirty());

        Assert.Contains(creature.Guid, state.RemovedObjects);
    }

    [Fact]
    public void RemovedObjects_IsEmpty_WhenCreatureStillPresent()
    {
        var state = new CharacterCharacterGameState();
        var creature = MakeRealCreature(1u);
        state.Update(AsCreatureDict(creature), [], [], EmptyDirty());

        state.Update(AsCreatureDict(creature), [], [], EmptyDirty());

        Assert.Empty(state.RemovedObjects);
    }

    [Fact]
    public void RemovedObjects_ContainsCharacterGuid_WhenCharacterDisappears()
    {
        var state = new CharacterCharacterGameState();
        var character = MakeRealCharacter(1u);
        state.Update([], AsCharacterDict(character), [], EmptyDirty());

        state.Update([], [], [], EmptyDirty());

        Assert.Contains(character.Guid, state.RemovedObjects);
    }

    // ──────────────────────────────────────────────
    // UpdatedObjects — dirty map driven
    // ──────────────────────────────────────────────

    [Fact]
    public void UpdatedObjects_ContainsPositionFlag_WhenCreaturePositionChanges()
    {
        var state = new CharacterCharacterGameState();
        var creature = MakeRealCreature(1u, position: Vector3.zero);
        state.Update(AsCreatureDict(creature), [], [], EmptyDirty());

        creature.Position = new Vector3(10, 0, 10);
        var frameDirty = DirtyFrom(creature);
        state.Update(AsCreatureDict(creature), [], [], frameDirty);

        var updated = state.UpdatedObjects.FirstOrDefault(o => o.Guid == creature.Guid);
        Assert.NotEqual(default, updated);
        Assert.True((updated.Fields & GameEntityFields.Position) != 0);
    }

    [Fact]
    public void UpdatedObjects_ContainsCurrentHealthFlag_WhenHealthChanges()
    {
        var state = new CharacterCharacterGameState();
        var creature = MakeRealCreature(2u, health: 100);
        state.Update(AsCreatureDict(creature), [], [], EmptyDirty());

        creature.CurrentHealth = 80u;
        var frameDirty = DirtyFrom(creature);
        state.Update(AsCreatureDict(creature), [], [], frameDirty);

        var updated = state.UpdatedObjects.FirstOrDefault(o => o.Guid == creature.Guid);
        Assert.True((updated.Fields & GameEntityFields.CurrentHealth) != 0);
    }

    [Fact]
    public void UpdatedObjects_IsEmpty_WhenNothingChanges()
    {
        // Key regression guard: idle entities must produce zero UpdatedObjects entries
        var state = new CharacterCharacterGameState();
        var creature = MakeRealCreature(3u);
        state.Update(AsCreatureDict(creature), [], [], EmptyDirty());

        // No mutations, empty dirty map
        state.Update(AsCreatureDict(creature), [], [], EmptyDirty());

        Assert.Empty(state.UpdatedObjects);
    }

    // ──────────────────────────────────────────────
    // State cleared between calls
    // ──────────────────────────────────────────────

    [Fact]
    public void NewObjects_ClearedOnSubsequentUpdate()
    {
        var state = new CharacterCharacterGameState();
        var c1 = MakeRealCreature(1u);
        var c2 = MakeRealCreature(2u);
        state.Update(AsCreatureDict(c1), [], [], EmptyDirty());
        state.Update(AsCreatureDict(c1, c2), [], [], EmptyDirty());

        Assert.DoesNotContain(c1.Guid, state.NewObjects);
        Assert.Contains(c2.Guid, state.NewObjects);
    }

    [Fact]
    public void Update_DoesNotThrow_WithAllEmptyInputs()
    {
        var state = new CharacterCharacterGameState();
        var ex = Record.Exception(() => state.Update([], [], [], EmptyDirty()));
        Assert.Null(ex);
    }

    [Fact]
    public void Update_DoesNotThrow_WithMultipleCreaturesAndCharacters()
    {
        var state = new CharacterCharacterGameState();
        var creatures = AsCreatureDict(MakeRealCreature(1u), MakeRealCreature(2u));
        var characters = AsCharacterDict(MakeRealCharacter(1u), MakeRealCharacter(2u));

        var ex = Record.Exception(() => state.Update(creatures, characters, [], EmptyDirty()));
        Assert.Null(ex);
    }
}
```

- [ ] **Step 4: Run tests**

```bash
dotnet test tests/Avalon.Server.World.UnitTests --filter "FullyQualifiedName~CharacterCharacterGameStateShould"
```

Expected: all tests pass.

- [ ] **Step 5: Run full test suite**

```bash
dotnet test --no-build
```

Fix any compile errors from callers of `ICharacterGameState.Update` (there should be one: `MapInstance.cs` — it still calls the old 3-argument signature, which will be fixed in Task 7).

- [ ] **Step 6: Commit**

```bash
git add src/Server/Avalon.World.Public/Characters/ICharacterGameState.cs \
        src/Server/Avalon.World/Entities/CharacterCharacterGameState.cs \
        tests/Avalon.Server.World.UnitTests/Entities/CharacterCharacterGameStateShould.cs
git commit -m "refactor: simplify CharacterGameState — List collections, remove snapshot helpers, accept dirty map"
```

---

## Task 6: Fix SInstanceStateRemovePacket.Create signature

**Files:**
- Modify: `src/Shared/Avalon.Network.Packets/State/SInstanceStatePacket.cs`

`BroadcastStateTo` in MapInstance passes `RemovedObjects` (now `IReadOnlyList<ObjectGuid>`) to `Create`, which currently requires `ISet<ObjectGuid>`. This fixes that mismatch.

- [ ] **Step 1: Update the Create signature**

In `SInstanceStatePacket.cs`, find `SInstanceStateRemovePacket.Create` at line 104 and change the parameter type from `ISet<ObjectGuid>` to `IEnumerable<ObjectGuid>`:

```csharp
public static NetworkPacket Create(IEnumerable<ObjectGuid> removes, Func<byte[], byte[]> encryptFunc)
{
    using var memoryStream = new MemoryStream();

    var p = new SInstanceStateRemovePacket()
    {
        Removes = removes.Select(r => r.RawValue).ToList()
    };

    Serializer.Serialize(memoryStream, p);

    var buffer = encryptFunc(memoryStream.ToArray());

    return new NetworkPacket
    {
        Header = new NetworkPacketHeader
        {
            Type = PacketType,
            Flags = Flags,
            Protocol = Protocol,
```

(The body is unchanged; only the parameter type changes from `ISet<ObjectGuid>` to `IEnumerable<ObjectGuid>`.)

- [ ] **Step 2: Build to confirm it compiles**

```bash
dotnet build --no-restore
```

- [ ] **Step 3: Commit**

```bash
git add src/Shared/Avalon.Network.Packets/State/SInstanceStatePacket.cs
git commit -m "refactor: accept IEnumerable in SInstanceStateRemovePacket.Create"
```

---

## Task 7: Update MapInstance — collect dirty fields before broadcasting

**Files:**
- Modify: `src/Server/Avalon.World/Instances/MapInstance.cs`

- [ ] **Step 1: Add the frame dirty dictionary field**

In `MapInstance.cs`, find the field declarations (before the constructor). Add:

```csharp
private readonly Dictionary<ObjectGuid, GameEntityFields> _frameDirtyFields = new(256);
```

- [ ] **Step 2: Replace step 5 in MapInstance.Update**

Find the current step 5 block in `MapInstance.Update` (lines 199–203):

```csharp
// Step 5: Update entity visibility state per character
foreach (ICharacter character in _characters.Values)
{
    character.CharacterGameState.Update(_creatures, _characters, objectSpells);
}
```

Replace with:

```csharp
// Step 5a: Snapshot dirty fields for this frame (once, before any client broadcast)
_frameDirtyFields.Clear();

foreach (var creature in _creatures.Values)
{
    var dirty = creature.ConsumeDirtyFields();
    if (dirty != GameEntityFields.None)
        _frameDirtyFields[creature.Guid] = dirty;
}

foreach (var character in _characters.Values)
{
    var dirty = character.ConsumeDirtyFields();
    if (dirty != GameEntityFields.None)
        _frameDirtyFields[character.Guid] = dirty;
}

foreach (var obj in objectSpells)
{
    if (obj is SpellScript spell)
    {
        var dirty = spell.ConsumeDirtyFields();
        if (dirty != GameEntityFields.None)
            _frameDirtyFields[spell.Guid] = dirty;
    }
}

// Step 5b: Update entity visibility state per character
foreach (ICharacter character in _characters.Values)
{
    character.CharacterGameState.Update(_creatures, _characters, objectSpells, _frameDirtyFields);
}
```

Add the using for SpellScript if not already present:

```csharp
using Avalon.World.Public.Scripts;
```

- [ ] **Step 3: Update BroadcastStateTo to use IReadOnlyList**

`BroadcastStateTo` iterates `character.CharacterGameState.NewObjects`, `UpdatedObjects`, and `RemovedObjects`. These are now `IReadOnlyList<>` instead of `ISet<>`. All `foreach` loops and `.Count` checks work identically — no changes needed in the loop bodies.

Verify the `RemovedObjects` call site at line 329:

```csharp
connection.Send(SInstanceStateRemovePacket.Create(character.CharacterGameState.RemovedObjects,
    connection.CryptoSession.Encrypt));
```

This now passes `IReadOnlyList<ObjectGuid>` to `IEnumerable<ObjectGuid>` — valid after Task 6's change.

- [ ] **Step 4: Build**

```bash
dotnet build --no-restore
```

Fix any remaining compile errors.

- [ ] **Step 5: Run the full test suite**

```bash
dotnet test
```

Expected: all tests pass. The key regression guard is `UpdatedObjects_IsEmpty_WhenNothingChanges` — if this passes, idle entities produce zero tracking overhead.

- [ ] **Step 6: Commit**

```bash
git add src/Server/Avalon.World/Instances/MapInstance.cs
git commit -m "perf: collect entity dirty fields per frame before broadcasting — eliminates idle-entity comparison cost"
```

---

## Verification

After all tasks are complete, confirm the core invariant:

```bash
dotnet test tests/Avalon.Server.World.UnitTests --filter "UpdatedObjects_IsEmpty_WhenNothingChanges"
```

Expected: PASS — an entity that has not mutated produces no `UpdatedObjects` entry, zero `EntityUpdated` events, and zero `ObjectUpdate` packets.

Run the benchmark project to get a before/after comparison if a game state benchmark exists:

```bash
dotnet run -c Release --project tools/Avalon.Benchmarking
```
