export const cliPackageId = 'Neolinter.Cli';

/** Analyses the tab can run. `duplicates` exists in the CLI but is not implemented yet. */
export const analyses = ['dead-code'] as const;
export type Analysis = (typeof analyses)[number];

export interface CliCommand {
  command: string;
  args: string[];
  source: 'setting' | 'local' | 'global' | 'project';
}

/** Runs a command and tells whether it exited with code 0. */
export type Probe = (command: string, args: string[]) => Promise<boolean>;

export interface DetectOptions {
  /** `neolinter.cli.path`. */
  path: string;
  /** `neolinter.cli.projectPath`: a .csproj run with `dotnet run --project` (before the tool is published). */
  projectPath: string;
  probe: Probe;
}

/**
 * Finds how to start the CLI: the configured executable, then the local tool manifest, then the global tool,
 * then the configured project. Undefined when none works.
 */
export async function detectCli({ path, projectPath, probe }: DetectOptions): Promise<CliCommand | undefined> {
  if (path.trim() !== '') {
    return { command: path.trim(), args: [], source: 'setting' };
  }

  if (await probe('dotnet', ['tool', 'run', 'neolinter', '--version'])) {
    return { command: 'dotnet', args: ['tool', 'run', 'neolinter'], source: 'local' };
  }
  if (await probe('neolinter', ['--version'])) {
    return { command: 'neolinter', args: [], source: 'global' };
  }
  if (projectPath.trim() !== '') {
    return { command: 'dotnet', args: ['run', '--project', projectPath.trim(), '--'], source: 'project' };
  }
  return undefined;
}

export function analyzeArguments(cli: CliCommand, analysis: Analysis, outputFile: string): string[] {
  return [...cli.args, 'analyze', analysis, '--output', outputFile];
}

/** Exit code 1 means findings were reported; anything else but 0 is a failure (2 is an error). */
export const analysisSucceeded = (exitCode: number | undefined) => exitCode === 0 || exitCode === 1;
