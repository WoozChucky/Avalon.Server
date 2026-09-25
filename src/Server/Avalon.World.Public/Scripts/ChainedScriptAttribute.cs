namespace Avalon.World.Public.Scripts;

/// <summary>
/// Marks an <see cref="AiScript"/> as a component that another script constructs and chains, rather
/// than one a creature template can name in <c>ScriptName</c>. The script loader skips these, so a
/// seed row naming one reports "not found" instead of resolving to a type that then fails to
/// construct.
/// </summary>
/// <remarks>
/// Placement builds a named script with exactly <c>(creature, instance)</c> as runtime arguments.
/// A component usually needs more than that — the range detector takes its parent's aggro range —
/// which is exactly why it must not be nameable.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ChainedScriptAttribute : Attribute;
