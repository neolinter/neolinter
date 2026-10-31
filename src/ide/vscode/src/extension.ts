import { randomBytes } from 'node:crypto';
import * as vscode from 'vscode';
import { readValues, setValue } from './editorconfig';
import { packageId, referencesNeolinter } from './nuget';
import { Rule, Severity, severities, severityKey } from './rules';

type WebviewMessage =
  | { type: 'ready' }
  | { type: 'setSeverity'; ruleId: string; severity: Severity }
  | { type: 'setOption'; key: string; value: string }
  | { type: 'addPackage' };

const msbuildFiles = '**/{*.csproj,Directory.Build.props,Directory.Build.targets}';
const excludedFolders = '**/{bin,obj,node_modules}/**';

let panel: vscode.WebviewPanel | undefined;

export function activate(context: vscode.ExtensionContext) {
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
  panel.webview.html = renderHtml(panel.webview, media);

  const refresh = async () => panel?.webview.postMessage({ type: 'state', ...(await readState(rules, editorConfig)) });

  const watcher = vscode.workspace.createFileSystemWatcher(new vscode.RelativePattern(folder, '{.editorconfig,**/*.csproj,**/Directory.Build.*}'));
  watcher.onDidChange(refresh);
  watcher.onDidCreate(refresh);
  watcher.onDidDelete(refresh);

  panel.webview.onDidReceiveMessage(async (message: WebviewMessage) => {
    switch (message.type) {
      case 'ready':
        break;
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
    <button role="tab" id="tab-analysis" aria-controls="panel-analysis" aria-selected="false" aria-disabled="true" tabindex="-1"
      title="Available once the neolinter command line is ready">Solution analysis</button>
  </div>
  <section role="tabpanel" id="panel-rules" aria-labelledby="tab-rules">
    <p class="hint">Settings are written to the workspace <code>.editorconfig</code>, section <code>[*.cs]</code>.</p>
    <div id="rules"></div>
  </section>
  <section role="tabpanel" id="panel-analysis" aria-labelledby="tab-analysis" hidden>
    <p>Dead code and duplicated code analyses will run here through the <code>neolinter</code> command line.</p>
  </section>
  <script nonce="${nonce}" src="${script}"></script>
</body>
</html>`;
}
