import { execFile } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import * as path from 'node:path';
import * as vscode from 'vscode';
import { Analysis, analyses, analysisSucceeded, analyzeArguments, cliPackageId, detectCli } from './cli';
import { readValues, setValue } from './editorconfig';
import { packageId, referencesNeolinter } from './nuget';
import { Rule, Severity, severities, severityKey } from './rules';
import { Finding, groupByRule, parseSarif } from './sarif';

type WebviewMessage =
  | { type: 'ready' }
  | { type: 'setSeverity'; ruleId: string; severity: Severity }
  | { type: 'setOption'; key: string; value: string }
  | { type: 'addPackage' }
  | { type: 'run'; analysis: Analysis }
  | { type: 'install' }
  | { type: 'recheck' }
  | { type: 'openCliSettings' }
  | { type: 'openFinding'; index: number }
  | { type: 'setProblems'; enabled: boolean };

const msbuildFiles = '**/{*.csproj,Directory.Build.props,Directory.Build.targets}';
const excludedFolders = '**/{bin,obj,node_modules}/**';

const problemsKey = 'neolinter.showInProblems';

let panel: vscode.WebviewPanel | undefined;
let diagnostics: vscode.DiagnosticCollection;
let extensionContext: vscode.ExtensionContext;
let findings: Finding[] = [];
let analysisView: { status: 'idle' | 'running' | 'done' | 'failed'; tool?: string; message?: string; groups: unknown[] } = { status: 'idle', groups: [] };

export function activate(context: vscode.ExtensionContext) {
  extensionContext = context;
  diagnostics = vscode.languages.createDiagnosticCollection('neolinter');
  context.subscriptions.push(diagnostics);
  context.subscriptions.push(vscode.commands.registerCommand('neolinter.openSettings', () => openSettings(context)));
}

export function deactivate() {}

async function openSettings(context: vscode.ExtensionContext) {
  if (panel) {
    panel.reveal();
    return;
  }

  const folder = vscode.workspace.workspaceFolders?.[0];
  if (!folder) {
    vscode.window.showErrorMessage('Neolinter: open a folder first, its .editorconfig stores the settings.');
    return;
  }

  const rules = await loadRules(context);
  const editorConfig = vscode.Uri.joinPath(folder.uri, '.editorconfig');
  const media = vscode.Uri.joinPath(context.extensionUri, 'media');

  panel = vscode.window.createWebviewPanel('neolinter.settings', 'Neolinter Settings', vscode.ViewColumn.Active, {
    enableScripts: true,
    localResourceRoots: [media],
  });
  panel.iconPath = {
    light: vscode.Uri.joinPath(context.extensionUri, 'resources', 'icons', 'neolinter-light.svg'),
    dark: vscode.Uri.joinPath(context.extensionUri, 'resources', 'icons', 'neolinter-dark.svg'),
  };
  panel.webview.html = renderHtml(panel.webview, media);

  const refresh = async () => panel?.webview.postMessage({ type: 'state', ...(await readState(rules, editorConfig)) });

  const watcher = vscode.workspace.createFileSystemWatcher(new vscode.RelativePattern(folder, '{.editorconfig,**/*.csproj,**/Directory.Build.*}'));
  watcher.onDidChange(refresh);
  watcher.onDidCreate(refresh);
  watcher.onDidDelete(refresh);

  panel.webview.onDidReceiveMessage(async (message: WebviewMessage) => {
    switch (message.type) {
      case 'ready':
        await detectTool();
        break;
      case 'run':
        if (analyses.includes(message.analysis)) {
          await runAnalysis(folder, message.analysis);
        }
        return;
      case 'install':
        await installTool(folder);
        return;
      case 'recheck':
        await detectTool();
        return;
      case 'openCliSettings':
        await vscode.commands.executeCommand('workbench.action.openSettings', 'neolinter.cli');
        return;
      case 'openFinding':
        await openFinding(message.index);
        return;
      case 'setProblems':
        await extensionContext.workspaceState.update(problemsKey, message.enabled === true);
        applyDiagnostics();
        postAnalysis();
        return;
      case 'setSeverity':
        if (!severities.includes(message.severity) || !rules.some((r) => r.id === message.ruleId)) {
          return;
        }
        await writeProperty(editorConfig, severityKey(message.ruleId), message.severity);
        break;
      case 'setOption':
        if (!rules.some((r) => r.options.some((o) => o.key === message.key)) || /[\r\n]/.test(message.value)) {
          return;
        }
        await writeProperty(editorConfig, message.key, message.value);
        break;
      case 'addPackage':
        await addPackage();
        break;
    }
    await refresh();
  });

  panel.onDidDispose(() => {
    watcher.dispose();
    panel = undefined;
  });
}

