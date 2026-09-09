using AudiobookManager.Api.Async;
using AudiobookManager.Api.Controllers;
using AudiobookManager.Services;
using System.Reflection;

namespace AudiobookManager.Test.Api;

/// <summary>
/// Guards the cross-language constant mirrors in the frontend against the backend source of
/// truth:
///
/// - <c>SignalREvents</c> / <c>OperationKeys</c> in <c>client/src/constants/signalrEvents.ts</c>
///   must match the <c>IOrganize</c> interface method names (the wire names the SignalR hub
///   publishes under) and the operation-key constants on the API controllers exactly, in both
///   directions: a backend event with no client entry and a client entry with no backend event
///   are both drift that must fail here. The operation-key side is discovered by reflection over
///   the <c>AudiobookManager.Api</c> assembly (see <see cref="DiscoverControllerOperationKeys"/>)
///   rather than enumerated by hand, so a newly added controller const cannot silently evade the
///   test; the hand-written expected list still documents each key explicitly, and both the
///   reflected set and the TS file are compared against it.
/// - The cover caps in <c>client/src/lib/coverImage.ts</c> must agree with
///   <c>CoverImageProcessor</c>. The two layers deliberately enforce the caps independently
///   rather than sharing a value, so this test is what keeps them from diverging.
///
/// The frontend files are located by walking up from the test working directory
/// (<c>AppContext.BaseDirectory</c>) to the filesystem root - with no fixed depth cap - until
/// the file is found or the root is reached. The repository checkout (a directory containing
/// both <c>AudiobookManager/</c> and <c>client/</c>) is what the file must sit under, mirroring
/// how CI checkouts are laid out; the failure message reports whether that layout was ever seen
/// on the way up.
/// </summary>
[TestClass]
public class SignalREventParityTests
{
    private static readonly string ClientEventConstantsFile =
        Path.Combine("client", "src", "constants", "signalrEvents.ts");
    private static readonly string ClientCoverImageFile =
        Path.Combine("client", "src", "lib", "coverImage.ts");

    private sealed record ObjectEntry(string Key, string Value);

    private sealed record OperationKeyRef(string DeclaringType, string FieldName, string Value);

    [TestMethod]
    public void SignalREvents_MatchTheIOrganizeInterfaceExactlyBothDirections()
    {
        var interfaceMethodNames = new HashSet<string>();
        foreach (var method in typeof(IOrganize).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            interfaceMethodNames.Add(method.Name);
        }

        var entries = ExtractObjectEntries(ReadTextFile(FindClientFile(ClientEventConstantsFile)), "SignalREvents");
        var clientKeys = entries.Select(e => e.Key).ToHashSet();
        var clientValues = entries.Select(e => e.Value).ToHashSet();

        var missingInClient = interfaceMethodNames
            .Where(m => !clientValues.Contains(m))
            .OrderBy(x => x)
            .ToList();
        var missingInBackend = clientValues
            .Where(v => !interfaceMethodNames.Contains(v))
            .OrderBy(x => x)
            .ToList();

        Assert.IsTrue(
            missingInClient.Count == 0,
            $"SignalR event names the backend IOrganize publishes but {ClientEventConstantsFile} does not: {Join(missingInClient)}");
        Assert.IsTrue(
            missingInBackend.Count == 0,
            $"SignalR event names in {ClientEventConstantsFile} that the backend IOrganize does not publish: {Join(missingInBackend)}");
        Assert.IsTrue(
            clientKeys.Count == clientValues.Count,
            $"SignalREvents in {ClientEventConstantsFile} has {clientKeys.Count} keys but {clientValues.Count} values - an entry must have failed to parse, or two entries share one key.");
    }

