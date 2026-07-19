using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

// Dev-only consistency checks for the VisitedTraderTeleport repository.
// Never shipped: lives under devtools/, outside the packaged mod folder.
//
// Usage (from the repository root):
//   dotnet run --project devtools/ModChecks              repo checks only
//   dotnet run --project devtools/ModChecks -- --package repo checks + dist ZIP allowlist
//
// Exit code is the number of failed checks.

int failures = 0;
bool checkPackage = args.Contains("--package");

string repoRoot = FindRepoRoot();
string modDir = Path.Combine(repoRoot, "mod", "VisitedTraderTeleport");
string modInfoPath = Path.Combine(modDir, "ModInfo.xml");
string changelogPath = Path.Combine(repoRoot, "CHANGELOG.md");
string csvPath = Path.Combine(modDir, "Config", "Localization.csv");

string version = CheckVersionConsistency();
CheckLocalizationCsv();
if (checkPackage)
{
    CheckPackageContents(version);
}

Console.WriteLine(failures == 0
    ? "ModChecks: all checks passed."
    : $"ModChecks: {failures} check(s) FAILED.");
return failures;

string FindRepoRoot()
{
    DirectoryInfo? dir = new(Directory.GetCurrentDirectory());
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "VisitedTraderTeleport.sln")))
    {
        dir = dir.Parent;
    }

    if (dir == null)
    {
        Console.WriteLine("FAIL cannot locate repository root (VisitedTraderTeleport.sln) from the current directory.");
        Environment.Exit(1);
    }

    return dir.FullName;
}

void Pass(string message) => Console.WriteLine($"PASS {message}");

void Fail(string message)
{
    Console.WriteLine($"FAIL {message}");
    failures++;
}

string CheckVersionConsistency()
{
    string modVersion = XDocument.Load(modInfoPath).Root?.Element("Version")?.Attribute("value")?.Value ?? "";
    Match changelogTop = Regex.Match(File.ReadAllText(changelogPath), @"^## (\d+\.\d+\.\d+) - ", RegexOptions.Multiline);
    string changelogVersion = changelogTop.Success ? changelogTop.Groups[1].Value : "";

    if (modVersion.Length == 0)
    {
        Fail("ModInfo.xml has no Version value.");
    }
    else if (modVersion != changelogVersion)
    {
        Fail($"version mismatch: ModInfo.xml={modVersion}, CHANGELOG.md top entry={changelogVersion}.");
    }
    else
    {
        Pass($"ModInfo.xml and CHANGELOG.md agree on version {modVersion}.");
    }

    return modVersion;
}

void CheckLocalizationCsv()
{
    if (!File.Exists(csvPath))
    {
        Fail($"missing {Path.GetRelativePath(repoRoot, csvPath)}.");
        return;
    }

    byte[] raw = File.ReadAllBytes(csvPath);
    if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF)
    {
        Fail("Localization.csv starts with a UTF-8 BOM; the shipped file is BOM-less.");
    }

    string text = Encoding.UTF8.GetString(raw);
    if (Regex.IsMatch(text, @"(?<!\r)\n"))
    {
        Fail("Localization.csv contains LF line endings without CR; packaged files must be CRLF " +
             "(re-checkout with .gitattributes in place, or normalize the file).");
    }

    string[] lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(l => l.TrimEnd('\r'))
        .Where(l => l.Length > 0)
        .ToArray();
    if (lines.Length < 2)
    {
        Fail("Localization.csv has no data rows.");
        return;
    }

    List<string>? header = ParseCsvLine(lines[0], 1);
    if (header == null)
    {
        return;
    }

    const int ExpectedColumns = 20;
    int columnCount = header.Count;
    if (columnCount != ExpectedColumns)
    {
        Fail($"Localization.csv header has {columnCount} columns, expected {ExpectedColumns} (3.0 format).");
        return;
    }

    int englishIdx = header.IndexOf("english");
    if (englishIdx < 0)
    {
        Fail("Localization.csv header has no 'english' column.");
        return;
    }

    var placeholderRegex = new Regex(@"\{\d+\}");
    int rowFailures = 0;
    for (int i = 1; i < lines.Length; i++)
    {
        List<string>? fields = ParseCsvLine(lines[i], i + 1);
        if (fields == null)
        {
            rowFailures++;
            continue;
        }

        if (fields.Count != columnCount)
        {
            Fail($"Localization.csv line {i + 1}: {fields.Count} fields, expected {columnCount}.");
            rowFailures++;
            continue;
        }

        if (!fields[0].StartsWith("vtt_", StringComparison.Ordinal))
        {
            Fail($"Localization.csv line {i + 1}: key '{fields[0]}' does not start with vtt_.");
            rowFailures++;
        }

        var englishPlaceholders = placeholderRegex.Matches(fields[englishIdx])
            .Select(m => m.Value).ToHashSet();
        for (int col = englishIdx; col < fields.Count; col++)
        {
            if (fields[col].Length == 0)
            {
                continue;
            }

            var cellPlaceholders = placeholderRegex.Matches(fields[col]).Select(m => m.Value).ToHashSet();
            if (!cellPlaceholders.SetEquals(englishPlaceholders))
            {
                Fail($"Localization.csv line {i + 1} ({fields[0]}), column '{header[col]}': placeholders " +
                     $"[{string.Join(",", cellPlaceholders)}] do not match english [{string.Join(",", englishPlaceholders)}].");
                rowFailures++;
            }
        }
    }

    if (rowFailures == 0)
    {
        Pass($"Localization.csv: {lines.Length - 1} rows x {columnCount} columns, CRLF, placeholders consistent.");
    }
}