function cliSettings() {
  const config = vscode.workspace.getConfiguration('neolinter.cli');
  return { path: config.get<string>('path', ''), projectPath: config.get<string>('projectPath', '') };
}

function probe(cwd: string) {
  return (command: string, args: string[]) =>
    new Promise<boolean>((resolve) => execFile(command, args, { cwd, timeout: 30000 }, (error) => resolve(!error)));
}

function detect(folder: vscode.WorkspaceFolder) {
  return detectCli({ ...cliSettings(), probe: probe(folder.uri.fsPath) });
}

function postAnalysis() {
  panel?.webview.postMessage({ type: 'analysis', ...analysisView, showInProblems: extensionContext.workspaceState.get(problemsKey, false) });
}

async function detectTool() {
  const folder = vscode.workspace.workspaceFolders?.[0];
  const cli = folder && (await detect(folder));
  analysisView = { ...analysisView, tool: cli ? cli.source : undefined };
  postAnalysis();
}

async function runAnalysis(folder: vscode.WorkspaceFolder, analysis: Analysis) {
  if (analysisView.status === 'running') {
    return;
  }

  const cli = await detect(folder);
  if (!cli) {
    analysisView = { ...analysisView, tool: undefined, status: 'failed', message: 'The neolinter command line was not found.' };
    postAnalysis();
    return;
  }

  const output = path.join(tmpdir(), `neolinter-${analysis}-${Date.now()}.sarif`);
  const name = `Neolinter ${analysis} ${Date.now()}`;
  const task = new vscode.Task(
    { type: 'process' },
    folder,
    name,
    'neolinter',
    new vscode.ProcessExecution(cli.command, analyzeArguments(cli, analysis, output), { cwd: folder.uri.fsPath }),
  );
  task.presentationOptions = { reveal: vscode.TaskRevealKind.Silent, clear: true };

  analysisView = { ...analysisView, tool: cli.source, status: 'running', message: undefined };
  postAnalysis();

  // Listen before starting so a fast exit is not missed.
  const exitCode = new Promise<number | undefined>((resolve) => {
    const listener = vscode.tasks.onDidEndTaskProcess((event) => {
      if (event.execution.task.name === name) {
        listener.dispose();
        resolve(event.exitCode);
      }
    });
  });

  try {
    await vscode.tasks.executeTask(task);
    const code = await exitCode;
    if (!analysisSucceeded(code)) {
      throw new Error(`The analysis failed (exit code ${code ?? 'unknown'}). See the task output in the Terminal panel.`);
    }

    const sarif = await readFile(output, 'utf8');
    findings = parseSarif(sarif);
    analysisView = {
      ...analysisView,
      status: 'done',
      groups: groupByRule(findings, sarif).map((group) => ({
        ruleId: group.ruleId,
        description: group.description,
        findings: group.findings.map((f) => ({
          index: findings.indexOf(f),
          message: f.message,
          file: vscode.workspace.asRelativePath(f.location.file),
          line: f.location.startLine,
          column: f.location.startColumn,
        })),
      })),
    };
  } catch (error) {
    findings = [];
    analysisView = { ...analysisView, status: 'failed', groups: [], message: error instanceof Error ? error.message : String(error) };
  }
  applyDiagnostics();
  postAnalysis();
}

