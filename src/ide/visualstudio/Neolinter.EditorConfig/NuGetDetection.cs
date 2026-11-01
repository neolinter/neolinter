using System.Text.RegularExpressions;

namespace Neolinter.EditorConfig;

public static class NuGetDetection
{
    public const string PackageId = "Neolinter";

    private static readonly Regex Comments = new(@"<!--[\s\S]*?-->", RegexOptions.None, TimeSpan.FromSeconds(1));
    private static readonly Regex Reference = new(
        @"<PackageReference\b[^>]*\bInclude\s*=\s*[""']Neolinter[""']", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    /// <summary>True when an MSBuild file (.csproj, Directory.Build.props...) references the Neolinter package.</summary>
    public static bool ReferencesNeolinter(string msbuildXml) => Reference.IsMatch(Comments.Replace(msbuildXml, ""));
}
