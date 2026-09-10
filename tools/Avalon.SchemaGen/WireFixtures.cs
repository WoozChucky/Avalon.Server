using System.Globalization;
using System.Reflection;
using ProtoBuf;

namespace Avalon.SchemaGen;

/// <summary>
/// The shapes a fixture is built in. Each one exists to make a different encoding decision
/// observable; a message populated only with ordinary values would agree with a reader that
/// gets presence, widths and signs all wrong.
/// </summary>
public enum FixtureVariant
{
    /// <summary>Nothing set. Members that declare an initializer are overridden back to null.</summary>
    Absent,

    /// <summary>
    /// Everything present and carrying nothing: empty strings, empty byte arrays, zero
    /// scalars, nullable members set to zero, submessages present with no members inside.
    /// This is where absent and empty part company, and the only shape that shows it.
    /// </summary>
    Empty,

    /// <summary>
    /// Ordinary values, drawn from a ladder per type by a stable hash of the member's path so
    /// that a message's members do not all carry one value. Not distinct: a ladder is shorter
    /// than the number of members drawing from it, and the path names the member's own message
    /// rather than the chain above it, so one submessage type reached through two different
    /// parents is filled the same way in both.
    /// </summary>
    Populated,

    /// <summary>The largest value each member's type can carry.</summary>
    Maxima,

    /// <summary>The smallest, which for the signed types is where sign extension shows.</summary>
    Minima,

    /// <summary>
    /// Values astride a varint width boundary, and lengths astride the boundary in a
    /// length prefix, where an off-by-one in a reader's decode loop first appears.
    /// </summary>
    VarintEdges,

    /// <summary>
    /// NaN, both infinities, a denormal and negative zero, for the messages that carry a
    /// float somewhere. Emitted only for those, so the other files do not repeat a variant
    /// identical to Populated.
    /// </summary>
    FloatSpecials,
}

/// <summary>
/// One <c>[ProtoMember]</c>, resolved once so the fixture builder and the corpus writer
/// agree on what a message's members are and in what order.
/// </summary>
public sealed class WireMember
{
    private readonly PropertyInfo _property;

    internal WireMember(PropertyInfo property, int tag)
    {
        _property = property;
        Tag = tag;
    }

    public int Tag { get; }

    public string Name => _property.Name;

    public Type DeclaredType => _property.PropertyType;

    public object? Read(object instance) => _property.GetValue(instance);

    public void Write(object instance, object? value) => _property.SetValue(instance, value);
}

/// <summary>
/// Builds a filled instance of any packet contract by reflection.
/// </summary>
/// <remarks>
/// Reflection rather than ninety-three hand-written fixtures, because a hand-written one
/// does not gain a member when the contract does. The values are chosen from a ladder per
/// type and indexed by a hash of the member's path, so they vary between members, stay the
/// same between runs and machines, and change only when a contract does.
/// </remarks>
public static class WireFixtures
{
    public static IReadOnlyList<WireMember> Members(Type contract)
    {
        const BindingFlags instanceMembers =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        RefuseMembersThisWalkCannotReach(contract, instanceMembers);

        return contract.GetProperties(instanceMembers)
            .Select(property => (property, tag: property.GetCustomAttribute<ProtoMemberAttribute>()))
            .Where(candidate => candidate.tag is not null)
            .Select(candidate => new WireMember(candidate.property, candidate.tag!.Tag))
            .OrderBy(member => member.Tag)
            .ToList();
    }