function applyDiagnostics() {
  diagnostics.clear();
  if (!extensionContext.workspaceState.get(problemsKey, false)) {
    return;
  }

  const byFile = new Map<string, vscode.Diagnostic[]>();
  for (const finding of findings) {
    const diagnostic = new vscode.Diagnostic(toRange(finding.location), finding.message, severityOf(finding.level));
    diagnostic.source = 'neolinter';
    diagnostic.code = finding.ruleId;
    diagnostic.relatedInformation = finding.related.map(
      (r) => new vscode.DiagnosticRelatedInformation(new vscode.Location(vscode.Uri.file(r.file), toRange(r)), 'Related location'),
    );
    byFile.set(finding.location.file, [...(byFile.get(finding.location.file) ?? []), diagnostic]);
  }
  diagnostics.set([...byFile].map(([file, list]) => [vscode.Uri.file(file), list]));
}

const severityOf = (level: Finding['level']) =>
  level === 'error' ? vscode.DiagnosticSeverity.Error : level === 'note' ? vscode.DiagnosticSeverity.Information : vscode.DiagnosticSeverity.Warning;

const toRange = (l: Finding['location']) => new vscode.Range(l.startLine - 1, l.startColumn - 1, l.endLine - 1, l.endColumn - 1);

/** The webview only sends an index into the last results; paths never come from the page. */
async function openFinding(index: number) {
  const finding = findings[index];
  if (!finding) {
    return;
  }
  const document = await vscode.workspace.openTextDocument(vscode.Uri.file(finding.location.file));
  await vscode.window.showTextDocument(document, { selection: toRange(finding.location) });
}

async function installTool(folder: vscode.WorkspaceFolder) {
  const picked = await vscode.window.showQuickPick(
    [
      { label: 'Global tool', description: `dotnet tool install --global ${cliPackageId}`, local: false },
      { label: 'Local tool', description: 'Tool manifest (.config/dotnet-tools.json) of this folder, shared with the team', local: true },
    ],
    { placeHolder: `Install ${cliPackageId} (the package is not on nuget.org until the first release)` },
  );
  if (!picked) {
    return;
  }

  const hasManifest = (await readText(vscode.Uri.joinPath(folder.uri, '.config', 'dotnet-tools.json'))) !== undefined;
  const terminal = vscode.window.createTerminal({ name: 'Neolinter', cwd: folder.uri.fsPath });
  terminal.show();
  terminal.sendText(
    picked.local ? `${hasManifest ? '' : 'dotnet new tool-manifest && '}dotnet tool install ${cliPackageId}` : `dotnet tool install --global ${cliPackageId}`,
  );
  vscode.window.showInformationMessage('Neolinter: when the installation is done, use "Check again" in the Solution analysis tab.');
}

async function loadRules(context: vscode.ExtensionContext): Promise<Rule[]> {
  const bytes = await vscode.workspace.fs.readFile(vscode.Uri.joinPath(context.extensionUri, 'resources', 'rules.json'));
  return JSON.parse(Buffer.from(bytes).toString('utf8')) as Rule[];
}

async function readText(uri: vscode.Uri): Promise<string | undefined> {
  try {
    return Buffer.from(await vscode.workspace.fs.readFile(uri)).toString('utf8');
  } catch {
    return undefined;
  }
}

async function readState(rules: Rule[], editorConfig: vscode.Uri) {
  const realtime = rules.filter((r) => r.type === 'realtime');
  const keys = realtime.flatMap((r) => [severityKey(r.id), ...r.options.map((o) => o.key)]);
  const values = readValues((await readText(editorConfig)) ?? '', keys);

  const projects = await vscode.workspace.findFiles(msbuildFiles, excludedFolders);
  const contents = await Promise.all(projects.map(readText));

  return {
    rules,
    values: Object.fromEntries(values),
    hasCSharpProject: projects.some((p) => p.path.endsWith('.csproj')),
    packageReferenced: contents.some((c) => c !== undefined && referencesNeolinter(c)),
  };
}

