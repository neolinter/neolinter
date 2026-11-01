using System.Text;
using System.Text.RegularExpressions;

namespace Neolinter.EditorConfig;

/// <summary>
/// Minimal, lossless .editorconfig editing: only the targeted line changes, everything else (comments, blank
/// lines, other sections, line endings) is kept byte for byte. Same behaviour as the VS Code implementation;
/// both pass the cases in tests/fixtures/editorconfig.
/// </summary>
public static class EditorConfigFile
{
    private sealed record Section(string? Glob, int Header, int End);

    private sealed record Property(string Indent, string Name, string Separator, string Value, string? Comment);

    private static readonly Regex SectionPattern = new(@"^\s*\[(.*)\]\s*$", RegexOptions.None, TimeSpan.FromSeconds(1));
    private static readonly Regex Braces = new(@"^\*\.\{([^}]*)\}$", RegexOptions.None, TimeSpan.FromSeconds(1));

    // Hand-written scan instead of one regular expression: whitespace-heavy lines make such patterns backtrack.
    private static Property? ParseProperty(string line)
    {
        var start = line.Length - line.TrimStart().Length;
        var equals = line.IndexOf('=');
        if (equals < 0 || start >= line.Length || "=#;[".IndexOf(line[start]) >= 0)
        {
            return null;
        }

        var name = line.Substring(start, equals - start).TrimEnd();
        var rest = line.Substring(equals + 1);
        var valueStart = equals + 1 + (rest.Length - rest.TrimStart().Length);
        var end = line.Length;
        for (var i = valueStart + 1; i < line.Length; i++)
        {
            if ((line[i] == '#' || line[i] == ';') && char.IsWhiteSpace(line[i - 1]))
            {
                end = i;
                while (end > valueStart && char.IsWhiteSpace(line[end - 1]))
                {
                    end--;
                }

                break;
            }
        }

        return new Property(
            line.Substring(0, start),
            name,
            line.Substring(start + name.Length, valueStart - start - name.Length),
            line.Substring(valueStart, end - valueStart),
            end < line.Length ? line.Substring(end) : null);
    }

    private static (List<string> Lines, string Eol) SplitLines(string text)
    {
        var eol = text.Contains("\r\n") ? "\r\n" : "\n";
        return (new List<string>(text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)), eol);
    }

    private static List<Section> Sections(List<string> lines)
    {
        var result = new List<Section> { new(null, -1, lines.Count) };
        for (var index = 0; index < lines.Count; index++)
        {
            var match = SectionPattern.Match(lines[index]);
            if (match.Success)
            {
                result[result.Count - 1] = result[result.Count - 1] with { End = index };
                result.Add(new Section(match.Groups[1].Value.Trim(), index, lines.Count));
            }
        }

        return result;
    }

    /// <summary>
    /// True when the section glob applies to every C# file of the workspace.
    /// shortcut: only the common forms (*, *.cs, **.cs, **/*.cs, *.{cs,...}) are recognised; path-specific globs
    /// are ignored when reading. Upgrade to a full glob matcher if users need them.
    /// </summary>
    public static bool AppliesToCSharp(string glob)
    {
        var pattern = glob.StartsWith("**/", StringComparison.Ordinal) ? glob.Substring(3) : glob;
        if (pattern.StartsWith("**", StringComparison.Ordinal))
        {
            pattern = "*" + pattern.Substring(2);
        }

        if (pattern is "*" or "*.cs")
        {
            return true;
        }

        var braces = Braces.Match(pattern);
        return braces.Success && braces.Groups[1].Value.Split(',').Any(extension => extension.Trim() == "cs");
    }

    private static int FindProperty(List<string> lines, Section section, string key)
    {
        for (var index = section.Header + 1; index < section.End; index++)
        {
            var property = ParseProperty(lines[index]);
            if (property is not null && string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>Effective values of the given keys for C# files: the last matching section wins, keys are case-insensitive.</summary>
    public static IReadOnlyDictionary<string, string> ReadValues(string text, IEnumerable<string> keys)
    {
        var (lines, _) = SplitLines(text);
        var values = new Dictionary<string, string>();
        var wanted = keys.ToList();
        foreach (var section in Sections(lines))
        {
            if (section.Glob is null || !AppliesToCSharp(section.Glob))
            {
                continue;
            }

            foreach (var key in wanted)
            {
                var index = FindProperty(lines, section, key);
                if (index >= 0)
                {
                    values[key] = ParseProperty(lines[index])!.Value;
                }
            }
        }

        return values;
    }

    /// <summary>
    /// Sets a property for C# files. Updates the line that is currently effective if there is one, otherwise adds
    /// it to the last [*.cs] section, otherwise appends a new [*.cs] section.
    /// </summary>
    public static string SetValue(string text, string key, string value)
    {
        var (lines, eol) = SplitLines(text);
        var all = Sections(lines);

        foreach (var section in Enumerable.Reverse(all.Where(s => s.Glob is not null && AppliesToCSharp(s.Glob)).ToList()))
        {
            var index = FindProperty(lines, section, key);
            if (index >= 0)
            {
                var p = ParseProperty(lines[index])!;
                lines[index] = $"{p.Indent}{p.Name}{p.Separator}{value}{p.Comment}";
                return string.Join(eol, lines);
            }
        }

        var targets = all.Where(s => s.Glob == "*.cs").ToList();
        if (targets.Count > 0)
        {
            var target = targets[targets.Count - 1];
            // Insert after the last non-blank line of the section, keeping the blank lines that separate sections.
            var insertAt = target.End;
            while (insertAt > target.Header + 1 && lines[insertAt - 1].Trim().Length == 0)
            {
                insertAt--;
            }

            lines.Insert(insertAt, $"{key} = {value}");
            return string.Join(eol, lines);
        }

        // The last element is "" when the text ends with a newline.
        while (lines.Count > 0 && lines[lines.Count - 1].Trim().Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        var result = new StringBuilder();
        if (lines.Count > 0)
        {
            result.Append(string.Join(eol, lines)).Append(eol).Append(eol);
        }

        return result.Append("[*.cs]").Append(eol).Append(key).Append(" = ").Append(value).Append(eol).ToString();
    }
}