    [TestMethod]
    public void OperationKeys_MatchTheControllerOperationKeyConstantsExactlyBothDirections()
    {
        var reflected = DiscoverControllerOperationKeys();
        var reflectedValues = reflected.Select(x => x.Value).ToHashSet();

        // The documented, itemized set of operation keys this test expects. It exists so the
        // expected set stays explicit and reviewed even though the actual discovery below is
        // reflection-based; a newly added controller const shows up in the reflection first and
        // fails the "noted in this list" assertion until it is added here AND in the TS file.
        var documentedExpectedKeys = new List<string> {
            ConsistencyController.OperationKey,
            ConsistencyController.ResolveOperationKey,
            ConsistencyController.CheckSelectedOperationKey,
            SimilarValuesController.OperationKey,
            LibraryController.ScanOperationKey,
            LibraryController.BulkImportOperationKey,
            SeriesController.MatchOperationKey,
            SeriesController.RefreshOperationKey,
            MetadataRefreshController.BulkOperationKey,
            MissingTagsController.LanguageBackfillOperationKey,
            AudiobookController.BulkEditOperationKey,
        };

        var entries = ExtractObjectEntries(ReadTextFile(FindClientFile(ClientEventConstantsFile)), "OperationKeys");
        var clientValues = entries.Select(e => e.Value).ToHashSet();

        // (a) Every const the reflection finds must be documented here and mirrored in the TS
        // file - a controller const with no client entry fails these two assertions.
        var unDocumented = reflected
            .Where(r => !documentedExpectedKeys.Contains(r.Value))
            .OrderBy(x => x.Value)
            .ToList();
        Assert.IsTrue(
            unDocumented.Count == 0,
            $"Operation keys found by reflection on the Api controllers that this test's expected list does not enumerate (add them to the list and to {ClientEventConstantsFile}): " +
            Join(unDocumented.Select(x => $"{x.DeclaringType}.{x.FieldName} = {x.Value}")));

        var missingInClient = reflectedValues
            .Where(v => !clientValues.Contains(v))
            .OrderBy(x => x)
            .ToList();
        Assert.IsTrue(
            missingInClient.Count == 0,
            $"Operation keys the backend controllers publish but {ClientEventConstantsFile} does not: {Join(missingInClient)}");

        // (b) Neither the documented list nor the TS file may hold a key no backend const
        // carries - a stale entry with no backend counterpart fails these two assertions.
        var missingByReflection = documentedExpectedKeys
            .Where(k => !reflectedValues.Contains(k))
            .OrderBy(x => x)
            .ToList();
        Assert.IsTrue(
            missingByReflection.Count == 0,
            $"Operation keys this test's expected list documents but no Api controller const carries anymore (remove them from the list): {Join(missingByReflection)}");

        var missingInBackend = clientValues
            .Where(v => !reflectedValues.Contains(v))
            .OrderBy(x => x)
            .ToList();
        Assert.IsTrue(
            missingInBackend.Count == 0,
            $"Operation keys in {ClientEventConstantsFile} that no backend controller publishes: {Join(missingInBackend)}");
    }

    [TestMethod]
    public void CoverImageCaps_MatchTheCoverImageProcessorConstants()
    {
        var coverImageSource = ReadTextFile(FindClientFile(ClientCoverImageFile));

        var clientMaxDimension = EvaluateIntLiteral(ExtractConstantExpression(coverImageSource, "COVER_MAX_DIMENSION"));
        var clientMaxBytes = EvaluateIntLiteral(ExtractConstantExpression(coverImageSource, "COVER_MAX_BYTES"));

        Assert.AreEqual(
            CoverImageProcessor.MaxDimension,
            clientMaxDimension,
            "The longest-edge cover cap must agree between the layers (CoverImageProcessor.MaxDimension vs client/src/lib/coverImage.ts COVER_MAX_DIMENSION) - they are enforced independently and this test keeps them from drifting.");
        Assert.AreEqual(
            CoverImageProcessor.MaxStoredBytes,
            clientMaxBytes,
            "The stored-bytes cover cap must agree between the layers (CoverImageProcessor.MaxStoredBytes vs client/src/lib/coverImage.ts COVER_MAX_BYTES) - they are enforced independently and this test keeps them from drifting.");
    }

    /// <summary>
    /// Reflects over every type in the <c>AudiobookManager.Api</c> assembly and collects each
    /// operation-key constant. Controllers declare these as
    /// <c>public const string XxxOperationKey = "kebab-case-key"</c> (some, like
    /// <c>SimilarValuesController.OperationKey</c>, are named exactly <c>OperationKey</c>), so
    /// the filter is: a public, static, compile-time-literal string field whose name ends with
    /// <c>OperationKey</c>. The value must make sense as a URL path segment
    /// (<c>/api/operations/{key}/status</c>), which is why it is asserted to be kebab-case.
    /// </summary>
    private static List<OperationKeyRef> DiscoverControllerOperationKeys()
    {
        var discovered = new List<OperationKeyRef>();
        foreach (var type in typeof(ConsistencyController).Assembly.GetTypes())
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType != typeof(string) ||
                    !field.IsLiteral ||
                    !field.Name.EndsWith("OperationKey"))
                {
                    continue;
                }