/** Edits through a TextDocument when the file exists, so unsaved changes in an open editor are not lost. */
async function writeProperty(uri: vscode.Uri, key: string, value: string) {
  const existing = await readText(uri);
  if (existing === undefined) {
    await vscode.workspace.fs.writeFile(uri, Buffer.from(setValue('', key, value), 'utf8'));
    return;
  }

  const document = await vscode.workspace.openTextDocument(uri);
  const edit = new vscode.WorkspaceEdit();
  const fullRange = new vscode.Range(document.positionAt(0), document.positionAt(document.getText().length));
  edit.replace(uri, fullRange, setValue(document.getText(), key, value));
  await vscode.workspace.applyEdit(edit);
  await document.save();
}

async function addPackage() {
  const projects = (await vscode.workspace.findFiles('**/*.csproj', excludedFolders)).map((uri) => ({
    label: vscode.workspace.asRelativePath(uri),
    uri,
  }));
  if (projects.length === 0) {
    vscode.window.showWarningMessage('Neolinter: no .csproj file found in the workspace.');
    return;
  }

  const picked = await vscode.window.showQuickPick(projects, { placeHolder: `Project to add the ${packageId} package to` });
  if (!picked) {
    return;
  }

  const terminal = vscode.window.createTerminal('Neolinter');
  terminal.show();
  terminal.sendText(`dotnet add "${picked.uri.fsPath}" package ${packageId}`);
}

function renderHtml(webview: vscode.Webview, media: vscode.Uri): string {
  const nonce = randomBytes(16).toString('base64');
  const script = webview.asWebviewUri(vscode.Uri.joinPath(media, 'settings.js'));
  const style = webview.asWebviewUri(vscode.Uri.joinPath(media, 'settings.css'));
  return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src ${webview.cspSource}; script-src 'nonce-${nonce}';">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <link href="${style}" rel="stylesheet">
  <title>Neolinter Settings</title>
</head>
<body>
  <h1>Neolinter</h1>
  <div id="package-banner" class="banner" role="status" hidden>
    <span>The ${packageId} NuGet package is not referenced: rules are not loaded by the C# language server.</span>
    <button id="add-package" type="button">Add package…</button>
  </div>
  <div role="tablist" aria-label="Neolinter settings">
    <button role="tab" id="tab-rules" aria-controls="panel-rules" aria-selected="true">Rules</button>
    <button role="tab" id="tab-analysis" aria-controls="panel-analysis" aria-selected="false" tabindex="-1">Solution analysis</button>
  </div>
  <section role="tabpanel" id="panel-rules" aria-labelledby="tab-rules">
    <p class="hint">Settings are written to the workspace <code>.editorconfig</code>, section <code>[*.cs]</code>.</p>
    <div id="rules"></div>
  </section>
  <section role="tabpanel" id="panel-analysis" aria-labelledby="tab-analysis" hidden>
    <p class="hint">Runs the <code>neolinter</code> command line on the solution of the workspace folder (restore it first with <code>dotnet restore</code>).</p>
    <div id="tool-status" class="banner" role="status"></div>
    <div class="row">
      <button id="install" type="button">Install…</button>
      <button id="recheck" type="button">Check again</button>
      <button id="cli-settings" type="button">Settings</button>
    </div>
    <div class="row">
      <label for="analysis">Analysis</label>
      <select id="analysis"><option value="dead-code">Dead code (NLC0002)</option><option value="duplicates">Duplicated code (NLC0003)</option></select>
      <button id="run" type="button">Run</button>
    </div>
    <div class="row">
      <input type="checkbox" id="problems"><label for="problems">Show in Problems panel</label>
    </div>
    <p id="run-status" class="hint" role="status"></p>
    <div id="results"></div>
  </section>
  <script nonce="${nonce}" src="${script}"></script>
</body>
</html>`;
}
