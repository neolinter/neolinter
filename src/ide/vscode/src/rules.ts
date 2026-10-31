export const severities = ['none', 'silent', 'suggestion', 'warning', 'error'] as const;
export type Severity = (typeof severities)[number];

export interface RuleOption {
  key: string;
  type: 'integer' | 'boolean' | 'string' | 'enum';
  default: string | number | boolean;
  minimum?: number;
  values?: string[];
  title: string;
  description: string;
}

/** A rule as described in /rules/*.yaml. */
export interface Rule {
  id: string;
  title: string;
  language: string;
  type: 'realtime' | 'cli';
  category: string;
  defaultSeverity: Severity;
  description: string;
  options: RuleOption[];
}

export const severityKey = (ruleId: string) => `dotnet_diagnostic.${ruleId}.severity`;
