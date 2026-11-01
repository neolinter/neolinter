import assert from 'node:assert/strict';
import { test } from 'node:test';
import { analysisSucceeded, analyzeArguments, detectCli } from '../cli';

const probeOnly = (...working: string[]) => async (command: string) => working.includes(command);
const none = { path: '', projectPath: '' };

test('the configured path wins and is not probed', async () => {
  const cli = await detectCli({ ...none, path: ' C:\\tools\\neolinter.exe ', probe: async () => assert.fail('probed') });
  assert.deepEqual(cli, { command: 'C:\\tools\\neolinter.exe', args: [], source: 'setting' });
});

test('local manifest comes before the global tool', async () => {
  const cli = await detectCli({ ...none, probe: probeOnly('dotnet', 'neolinter') });
  assert.equal(cli?.source, 'local');
  assert.deepEqual(analyzeArguments(cli!, 'dead-code', 'out.sarif'), ['tool', 'run', 'neolinter', 'analyze', 'dead-code', '--output', 'out.sarif']);
});

test('falls back to the global tool, then to the project, then to nothing', async () => {
  assert.equal((await detectCli({ ...none, probe: probeOnly('neolinter') }))?.source, 'global');

  const project = await detectCli({ path: '', projectPath: 'src/Neolinter.Cli.csproj', probe: probeOnly() });
  assert.deepEqual(project, { command: 'dotnet', args: ['run', '--project', 'src/Neolinter.Cli.csproj', '--'], source: 'project' });

  assert.equal(await detectCli({ ...none, probe: probeOnly() }), undefined);
});

test('exit code 1 (findings) is a success, 2 and a missing code are not', () => {
  assert.deepEqual([0, 1, 2, undefined].map(analysisSucceeded), [true, true, false, false]);
});
