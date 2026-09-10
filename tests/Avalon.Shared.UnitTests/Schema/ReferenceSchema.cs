extern alias wire;

using System;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf.Reflection;

namespace Avalon.Shared.UnitTests.Schema;

/// <summary>
/// The checked-in schema as a standard protobuf implementation understands it.
/// </summary>
/// <remarks>
/// <c>Avalon.Wire.Reference</c> compiles <c>schema/avalon.proto</c> with <c>protoc</c> at build
/// time, so what these tests compare against is a reader generated from the schema rather than
/// a second reading of the same C# attributes. That is the whole point: protobuf-net agreeing
/// with itself proves nothing about a client.
///
/// Nothing here names a generated type beyond the one line that reaches the file descriptor.
/// Everything downstream goes through reflection, so a message added to the protocol needs no
/// change on this side.
/// </remarks>
internal static class ReferenceSchema
{
    internal static FileDescriptor File => wire::Avalon.AvalonReflection.Descriptor;

    internal static IEnumerable<MessageDescriptor> Messages => File.MessageTypes;

    internal static MessageDescriptor For(string name) =>
        File.MessageTypes.SingleOrDefault(message => string.Equals(message.Name, name, StringComparison.Ordinal))
        ?? throw new InvalidOperationException(
            $"schema/avalon.proto declares no message named {name}. Re-export the schema: " +
            "dotnet run --project tools/Avalon.SchemaGen");
}
