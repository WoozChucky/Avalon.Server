using System.Data;
using System.Globalization;
using Avalon.Api.Contract;
using Avalon.Combat;
using Avalon.Common.ValueObjects;
using Avalon.Database.World;
using Avalon.Domain.Auth;
using Avalon.Domain.World;
using Avalon.Infrastructure.Scripts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Avalon.Api.Worlds.Templates;

public enum TemplateEditOutcome
{
    Saved,
    NotFound,
    /// <summary>The row is not at the version the caller read.</summary>
    Conflict,
    /// <summary>
    /// Another template edit committed at the same moment and the database refused this one (a serialization failure,
    /// #1179): nothing was written, and the same request may simply be sent again.
    /// </summary>
    ConcurrentSave,
    Invalid,
}

/// <param name="Row">The row as stored (with <see cref="TemplateEditOutcome.Saved"/>).</param>
/// <param name="Reload">How the world's reload went (with <see cref="TemplateEditOutcome.Saved"/>).</param>
/// <param name="Errors">The field errors (with <see cref="TemplateEditOutcome.Invalid"/>).</param>
public sealed record TemplateEditResult<TRow>(
    TemplateEditOutcome Outcome,
    TRow? Row = default,
    TemplateReloadResult? Reload = null,
    IDictionary<string, string[]>? Errors = null)
    where TRow : class;

/// <summary>One changed field in the audit line.</summary>
public sealed record TemplateChange(string Field, string? Old, string? New)
{
    public override string ToString() => $"{Field}: {Old ?? "null"} -> {New ?? "null"}";
}

/// <summary>Who saves, and on which world.</summary>
public readonly record struct TemplateEditCaller(WorldId World, AccountId Account);

