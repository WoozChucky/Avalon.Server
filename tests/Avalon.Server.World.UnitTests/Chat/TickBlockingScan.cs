using System.Reflection;
using System.Runtime.CompilerServices;

namespace Avalon.Server.World.UnitTests.Chat;

/// <summary>
/// Finds, in a type and every type nested in it (closures and lambdas compile into nested types), an
/// async method or a call that blocks on a task. A command runs on the tick; either would stall it.
/// </summary>
internal static class TickBlockingScan
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                          BindingFlags.Static | BindingFlags.DeclaredOnly;

    public static IEnumerable<string> Violations(Type command)
    {
        foreach (Type type in WithNested(command))
        {
            IEnumerable<MethodBase> methods = type.GetMethods(Declared).Cast<MethodBase>()
                .Concat(type.GetConstructors(Declared));

            foreach (MethodBase method in methods)
            {
                if (method.GetCustomAttribute<AsyncStateMachineAttribute>() is not null)
                    yield return $"{command.Name}: {type.Name}.{method.Name} is async";

                foreach (MethodBase called in Calls(method))
                {
                    if (IsBlocking(called))
                        yield return $"{command.Name}: {type.Name}.{method.Name} calls {called.DeclaringType?.Name}.{called.Name}";
                }
            }
        }
    }

    private static IEnumerable<Type> WithNested(Type type)
    {
        yield return type;
        foreach (Type nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            foreach (Type inner in WithNested(nested))
                yield return inner;
    }

    /// <summary>Every method a call or callvirt in the body names. A byte scan: a token that does not resolve is skipped.</summary>
    private static IEnumerable<MethodBase> Calls(MethodBase method)
    {
        byte[]? il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null)
            yield break;

        Type[]? typeArgs = method.DeclaringType is { IsGenericType: true } d ? d.GetGenericArguments() : null;
        Type[]? methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;

        for (int i = 0; i + 4 < il.Length; i++)
        {
            if (il[i] is not (0x28 or 0x6F)) // call, callvirt
                continue;

            int token = BitConverter.ToInt32(il, i + 1);
            MethodBase? called;
            try
            {
                called = method.Module.ResolveMethod(token, typeArgs, methodArgs);
            }
            catch (Exception)
            {
                continue;
            }

            if (called is not null)
                yield return called;
        }
    }

    private static bool IsBlocking(MethodBase called)
    {
        if (called.DeclaringType is not { } declaring)
            return false;

        Type type = declaring.IsGenericType ? declaring.GetGenericTypeDefinition() : declaring;

        return (type == typeof(Task<>) && called.Name == "get_Result")
               || (type == typeof(ValueTask<>) && called.Name == "get_Result")
               || (type == typeof(Task) && called.Name is "Wait" or "WaitAll" or "WaitAny")
               || (called.Name == "GetResult" && (type == typeof(TaskAwaiter) || type == typeof(TaskAwaiter<>)
                                                  || type == typeof(ValueTaskAwaiter) || type == typeof(ValueTaskAwaiter<>)
                                                  || type == typeof(ConfiguredTaskAwaitable.ConfiguredTaskAwaiter)
                                                  || type == typeof(ConfiguredTaskAwaitable<>.ConfiguredTaskAwaiter)));
    }
}
