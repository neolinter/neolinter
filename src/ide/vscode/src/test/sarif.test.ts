import assert from 'node:assert/strict';
import { test } from 'node:test';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { groupByRule, parseSarif } from '../sarif';

const root = pathToFileURL('/work/solution/').href;

const log = JSON.stringify({
  version: '2.1.0',
  runs: [
    {
      tool: { driver: { name: 'Neolinter', rules: [{ id: 'NLD0001', shortDescription: { text: 'Unused member' } }] } },
      originalUriBaseIds: { SRCROOT: { uri: root } },
      results: [
        {
          ruleId: 'NLD0001',
          level: 'warning',
          message: { text: 'B is never used.' },
          locations: [{ physicalLocation: { artifactLocation: { uri: 'src/My%20Dir/B.cs', uriBaseId: 'SRCROOT' }, region: { startLine: 4, startColumn: 3, endLine: 4, endColumn: 9 } } }],
        },
        {
          ruleId: 'NLC0001',
          level: 'error',
          message: { text: 'Duplicate.' },
          locations: [{ physicalLocation: { artifactLocation: { uri: 'A.cs', uriBaseId: 'SRCROOT' }, region: { startLine: 1 } } }],
          relatedLocations: [{ id: 0, physicalLocation: { artifactLocation: { uri: pathToFileURL('/elsewhere/C.cs').href }, region: { startLine: 7, startColumn: 1, endLine: 9, endColumn: 2 } } }],
        },
        { ruleId: 'NLD0001', message: { text: 'No location.' } },
        {
          ruleId: 'NLD0001',
          message: { text: 'Default level.' },
          locations: [{ physicalLocation: { artifactLocation: { uri: 'A.cs', uriBaseId: 'SRCROOT' }, region: { startLine: 2 } } }],
        },
      ],
    },
  ],
});

test('resolves locations against the SRCROOT base and decodes the path', () => {
  const [first] = parseSarif(log);
  assert.equal(first.location.file, fileURLToPath(`${root}src/My%20Dir/B.cs`));
  assert.deepEqual([first.location.startLine, first.location.startColumn, first.location.endLine, first.location.endColumn], [4, 3, 4, 9]);
  assert.equal(first.level, 'warning');
});

test('keeps absolute related locations and fills missing columns and end line', () => {
  const second = parseSarif(log)[1];
  assert.equal(second.level, 'error');
  assert.deepEqual([second.location.startColumn, second.location.endLine], [1, 1]);
  assert.equal(second.related[0].file, fileURLToPath(pathToFileURL('/elsewhere/C.cs')));
  assert.equal(second.related[0].endLine, 9);
});

test('skips results without a location and defaults the level to warning', () => {
  const findings = parseSarif(log);
  assert.equal(findings.length, 3);
  assert.equal(findings[2].level, 'warning');
});

test('returns nothing for a log without results', () => {
  assert.deepEqual(parseSarif('{"version":"2.1.0","runs":[{"results":[]}]}'), []);
  assert.deepEqual(parseSarif('{"version":"2.1.0"}'), []);
});

test('rejects text that is not JSON', () => {
  assert.throws(() => parseSarif('not json'));
});

test('groups by rule id, sorted, with the description from the log', () => {
  const groups = groupByRule(parseSarif(log), log);
  assert.deepEqual(groups.map((g) => [g.ruleId, g.findings.length, g.description]), [
    ['NLC0001', 1, undefined],
    ['NLD0001', 2, 'Unused member'],
  ]);
});