List<string>? ParseCsvLine(string line, int lineNumber)
{
    var fields = new List<string>();
    var current = new StringBuilder();
    bool inQuotes = false;

    for (int i = 0; i < line.Length; i++)
    {
        char c = line[i];
        if (inQuotes)
        {
            if (c == '"')
            {
                if (i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = false;
                }
            }
            else
            {
                current.Append(c);
            }
        }
        else if (c == '"' && current.Length == 0)
        {
            inQuotes = true;
        }
        else if (c == ',')
        {
            fields.Add(current.ToString());
            current.Clear();
        }
        else
        {
            current.Append(c);
        }
    }

    if (inQuotes)
    {
        Fail($"Localization.csv line {lineNumber}: unbalanced quotes.");
        return null;
    }

    fields.Add(current.ToString());
    return fields;
}

void CheckPackageContents(string version)
{
    string zipPath = Path.Combine(repoRoot, "dist", $"VisitedTraderTeleport-{version}.zip");
    if (!File.Exists(zipPath))
    {
        Fail($"package not found: {Path.GetRelativePath(repoRoot, zipPath)} (build first).");
        return;
    }

    // The only files a player may ever receive. Anything else in the ZIP - a debug helper,
    // a test config, a stray script - is a release blocker.
    var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "VisitedTraderTeleport/ModInfo.xml",
        "VisitedTraderTeleport/VisitedTraderTeleport.dll",
        "VisitedTraderTeleport/Changelog.txt",
        "VisitedTraderTeleport/LICENSE",
        "VisitedTraderTeleport/Config/dialogs.xml",
        "VisitedTraderTeleport/Config/Localization.csv",
        "VisitedTraderTeleport/Config/VisitedTraderTeleport.xml",
    };

    using ZipArchive zip = ZipFile.OpenRead(zipPath);
    var entries = zip.Entries
        .Where(e => !e.FullName.EndsWith("/", StringComparison.Ordinal))
        .Select(e => e.FullName.Replace('\\', '/'))
        .ToList();

    var unexpected = entries.Where(e => !allowed.Contains(e)).ToList();
    var missing = allowed.Where(a => !entries.Contains(a, StringComparer.OrdinalIgnoreCase)).ToList();

    foreach (string entry in unexpected)
    {
        Fail($"unexpected file in package: {entry}");
    }

    foreach (string entry in missing)
    {
        Fail($"missing file in package: {entry}");
    }

    ZipArchiveEntry? packagedChangelog = zip.Entries.FirstOrDefault(e =>
        e.FullName.Replace('\\', '/').Equals("VisitedTraderTeleport/Changelog.txt", StringComparison.OrdinalIgnoreCase));
    if (packagedChangelog != null)
    {
        using var reader = new StreamReader(packagedChangelog.Open());
        Match top = Regex.Match(reader.ReadToEnd(), @"^## (\d+\.\d+\.\d+) - ", RegexOptions.Multiline);
        if (!top.Success || top.Groups[1].Value != version)
        {
            Fail($"packaged Changelog.txt top entry ({(top.Success ? top.Groups[1].Value : "none")}) does not match version {version}; rebuild.");
        }
    }

    if (unexpected.Count == 0 && missing.Count == 0)
    {
        Pass($"package {Path.GetFileName(zipPath)}: exactly the {allowed.Count} expected files, Changelog.txt current.");
    }
}
