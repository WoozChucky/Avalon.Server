using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Avalon.Shared.UnitTests.Schema;

/// <summary>
/// Holds the vendored <c>bcl.proto</c> to the protobuf-net the server serializes with.
/// </summary>
/// <remarks>
/// The file is a copy of one protobuf-net does not ship, in the package or anywhere else a
/// build can reach, so nothing compares it to its original. An upgrade that changed it upstream
/// would leave a copy here describing the previous library, and a client generated from it
/// would decode timestamps and guids by rules the server no longer writes -- plausible values,
/// no error. Reading the two files against each other is a person's job; this makes the upgrade
/// the moment that job is asked for, rather than letting it pass unnoticed.
/// </remarks>
public class VendoredBclSchemaShould
{
    private const string NoticePath = "schema/protobuf-net/NOTICE";
    private const string VersionsPath = "src/Directory.Packages.props";
    private const string PackageName = "protobuf-net";

    [Fact]
    public void Record_The_Library_Version_Its_Copy_Was_Taken_From()
    {
        string recorded = RecordedVersion();
        string referenced = ReferencedVersion();

        Assert.True(
            string.Equals(recorded, referenced, StringComparison.Ordinal),
            string.Join(Environment.NewLine,
                $"{NoticePath} says bcl.proto was vendored from {PackageName} {recorded}, but",
                $"{VersionsPath} now references {referenced}. The copy may no longer match the",
                "library, and nothing else will say so.",
                string.Empty,
                $"Compare schema/protobuf-net/bcl.proto against src/Tools/bcl.proto in the {PackageName}",
                "repository, at the commit that release was built from, and take the new copy if it",
                $"changed. Then update both lines at the end of {NoticePath}:",
                string.Empty,
                $"    Vendored-From: {PackageName} {referenced}",
                "    Upstream-Commit: <the commit that release was built from>"));
    }

    /// <summary>
    /// The version the notice beside the vendored file records it was taken from.
    /// </summary>
    private static string RecordedVersion()
    {
        string path = Path.Combine(RepositoryLayout.Root(), NoticePath);

        Assert.True(File.Exists(path), $"{NoticePath} is missing; it is what records where bcl.proto came from.");

        Match recorded = Regex.Match(
            File.ReadAllText(path),
            @"^Vendored-From:\s*" + Regex.Escape(PackageName) + @"\s+(?<version>\S+)\s*$",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);

        Assert.True(
            recorded.Success,
            $"{NoticePath} has no \"Vendored-From: {PackageName} <version>\" line, so there is nothing "
            + "to hold the vendored copy to.");

        return recorded.Groups["version"].Value;
    }

    /// <summary>
    /// The version the solution actually builds against, from its one declaration.
    /// </summary>
    private static string ReferencedVersion()
    {
        string path = Path.Combine(RepositoryLayout.Root(), VersionsPath);

        string? referenced = XDocument.Load(path)
            .Descendants("PackageVersion")
            .Where(element => string.Equals(
                (string?)element.Attribute("Include"), PackageName, StringComparison.Ordinal))
            .Select(element => (string?)element.Attribute("Version"))
            .FirstOrDefault();

        Assert.True(
            referenced is not null,
            $"{VersionsPath} declares no version for {PackageName}, which the vendored schema is pinned to.");

        return referenced!;
    }
}
