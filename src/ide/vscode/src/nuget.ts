export const packageId = 'Neolinter';

/** True when an MSBuild file (.csproj, Directory.Build.props...) references the Neolinter package. */
export function referencesNeolinter(msbuildXml: string): boolean {
  const withoutComments = msbuildXml.replace(/<!--[\s\S]*?-->/g, '');
  return /<PackageReference\b[^>]*\bInclude\s*=\s*["']Neolinter["']/i.test(withoutComments);
}