                var value = (string)field.GetValue(null)!;
                Assert.IsTrue(
                    IsKebabCase(value),
                    $"Operation key {type.Name}.{field.Name} = '{value}' is not kebab-case. These values travel in " +
                    "URL path segments (GET /api/operations/{key}/status), so they must stay lowercase words " +
                    "joined by single dashes.");
                discovered.Add(new OperationKeyRef(type.Name, field.Name, value));
            }
        }
        return discovered;
    }

    private static bool IsKebabCase(string value)
    {
        var wordLength = 0;
        foreach (var c in value)
        {
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
            {
                wordLength++;
            }
            else if (c == '-')
            {
                // A separator between words: a leading dash, trailing dash or doubled dash means
                // an empty word, which is not kebab-case.
                if (wordLength == 0)
                {
                    return false;
                }
                wordLength = 0;
            }
            else
            {
                return false;
            }
        }
        // The last word must be non-empty (rejects a trailing dash and the empty string).
        return wordLength > 0;
    }

    /// <summary>
    /// Finds a file under the frontend tree by walking up from the test working directory
    /// (<c>AppContext.BaseDirectory</c>) to the filesystem root, collecting every candidate path
    /// it searched along the way. The walk has no depth cap - only the filesystem root ends it -
    /// so a checkout nested arbitrarily deep (e.g. under /home/user/projects/...) resolves just
    /// as well as a shallow one. The failure message distinguishes "the repository layout
    /// (AudiobookManager/ + client/) was found but the file is not there" from "no ancestor is a
    /// repository checkout at all".
    /// </summary>
    private static string FindClientFile(string relativeLeafPath)
    {
        var searched = new List<string>();
        var innermostRepoRoot = "";
        var directory = Path.GetDirectoryName(AppContext.BaseDirectory.ToString());
        while (directory is not null)
        {
            if (IsRepoRoot(directory) && innermostRepoRoot.Length == 0)
            {
                innermostRepoRoot = directory;
            }

            var candidate = Path.Combine(directory, relativeLeafPath);
            searched.Add(candidate);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            var parent = Path.GetDirectoryName(directory);
            if (parent is null || parent == directory)
            {
                break;
            }
            directory = parent;
        }

        var location = innermostRepoRoot.Length == 0
            ? "no ancestor is a repository checkout containing both 'AudiobookManager/' and 'client/'"
            : $"a repository checkout was found at '{innermostRepoRoot}' but the file is not under it";

        Assert.Fail(
            $"Could not find '{relativeLeafPath}' under any ancestor of the test working directory " +
            $"'{AppContext.BaseDirectory}' up to the filesystem root; {location}. " +
            $"Searched: {string.Join("; ", searched)}.");
        return "";
    }

    private static bool IsRepoRoot(string directory) =>
        Directory.Exists(Path.Combine(directory, "AudiobookManager")) &&
        Directory.Exists(Path.Combine(directory, "client"));

    private static string ReadTextFile(string path) => File.ReadAllText(path);

    /// <summary>
    /// Parses the entries of an object literal like
    /// <c>export const SignalREvents = { UpdateProgress: "UpdateProgress", ... };</c> into an
    /// ordered list of (key, value) pairs. Line-based rather than brace-matching so comments and
    /// quoting inside the object do not matter, as long as each entry is one
    /// <c>key: "value",</c> pair per line. Two properties make the parsing itself a guard rather
    /// than a best-effort read:
    ///
    /// - every non-comment, non-empty line inside the literal must parse into exactly one pair,
    ///   so a malformed or unreadable entry fails here instead of being silently dropped from
    ///   both sides of the set-membership comparison below;
    /// - duplicate keys or duplicate values inside one literal fail here, since neither can be
    ///   mirrored faithfully in the other language.
    /// </summary>
    private static List<ObjectEntry> ExtractObjectEntries(
        string source,
        string objectName)
    {
        var startMarker = $"export const {objectName} = {{";
        var start = source.IndexOf(startMarker);
        if (start < 0)
        {
            Assert.Fail($"Could not find 'export const {objectName} = {{' in {ClientEventConstantsFile}.");
        }

        var openBrace = source.IndexOf("{", start);
        var closeBrace = source.IndexOf("}", openBrace + 1);
        if (closeBrace < 0)
        {
            Assert.Fail($"Could not find the closing '}}' of the {objectName} object in {ClientEventConstantsFile}.");
        }

        var entries = new List<ObjectEntry>();
        var unparsed = new List<string>();
        foreach (var line in source.Substring(openBrace + 1, closeBrace - openBrace - 1).Split("\n"))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("//") || trimmed.StartsWith("/*"))
            {
                // A blank or comment line inside the object literal.
                continue;
            }

            var colon = trimmed.IndexOf(":");
            if (colon < 1)
            {
                unparsed.Add(trimmed);
                continue;
            }

            var key = trimmed.Substring(0, colon).Trim();
            var rest = trimmed.Substring(colon + 1).Trim();
            if (rest.EndsWith(","))
            {
                rest = rest.Substring(0, rest.Length - 1).Trim();
            }

            if (rest.StartsWith("\"") && rest.EndsWith("\"") && rest.Length >= 2)
            {
                entries.Add(new ObjectEntry(key, rest.Substring(1, rest.Length - 2)));
            }
            else
            {
                unparsed.Add(trimmed);
            }
        }

        Assert.IsTrue(
            entries.Count > 0,
            $"No 'key: \"value\"' entries could be parsed out of the {objectName} object in {ClientEventConstantsFile} - the literal's formatting must have changed (expected one 'key: \"value\"' pair per line).");
        Assert.IsTrue(
            unparsed.Count == 0,
            $"Every non-comment, non-empty line inside the {objectName} object in {ClientEventConstantsFile} must be one 'key: \"value\"' pair, but these lines did not parse: {Join(unparsed)}");

        var duplicateKeys = FindDuplicates(entries.Select(e => e.Key));
        var duplicateValues = FindDuplicates(entries.Select(e => e.Value));
        Assert.IsTrue(
            duplicateKeys.Count == 0,
            $"Duplicate keys inside the {objectName} object in {ClientEventConstantsFile} - an object literal cannot hold two entries under one key: {Join(duplicateKeys)}");
        Assert.IsTrue(
            duplicateValues.Count == 0,
            $"Duplicate values inside the {objectName} object in {ClientEventConstantsFile} - each entry must mirror a distinct backend name: {Join(duplicateValues)}");

        return entries;
    }

    /// <summary>
    /// Returns the values that occur more than once, each listed once in sorted order.
    /// </summary>
    private static List<string> FindDuplicates(IEnumerable<string> values)
    {
        var sorted = values.OrderBy(x => x).ToList();
        var duplicates = new List<string>();
        for (var i = 1; i < sorted.Count; i++)
        {
            if (sorted[i] == sorted[i - 1] &&
                (duplicates.Count == 0 || duplicates[duplicates.Count - 1] != sorted[i]))
            {
                duplicates.Add(sorted[i]);
            }
        }
        return duplicates;
    }

    /// <summary>
    /// Reads the expression assigned to a const, up to the end of its line (or its ';'), e.g.
    /// 'const COVER_MAX_DIMENSION = 1500;' yields '1500'.
    /// </summary>
    private static string ExtractConstantExpression(string source, string constantName)
    {
        var marker = $"const {constantName} = ";
        var start = source.IndexOf(marker);
        if (start < 0)
        {
            Assert.Fail($"Could not find 'const {constantName} =' in {ClientCoverImageFile}.");
        }

        var rest = source.Substring(start + marker.Length);
        var lineEnd = rest.IndexOf("\n");
        var line = (lineEnd < 0 ? rest : rest.Substring(0, lineEnd)).Trim();
        var expressionEnd = line.IndexOf(";");
        if (expressionEnd >= 0)
        {
            line = line.Substring(0, expressionEnd).Trim();
        }

        Assert.IsTrue(
            line.Length > 0,
            $"The value of {constantName} parsed as empty in {ClientCoverImageFile}.");
        return line;
    }

    /// <summary>
    /// Evaluates a plain integer literal or a product of them ('1500', '2 * 1024 * 1024').
    /// </summary>
    private static long EvaluateIntLiteral(string expression)
    {
        foreach (var part in expression.Split("*"))
        {
            if (!IsNonNegativeIntLiteral(part.Trim()))
            {
                Assert.Fail(
                    $"Could not evaluate the constant expression '{expression}' parsed from {ClientCoverImageFile} - expected only plain integers (and ' * ' products) like '1500' or '2 * 1024 * 1024'.");
            }
        }

        var result = 1L;
        foreach (var part in expression.Split("*"))
        {
            result *= long.Parse(part.Trim());
        }

        return result;
    }

    private static bool IsNonNegativeIntLiteral(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (c < '0' || c > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static string Join(IEnumerable<string> values) => string.Join(", ", values.ToList());
}