/// <summary>
/// Saves an edit to an item, ability, creature or aura template. In one transaction: load the row, check the version the
/// caller read, validate, apply, save. Then one audit line naming only the changed fields, and a reload request for
/// the world. The editable-world and If-Match-present checks come before this (<see cref="TemplateEditGuard"/>).
/// </summary>
public sealed class TemplateEditService(
    IDbContextFactory<WorldDbContext> contexts,
    ITemplateReloadSignal reload,
    IWorldScriptCatalog scripts,
    ILogger<TemplateEditService> logger)
{
    public Task<TemplateEditResult<ItemTemplate>> EditItemAsync(
        TemplateEditCaller caller, ulong id, string ifMatch, UpdateItemTemplateRequest request, CancellationToken ct)
    {
        string? stored = null; // the use script as the row held it, before the request is applied
        return RunAsync(new Kind<ItemTemplate, UpdateItemTemplateRequest>(
            "Item", TemplateReloadArea.Items, TemplateFields.Item,
            async (db, ct2) =>
            {
                // A query, not Find: its base-stat ranges (#1179) load with it (the model auto-includes them), so the
                // version covers them and the edit replaces the rows it read.
                ItemTemplate? found = await db.ItemTemplates.FirstOrDefaultAsync(t => t.Id == new ItemTemplateId(id), ct2);
                stored = found?.UseScript;
                return found;
            },
            TemplateVersion.Of, TemplateValidation.Item,
            async (db, row, errors, ct2) =>
            {
                TemplateValidation.ItemWorldRules(errors, row);
                await TemplateItemUsers.ValidateAsync(db, row, errors, ct2);
                ScriptCatalogSnapshot? catalog = await scripts.GetAsync(caller.World, ct2);
                TemplateValidation.ScriptKnown(errors, row.UseScript, stored, catalog, c => c.Item, "useScript");
            }),
            caller, id, ifMatch, request, ct);
    }

    public Task<TemplateEditResult<AbilityTemplate>> EditAbilityAsync(
        TemplateEditCaller caller, uint id, string ifMatch, UpdateAbilityTemplateRequest request, CancellationToken ct)
    {
        string? stored = null; // the script name as the row held it, before the request is applied
        return RunAsync(new Kind<AbilityTemplate, UpdateAbilityTemplateRequest>(
            "Ability", TemplateReloadArea.Abilities, TemplateFields.Ability,
            async (db, ct2) =>
            {
                AbilityTemplate? found = await db.AbilityTemplates.FindAsync([new AbilityId(id)], ct2);
                stored = found?.ScriptName;
                return found;
            },
            TemplateVersion.Of, TemplateValidation.Ability,
            async (db, row, errors, ct2) =>
            {
                // A class ability is one the talent tree grants (#1181): a class start or a node links it. Read in the
                // edit's own transaction, as the row is.
                bool granted = await db.TalentNodeAbilities.AnyAsync(a => a.AbilityId == row.Id, ct2);
                TemplateValidation.AbilityWorldRules(errors, row, granted);
                ScriptCatalogSnapshot? catalog = await scripts.GetAsync(caller.World, ct2);
                TemplateValidation.ScriptKnown(errors, row.ScriptName, stored, catalog, c => c.Ability);
                // The aura must exist (its foreign key) and be one the world loads, fit what the ability affects, and find
                // a damage type on a class ability when it deals damage over time (#1178), and have the ability as a source
                // it can scale with (its tags and spell base), as the world's ability catalog checks the link. A
                // concurrent edit of that aura's Kind can still race this check (it reads the aura, not a lock on it); the
                // world's catalog then refuses the link and logs it.
                AuraTemplate? loaded = null;
                if (row.AuraId is { } auraId)
                {
                    AuraTemplate? aura = await db.AuraTemplates.AsNoTracking().Include(a => a.Modifiers)
                        .FirstOrDefaultAsync(a => a.Id == auraId, ct2);
                    loaded = aura is not null && AuraRules.Problem(aura) is null ? aura : null;
                    if (AuraRules.LinkProblem(row.Affects, auraId, _ => loaded) is { } problem)
                        errors.Add("auraId", $"The world would refuse this ability: {problem}.");
                    else if (loaded is not null && AuraRules.DamageTypeProblem(row, loaded, granted) is { } typeProblem)
                        errors.Add("damageType", $"The world would refuse this ability: {typeProblem}.");
                    else if (loaded is not null && AuraRules.SourceProblem(row, loaded, granted) is { } sourceProblem)
                        errors.Add(TemplateValidation.AbilitySourceField(sourceProblem), $"The world would refuse this ability: {sourceProblem}.");
                }

                // The capstones that shape it (#1181 Part D) must still fit it and its aura, or the world would refuse the
                // whole talent tree. Of its fields, only the effects and the aura can break one.
                if (await CapstoneProblemAsync(db, row, loaded, ct2) is ({ } capstone, { } capstoneProblem))
                {
                    bool onAura = capstone.Kind is Domain.World.TalentModifierKind.AuraStacks
                        or Domain.World.TalentModifierKind.AuraDurationPct;
                    errors.Add(onAura ? "auraId" : "effects", $"The world would refuse the talent tree: {capstoneProblem}.");
                }
            }),
            caller, id, ifMatch, request, ct);
    }

    public Task<TemplateEditResult<AuraTemplate>> EditAuraAsync(
        TemplateEditCaller caller, uint id, string ifMatch, UpdateAuraTemplateRequest request, CancellationToken ct)
    {
        string? stored = null; // the script name as the row held it, before the request is applied
        return RunAsync(new Kind<AuraTemplate, UpdateAuraTemplateRequest>(
            "Aura", TemplateReloadArea.Auras, TemplateFields.Aura,
            async (db, ct2) =>
            {
                AuraTemplate? found = await db.AuraTemplates.Include(a => a.Modifiers)
                    .FirstOrDefaultAsync(a => a.Id == new AuraId(id), ct2);
                stored = found?.ScriptName;
                return found;
            },
            TemplateVersion.Of, TemplateValidation.Aura,
            async (db, row, errors, ct2) =>
            {
                TemplateValidation.AuraWorldRules(errors, row);
                ScriptCatalogSnapshot? catalog = await scripts.GetAsync(caller.World, ct2);
                TemplateValidation.ScriptKnown(errors, row.ScriptName, stored, catalog, c => c.Aura);
                // The aura must still fit every ability that applies it, a damaging one a class ability (one the talent tree
                // grants, #1181) naming its damage type, and a ticking one a source it can scale with (#1178), or the world
                // would refuse those abilities.
                foreach (AbilityTemplate ability in await db.AbilityTemplates.AsNoTracking()
                             .Where(a => a.AuraId == row.Id).ToListAsync(ct2))
                {
                    bool isGranted = await db.TalentNodeAbilities.AnyAsync(l => l.AbilityId == ability.Id, ct2);
                    if (AuraRules.LinkProblem(ability.Affects, row.Id, _ => row) is { } problem)
                    {
                        errors.Add("kind", $"The world would refuse ability {ability.Id.Value} '{ability.Name}': {problem}.");
                    }
                    else if ((AuraRules.DamageTypeProblem(ability, row, isGranted)
                              ?? AuraRules.SourceProblem(ability, row, isGranted)) is { } typeProblem)
                    {
                        errors.Add("periodicKind", $"The world would refuse ability {ability.Id.Value} '{ability.Name}': {typeProblem}.");
                    }

                    // The capstones that shape the ability (#1181 Part D) must still fit the aura: never on crowd control,
                    // and a stack modifier only on an aura that stacks, or the world would refuse the whole talent tree.
                    if (await CapstoneProblemAsync(db, ability, row, ct2) is (_, { } capstoneProblem))
                    {
                        string key = row.Control != Domain.World.CrowdControl.None ? "control"
                            : AuraRules.IsCrowdControl(row) ? "modifiers" : "stacking";
                        errors.Add(key, $"The world would refuse the talent tree: {capstoneProblem}.");
                    }
                }
            }),
            caller, id, ifMatch, request, ct);
    }

    /// <summary>
    /// The first capstone row that could not shape <paramref name="ability" /> with <paramref name="aura" /> as its aura
    /// (<see cref="TalentModifierRules.AbilityProblem" />, the world catalog's own check), read in the edit's transaction.
    /// </summary>
    private static async Task<(TalentAbilityModifier Row, string Problem)?> CapstoneProblemAsync(
        WorldDbContext db, AbilityTemplate ability, AuraTemplate? aura, CancellationToken ct)
    {
        List<TalentAbilityModifier> capstones = await db.TalentAbilityModifiers.AsNoTracking()
            .Where(m => m.AbilityId == ability.Id).ToListAsync(ct);
        return capstones.Count == 0 ? null : TalentModifierRules.AbilityProblem(capstones, ability, aura);
    }

    public Task<TemplateEditResult<CreatureTemplate>> EditCreatureAsync(
        TemplateEditCaller caller, ulong id, string ifMatch, UpdateCreatureTemplateRequest request, CancellationToken ct)
    {
        string? stored = null; // the script name as the row held it, before the request is applied
        return RunAsync(new Kind<CreatureTemplate, UpdateCreatureTemplateRequest>(
            "Creature", TemplateReloadArea.Creatures, TemplateFields.Creature,
            async (db, ct2) =>
            {
                CreatureTemplate? found = await db.CreatureTemplates.FindAsync([new CreatureTemplateId(id)], ct2);
                stored = found?.ScriptName;
                return found;
            },
            TemplateVersion.Of, TemplateValidation.Creature,
            async (db, row, errors, ct2) =>
            {
                TemplateValidation.CreatureWorldRules(errors, row);
                ScriptCatalogSnapshot? catalog = await scripts.GetAsync(caller.World, ct2);
                TemplateValidation.ScriptKnown(errors, row.ScriptName, stored, catalog, c => c.Ai);
                // A loot table that does not exist would break its foreign key, which is not a check violation.
                if (row.LootTableId is { } loot && await db.LootTables.FindAsync([loot], ct2) is null)
                    errors.Add("lootTableId", $"There is no loot table {loot.Value}.");
            }),
            caller, id, ifMatch, request, ct);
    }

    /// <summary>One template kind: where its row is, how to read it, check it and which fields it has.</summary>
    private sealed record Kind<TRow, TRequest>(
        string Name,
        TemplateReloadArea Area,
        IReadOnlyList<TemplateField<TRow, TRequest>> Fields,
        Func<WorldDbContext, CancellationToken, Task<TRow?>> Find,
        Func<TRow, string> Version,
        Func<TRequest, TemplateErrors> ValidateRequest,
        Func<WorldDbContext, TRow, TemplateErrors, CancellationToken, Task> ValidateRow)
        where TRow : class;

    private async Task<TemplateEditResult<TRow>> RunAsync<TRow, TRequest>(
        Kind<TRow, TRequest> kind, TemplateEditCaller caller, ulong id, string ifMatch, TRequest request, CancellationToken ct)
        where TRow : class
    {
        TemplateEditResult<TRow> result;
        try
        {
            result = await SaveAsync(kind, caller, id, ifMatch, request, ct);
        }
        catch (Exception ex) when (TemplateDbErrors.Postgres(ex) is { SqlState: PostgresErrorCodes.SerializationFailure })
        {
            // The losing side of two concurrent edits, at a read, at the save or at the commit (the transaction rolled
            // back, so nothing was written): a 409 the caller retries, worded apart from a stale version. Two saves of
            // one version land here too; the retry then answers the stale version.
            logger.LogInformation(ex, "A {Kind} template save {TemplateId} lost to a concurrent edit", kind.Name, id);
            return new(TemplateEditOutcome.ConcurrentSave);
        }

        return result.Outcome == TemplateEditOutcome.Saved
            ? result with { Reload = await RequestReloadAsync(kind.Name, caller.World, kind.Area, ct) }
            : result;
    }

    /// <summary>The edit's transaction, and its audit line once committed; a serialization failure is thrown.</summary>
    private async Task<TemplateEditResult<TRow>> SaveAsync<TRow, TRequest>(
        Kind<TRow, TRequest> kind, TemplateEditCaller caller, ulong id, string ifMatch, TRequest request, CancellationToken ct)
        where TRow : class
    {
        List<TemplateChange> changes;
        TRow row;
        await using (WorldDbContext db = await contexts.CreateDbContextAsync(ct))
        {
            // Serializable: the edit commits only if it would have, run alone. Two saves of the same version cannot both
            // commit, and neither can two edits that each check a row the other changes (#1179): a unique pointed at a
            // base while that base's kind or level changes, each judged against the other's old row. In Postgres the
            // loser fails with a serialization failure (RunAsync answers it as a concurrent save). Two edits of unrelated
            // item templates can collide too, as the item checks read the whole table.
            await using IDbContextTransaction transaction =
                await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            TRow? found = await kind.Find(db, ct);
            if (found is null)
                return new(TemplateEditOutcome.NotFound);
            row = found;

            if (!TemplateIfMatch.Matches(ifMatch, kind.Version(row)))
                return new(TemplateEditOutcome.Conflict);

            TemplateErrors errors = kind.ValidateRequest(request);
            if (errors.Any)
                return Invalid<TRow>(errors);

            Dictionary<string, string?> before = Snapshot(kind.Fields, row);
            foreach (TemplateField<TRow, TRequest> field in kind.Fields)
                field.Set(row, request);

            await kind.ValidateRow(db, row, errors, ct);
            if (errors.Any)
                return Invalid<TRow>(errors); // Nothing was saved; the transaction rolls back on disposal.

            changes = Changes(kind.Fields, before, Snapshot(kind.Fields, row));

            if (await CommitAsync<TRow>(db, transaction, kind.Name, id, changes.Count > 0, errors, ct) is { } refused)
                return refused;
        }

        // One line per save, only the fields that changed. A save that changed nothing wrote nothing and says nothing.
        if (changes.Count > 0)
        {
            logger.LogInformation(
                "Template saved: {Kind} {TemplateId} on world {WorldId} by account {AccountId}, changed {@Changes}",
                kind.Name, id, caller.World.Value, caller.Account.Value, changes);
        }

        return new(TemplateEditOutcome.Saved, row);
    }

    /// <summary>The fields whose stored value the request changed, as the audit line names them.</summary>
    private static List<TemplateChange> Changes<TRow, TRequest>(
        IReadOnlyList<TemplateField<TRow, TRequest>> fields, Dictionary<string, string?> before,
        Dictionary<string, string?> after) =>
        fields
            .Where(f => !string.Equals(before[f.Name], after[f.Name], StringComparison.Ordinal))
            .Select(f => new TemplateChange(f.Name, before[f.Name], after[f.Name]))
            .ToList();

    /// <summary>
    /// Saves (only when something changed) and commits. Null once committed; otherwise the answer: a check constraint's
    /// or a foreign key's refusal as a field error. Any other failure, a serialization failure among them, is thrown.
    /// </summary>
    private async Task<TemplateEditResult<TRow>?> CommitAsync<TRow>(
        WorldDbContext db, IDbContextTransaction transaction, string kind, ulong id, bool changed, TemplateErrors errors,
        CancellationToken ct)
        where TRow : class
    {
        try
        {
            if (changed)
                await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return null;
        }
        catch (Exception ex) when (TemplateDbErrors.Postgres(ex) is { } pg)
        {
            if (pg.SqlState == PostgresErrorCodes.CheckViolation)
            {
                logger.LogWarning(ex, "A {Kind} template save {TemplateId} was refused by a check constraint", kind, id);
                errors.Add(TemplateDbErrors.FieldOf(pg), $"The database refused this value ({pg.ConstraintName}).");
                return Invalid<TRow>(errors);
            }

            // A foreign key the row checks missed (a row deleted between the check and the save): the value names a
            // row that is not there, which is the caller's to fix, not an outage.
            if (pg.SqlState == PostgresErrorCodes.ForeignKeyViolation)
            {
                logger.LogWarning(ex, "A {Kind} template save {TemplateId} was refused by a foreign key", kind, id);
                errors.Add(TemplateDbErrors.FieldOf(pg), $"The database refused this value ({pg.ConstraintName}).");
                return Invalid<TRow>(errors);
            }

            throw;
        }
    }

    private async Task<TemplateReloadResult> RequestReloadAsync(
        string kind, WorldId world, TemplateReloadArea area, CancellationToken ct)
    {
        try
        {
            return await reload.RequestAsync(world, area, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The save is committed; the world just was not asked. Fixed wording: the cause stays in the log.
            logger.LogWarning(ex, "The reload request for {Kind} templates on world {WorldId} could not be sent",
                kind, world.Value);
            return new TemplateReloadResult(TemplateReloadResult.Failed, "The world could not be asked to reload.");
        }
    }

    private static TemplateEditResult<TRow> Invalid<TRow>(TemplateErrors errors) where TRow : class =>
        new(TemplateEditOutcome.Invalid, Errors: errors.ToDictionary());

    private static Dictionary<string, string?> Snapshot<TRow, TRequest>(
        IReadOnlyList<TemplateField<TRow, TRequest>> fields, TRow row) =>
        fields.ToDictionary(f => f.Name, f => Display(f.Get(row)), StringComparer.Ordinal);

    /// <summary>A value as the audit line shows it: enums by name, lists comma-joined, numbers invariant.</summary>
    internal static string? Display(object? value) => value switch
    {
        null => null,
        string s => s,
        System.Collections.IEnumerable list => string.Join(",", list.Cast<object?>().Select(Display)),
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };
}

/// <summary>Reads the Postgres error out of a failed save, for the template save only.</summary>
public static class TemplateDbErrors
{
    /// <summary>
    /// The first <see cref="PostgresException" /> in <paramref name="ex" />'s chain of inner exceptions, or null: Npgsql's
    /// execution strategy wraps a transient failure (a serialization failure among them) in an
    /// InvalidOperationException around the DbUpdateException or the query's own exception.
    /// </summary>
    public static PostgresException? Postgres(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres)
                return postgres;
        }

        return null;
    }

    /// <summary>
    /// The request field a constraint is about: <c>CK_AbilityTemplates_ThreatMultiplier_NonNegative</c> is
    /// <c>threatMultiplier</c>, and the foreign key <c>FK_AbilityTemplates_AuraTemplates_AuraId</c> is <c>auraId</c>. A
    /// constraint named some other way is <c>template</c>.
    /// </summary>
    public static string FieldOf(PostgresException error)
    {
        string[] parts = (error.ConstraintName ?? "").Split('_');
        string? column = parts switch
        {
            ["CK", _, { Length: > 0 } c, ..] => c,
            ["FK", _, _, { Length: > 0 } c] => c,
            _ => null,
        };
        return column is null ? "template" : char.ToLowerInvariant(column[0]) + column[1..];
    }
}
