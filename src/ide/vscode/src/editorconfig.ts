// Minimal, lossless .editorconfig editing: only the targeted line changes, everything else
// (comments, blank lines, other sections, line endings) is kept byte for byte.

interface Section {
  /** Glob between brackets, or null for the preamble before the first section. */
  glob: string | null;
  /** Index of the header line (-1 for the preamble). */
  header: number;
  /** Index after the last line of the section. */
  end: number;
}

const sectionPattern = /^\s*\[(.*)\]\s*$/;

/** Parts of a `key = value # comment` line; the comment keeps its leading whitespace. */
type Property = [indent: string, name: string, separator: string, value: string, comment: string | undefined];

// Hand-written scan instead of one regular expression: whitespace-heavy lines made the pattern backtrack heavily.
function parseProperty(line: string): Property | null {
  const start = line.length - line.trimStart().length;
  const equals = line.indexOf('=', start);
  if (equals < 0 || start >= line.length || '=#;['.includes(line[start])) {
    return null;
  }
  const name = line.slice(start, equals).trimEnd();
  const valueStart = equals + 1 + (line.length - equals - 1 - line.slice(equals + 1).trimStart().length);
  let end = line.length;
  for (let i = valueStart + 1; i < line.length; i++) {
    if ((line[i] === '#' || line[i] === ';') && /\s/.test(line[i - 1])) {
      end = i;
      while (end > valueStart && /\s/.test(line[end - 1])) {
        end--;
      }
      break;
    }
  }
  return [
    line.slice(0, start),
    name,
    line.slice(start + name.length, valueStart),
    line.slice(valueStart, end),
    end < line.length ? line.slice(end) : undefined,
  ];
}

function splitLines(text: string): { lines: string[]; eol: string } {
  const eol = text.includes('\r\n') ? '\r\n' : '\n';
  return { lines: text.split(/\r?\n/), eol };
}

function sections(lines: string[]): Section[] {
  const result: Section[] = [{ glob: null, header: -1, end: lines.length }];
  lines.forEach((line, index) => {
    const match = sectionPattern.exec(line);
    if (match) {
      result[result.length - 1].end = index;
      result.push({ glob: match[1].trim(), header: index, end: lines.length });
    }
  });
  return result;
}

/**
 * True when the section glob applies to every C# file of the workspace.
 * shortcut: only the common forms (*, *.cs, **.cs, **\/*.cs, *.{cs,...}) are recognised; path-specific
 * globs such as src/**\/*.cs are ignored when reading. Upgrade to a full glob matcher if users need them.
 */
export function appliesToCSharp(glob: string): boolean {
  const pattern = glob.replace(/^\*\*\//, '').replace(/^\*\*/, '*');
  if (pattern === '*' || pattern === '*.cs') {
    return true;
  }
  const braces = /^\*\.\{([^}]*)\}$/.exec(pattern);
  return braces !== null && braces[1].split(',').some((extension) => extension.trim() === 'cs');
}

function findProperty(lines: string[], section: Section, key: string): number {
  for (let index = section.header + 1; index < section.end; index++) {
    const match = parseProperty(lines[index]);
    if (match && match[1].toLowerCase() === key.toLowerCase()) {
      return index;
    }
  }
  return -1;
}

/** Effective values of the given keys for C# files: the last matching section wins, keys are case-insensitive. */
export function readValues(text: string, keys: string[]): Map<string, string> {
  const { lines } = splitLines(text);
  const values = new Map<string, string>();
  for (const section of sections(lines)) {
    if (section.glob === null || !appliesToCSharp(section.glob)) {
      continue;
    }
    for (const key of keys) {
      const index = findProperty(lines, section, key);
      if (index >= 0) {
        values.set(key, parseProperty(lines[index])![3]);
      }
    }
  }
  return values;
}

/**
 * Sets a property for C# files. Updates the line that is currently effective if there is one,
 * otherwise adds it to the last [*.cs] section, otherwise appends a new [*.cs] section.
 */
export function setValue(text: string, key: string, value: string): string {
  const { lines, eol } = splitLines(text);
  const all = sections(lines);
  const csharpSections = all.filter((s) => s.glob !== null && appliesToCSharp(s.glob));

  for (const section of [...csharpSections].reverse()) {
    const index = findProperty(lines, section, key);
    if (index >= 0) {
      const [indent, name, separator, , comment] = parseProperty(lines[index])!;
      lines[index] = `${indent}${name}${separator}${value}${comment ?? ''}`;
      return lines.join(eol);
    }
  }

  const target = [...all].reverse().find((s) => s.glob === '*.cs');
  if (target) {
    // Insert after the last non-blank line of the section, keeping the blank lines that separate sections.
    let insertAt = target.end;
    while (insertAt > target.header + 1 && lines[insertAt - 1].trim() === '') {
      insertAt--;
    }
    lines.splice(insertAt, 0, `${key} = ${value}`);
    return lines.join(eol);
  }

  // The last element is '' when the text ends with a newline.
  while (lines.length > 0 && lines[lines.length - 1].trim() === '') {
    lines.pop();
  }
  const prefix = lines.length > 0 ? [...lines, ''] : [];
  return [...prefix, '[*.cs]', `${key} = ${value}`, ''].join(eol);
}