    /// <summary>
    /// Stops on a <c>[ProtoMember]</c> this walk does not see. protobuf-net accepts the
    /// attribute on a field as well as on a property, and only properties are read here.
    /// </summary>
    /// <remarks>
    /// Every member in the protocol today is an auto-property, so nothing is missing. One
    /// declared on a field later would be absent from the fixtures, from the corpus, from the
    /// presence marking and from the value comparison, and each of those would keep passing -
    /// the member would simply not exist as far as any of them could tell. A shape this tool
    /// cannot reach therefore stops it, which is a build someone has to fix rather than
    /// coverage that quietly is not there.
    /// </remarks>
    private static void RefuseMembersThisWalkCannotReach(Type contract, BindingFlags instanceMembers)
    {
        string[] fields = contract.GetFields(instanceMembers)
            .Where(field => field.GetCustomAttribute<ProtoMemberAttribute>() is not null)
            .Select(field => field.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        if (fields.Length > 0)
        {
            throw new NotSupportedException(
                $"{contract.FullName} carries [ProtoMember] on a field: {string.Join(", ", fields)}. "
                + "The fixtures read properties only, so these members would be missing from the corpus "
                + "with nothing failing. Teach WireMember to read and write a field before declaring one.");
        }
    }

    /// <summary>
    /// The variants worth emitting for a contract. All but one apply to everything;
    /// FloatSpecials would otherwise duplicate Populated for the messages with no float
    /// anywhere beneath them.
    /// </summary>
    public static IReadOnlyList<FixtureVariant> VariantsFor(Type contract)
    {
        List<FixtureVariant> variants =
        [
            FixtureVariant.Absent,
            FixtureVariant.Empty,
            FixtureVariant.Populated,
            FixtureVariant.Maxima,
            FixtureVariant.Minima,
            FixtureVariant.VarintEdges,
        ];

        if (CarriesFloat(contract, []))
        {
            variants.Add(FixtureVariant.FloatSpecials);
        }

        return variants;
    }

    public static string NameOf(FixtureVariant variant) => variant switch
    {
        FixtureVariant.Absent => "absent",
        FixtureVariant.Empty => "empty",
        FixtureVariant.Populated => "populated",
        FixtureVariant.Maxima => "maxima",
        FixtureVariant.Minima => "minima",
        FixtureVariant.VarintEdges => "varint-edges",
        FixtureVariant.FloatSpecials => "float-specials",
        _ => throw new ArgumentOutOfRangeException(nameof(variant)),
    };

    public static object Build(Type contract, FixtureVariant variant) =>
        Build(contract, variant, ordinal: 0, enclosing: []);

    /// <summary>
    /// The ordinal travels down into nested messages so that two elements of a repeated
    /// message field differ from each other. Identical elements would still catch a reader
    /// that dropped one, but not one that returned them in the wrong order.
    /// </summary>
    /// <remarks>
    /// The chain of messages already open travels down with it, so that a contract reached
    /// through itself is refused rather than recursed into.
    /// </remarks>
    internal static object Build(Type contract, FixtureVariant variant, int ordinal, List<Type> enclosing)
    {
        RefuseAContractThatContainsItself(contract, enclosing);

        object instance = Activator.CreateInstance(contract)
            ?? throw new InvalidOperationException($"{contract.FullName} has no parameterless constructor.");

        string message = WireSchema.SchemaNameOf(contract);
        enclosing.Add(contract);

        try
        {
            foreach (WireMember member in Members(contract))
            {
                member.Write(
                    instance,
                    FixtureValues.For(member.DeclaredType, variant, $"{message}.{member.Name}", ordinal, enclosing));
            }
        }
        finally
        {
            enclosing.RemoveAt(enclosing.Count - 1);
        }

        return instance;
    }

    /// <summary>
    /// Stops on a contract reached through itself, directly or by way of another.
    /// </summary>
    /// <remarks>
    /// Every member of a fixture is filled, so building one for such a shape would descend
    /// until the process died of a stack overflow - which .NET does not allow anything to
    /// catch, so there would be no message, no failing test and no exit code worth reading,
    /// only a runner that disappeared. Nothing in the protocol is shaped this way today.
    /// </remarks>
    private static void RefuseAContractThatContainsItself(Type contract, List<Type> enclosing)
    {
        if (!enclosing.Contains(contract))
        {
            return;
        }

        IEnumerable<string> chain = enclosing
            .SkipWhile(open => open != contract)
            .Append(contract)
            .Select(WireSchema.SchemaNameOf);

        throw new NotSupportedException(
            "A fixture cannot be built for a contract that contains itself, because every member is "
            + "filled and the descent would not end: " + string.Join(" -> ", chain) + ".");
    }

    /// <summary>
    /// The element type of a repeated member, or null when the member is not repeated.
    /// <c>byte[]</c> is not repeated: protobuf carries it as one length-delimited field.
    /// </summary>
    public static Type? RepeatedElementType(Type declared) => FixtureValues.ElementTypeOf(declared);

    /// <summary>Whether the type is itself a message rather than a scalar.</summary>
    public static bool IsContract(Type type) => FixtureValues.IsContract(type);

    /// <summary>
    /// The type a member's values actually are: the element type where it is repeated, and
    /// the underlying type where that is nullable.
    /// </summary>
    /// <remarks>
    /// A member declared <c>float?</c> carries floats, and anything matching on the declared
    /// type alone would not say so - which for the float specials means a member silently
    /// outside the index space they are handed out by.
    /// </remarks>
    public static Type ValueTypeOf(Type declared)
    {
        Type carried = FixtureValues.ElementTypeOf(declared) ?? declared;

        return Nullable.GetUnderlyingType(carried) ?? carried;
    }

    /// <summary>
    /// The float values the fixtures carry that a comparison going through a decimal or a
    /// text form is liable to lose. Exposed so a test can hold the corpus to containing all
    /// of them rather than to containing whichever ones it happened to generate.
    /// </summary>
    public static IReadOnlyList<float> FloatEdgeCases() => FixtureFloats.Specials;

    /// <summary>
    /// Every float member in the protocol, as <c>Message.Member</c>, ordered and de-duplicated.
    /// Its only purpose is to be a stable index space: the float specials are handed out by
    /// position in this list, so each one is guaranteed to appear in the corpus.
    /// </summary>
    public static IReadOnlyList<string> FloatMemberPaths() => _floatMemberPaths.Value;

    private static readonly Lazy<IReadOnlyList<string>> _floatMemberPaths = new(() =>
        WireSchema.ContractTypes()
            .SelectMany(contract => Members(contract)
                .Where(member => ValueTypeOf(member.DeclaredType) == typeof(float))
                .Select(member => $"{WireSchema.SchemaNameOf(contract)}.{member.Name}"))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList());

    private static bool CarriesFloat(Type contract, HashSet<Type> seen)
    {
        if (!seen.Add(contract))
        {
            return false;
        }

        foreach (WireMember member in Members(contract))
        {
            Type element = ValueTypeOf(member.DeclaredType);

            if (element == typeof(float))
            {
                return true;
            }

            if (FixtureValues.IsContract(element) && CarriesFloat(element, seen))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// The value ladders, one per member type, and the rules for picking from them.
/// </summary>
internal static class FixtureValues
{
    internal static bool IsContract(Type type) =>
        type.GetCustomAttribute<ProtoContractAttribute>() is not null;

    /// <summary>
    /// The element type of a repeated member, or null when the member is not repeated.
    /// <c>byte[]</c> is deliberately excluded: protobuf carries it as a single length-delimited
    /// field, not as a repeated one.
    /// </summary>
    internal static Type? ElementTypeOf(Type type)
    {
        if (type == typeof(byte[]) || type == typeof(string))
        {
            return null;
        }

        if (type.IsArray)
        {
            return type.GetElementType();
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            return type.GetGenericArguments()[0];
        }

        return null;
    }

    internal static object? For(
        Type declared,
        FixtureVariant variant,
        string path,
        int ordinal,
        List<Type> enclosing)
    {
        if (Nullable.GetUnderlyingType(declared) is { } underlying)
        {
            // Zero rather than null under Empty: a nullable member set to its type default is
            // the case a reader without explicit presence re-encodes as absent.
            return variant == FixtureVariant.Absent ? null : For(underlying, variant, path, ordinal, enclosing);
        }

        if (ElementTypeOf(declared) is { } element)
        {
            return Repeated(declared, element, variant, path, enclosing);
        }

        if (IsContract(declared))
        {
            return variant switch
            {
                FixtureVariant.Absent => null,

                // A submessage present with nothing in it, which is two bytes on the wire and
                // indistinguishable from absent to anything that only reads values back.
                FixtureVariant.Empty => WireFixtures.Build(declared, FixtureVariant.Absent, ordinal, enclosing),

                _ => WireFixtures.Build(declared, variant, ordinal, enclosing),
            };
        }

        return Scalar(declared, variant, path, ordinal);
    }

    private static object? Repeated(
        Type declared,
        Type element,
        FixtureVariant variant,
        string path,
        List<Type> enclosing)
    {
        if (variant == FixtureVariant.Absent)
        {
            return null;
        }

        int count = variant switch
        {
            FixtureVariant.Empty => 0,
            FixtureVariant.Minima => 1,
            FixtureVariant.Maxima => 3,
            _ => 2,
        };

        // An element's variant is the message's, except that Empty means "the collection is
        // present and holds nothing", so there are no elements to give a variant to.
        Array items = Array.CreateInstance(element, count);
        for (int i = 0; i < count; i++)
        {
            items.SetValue(For(element, variant, path, i, enclosing), i);
        }

        if (declared.IsArray)
        {
            return items;
        }

        object list = Activator.CreateInstance(declared)!;
        MethodInfo add = declared.GetMethod("Add")!;
        foreach (object? item in items)
        {
            add.Invoke(list, [item]);
        }

        return list;
    }

    private static object? Scalar(Type declared, FixtureVariant variant, string path, int ordinal)
    {
        if (declared.IsEnum)
        {
            return FixtureEnums.Value(declared, variant, path, ordinal);
        }

        if (declared == typeof(string))
        {
            return FixtureText.Value(variant, path, ordinal);
        }

        if (declared == typeof(byte[]))
        {
            return FixtureBinary.Value(variant, path, ordinal);
        }

        if (declared == typeof(ReadOnlyMemory<byte>))
        {
            byte[]? bytes = FixtureBinary.Value(variant, path, ordinal);
            return bytes is null ? default(ReadOnlyMemory<byte>) : new ReadOnlyMemory<byte>(bytes);
        }

        if (declared == typeof(DateTime))
        {
            return FixtureBcl.Moment(variant, path, ordinal);
        }

        if (declared == typeof(Guid))
        {
            return FixtureBcl.Identifier(variant, path, ordinal);
        }

        return FixtureNumbers.Value(declared, variant, path, ordinal);
    }

    /// <summary>
    /// A stable index into a ladder. FNV-1a over the member's path rather than
    /// <see cref="string.GetHashCode()"/>, which is randomized per process and would make the
    /// corpus differ between two runs of the same code.
    /// </summary>
    internal static int Pick(string path, int ordinal, int count)
    {
        uint hash = 2166136261;

        foreach (char c in path)
        {
            hash = (hash ^ c) * 16777619;
        }

        hash = (hash ^ (uint)ordinal) * 16777619;

        return (int)(hash % (uint)count);
    }

    internal static CultureInfo Invariant => CultureInfo.InvariantCulture;
}
