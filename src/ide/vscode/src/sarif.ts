import { fileURLToPath } from 'node:url';

export interface Location {
  file: string;
  startLine: number;
  startColumn: number;
  endLine: number;
  endColumn: number;
}

export interface Finding {
  ruleId: string;
  level: 'note' | 'warning' | 'error';
  message: string;
  location: Location;
  related: Location[];
}

export interface RuleGroup {
  ruleId: string;
  description?: string;
  findings: Finding[];
}

type Json = Record<string, any>;

/** Reads a SARIF 2.1.0 log written by `neolinter analyze`. Results without a usable location are skipped. */
export function parseSarif(text: string): Finding[] {
  const log: Json = JSON.parse(text);
  const findings: Finding[] = [];

  for (const run of log.runs ?? []) {
    const bases: Json = run.originalUriBaseIds ?? {};
    for (const result of run.results ?? []) {
      const location = toLocation(result.locations?.[0]?.physicalLocation, bases);
      if (!location || typeof result.ruleId !== 'string') {
        continue;
      }
      findings.push({
        ruleId: result.ruleId,
        level: result.level === 'error' || result.level === 'note' ? result.level : 'warning',
        message: String(result.message?.text ?? ''),
        location,
        related: (result.relatedLocations ?? []).flatMap((r: Json) => toLocation(r.physicalLocation, bases) ?? []),
      });
    }
  }
  return findings;
}

/** Groups findings by rule id (sorted), keeping the rule short description when the log has one. */
export function groupByRule(findings: Finding[], sarifText?: string): RuleGroup[] {
  const descriptions = new Map<string, string>();
  for (const run of sarifText ? (JSON.parse(sarifText).runs ?? []) : []) {
    for (const rule of run.tool?.driver?.rules ?? []) {
      descriptions.set(rule.id, rule.shortDescription?.text);
    }
  }

  const groups = new Map<string, RuleGroup>();
  for (const finding of findings) {
    const group = groups.get(finding.ruleId) ?? { ruleId: finding.ruleId, description: descriptions.get(finding.ruleId), findings: [] };
    group.findings.push(finding);
    groups.set(finding.ruleId, group);
  }
  return [...groups.values()].sort((a, b) => a.ruleId.localeCompare(b.ruleId));
}

function toLocation(physical: Json | undefined, bases: Json): Location | undefined {
  const artifact = physical?.artifactLocation;
  const region = physical?.region;
  if (typeof artifact?.uri !== 'string' || typeof region?.startLine !== 'number') {
    return undefined;
  }

  let uri = artifact.uri;
  const baseUri: unknown = artifact.uriBaseId ? bases[artifact.uriBaseId]?.uri : undefined;
  if (typeof baseUri === 'string') {
    uri = baseUri.endsWith('/') ? baseUri + uri : `${baseUri}/${uri}`;
  }

  let file: string;
  try {
    file = fileURLToPath(uri);
  } catch {
    return undefined; // relative without a base, or not a file: URI
  }

  const startColumn = region.startColumn ?? 1;
  return {
    file,
    startLine: region.startLine,
    startColumn,
    endLine: region.endLine ?? region.startLine,
    endColumn: region.endColumn ?? startColumn,
  };
}
