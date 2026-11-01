import assert from 'node:assert/strict';
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { appliesToCSharp, readValues, setValue } from '../editorconfig';

// Shared with the other IDE implementations, see "Shared .editorconfig fixtures" in docs/architecture.md.
const fixtures = join(__dirname, '..', '..', '..', '..', '..', 'tests', 'fixtures', 'editorconfig');
// Not text files for git (see .gitattributes): they must keep their exact bytes, CRLF included.
const file = (dir: string, name: string) => readFileSync(join(dir, name), 'utf8');

for (const name of readdirSync(fixtures).sort()) {
  const dir = join(fixtures, name);
  const input = file(dir, 'input.editorconfig');
  if (existsSync(join(dir, 'read.json'))) {
    test(`fixture ${name}: read`, () => {
      const { keys, expected } = JSON.parse(file(dir, 'read.json')) as { keys: string[]; expected: Record<string, string> };
      assert.deepEqual(Object.fromEntries(readValues(input, keys)), expected);
    });
  }
  if (existsSync(join(dir, 'change.json'))) {
    test(`fixture ${name}: change`, () => {
      const { key, value } = JSON.parse(file(dir, 'change.json')) as { key: string; value: string };
      assert.equal(setValue(input, key, value), file(dir, 'expected.editorconfig'));
    });
  }
}

test('recognises the common C# globs', () => {
  for (const glob of ['*', '*.cs', '**.cs', '**/*.cs', '*.{cs,vb}', '**']) {
    assert.equal(appliesToCSharp(glob), true, glob);
  }
  for (const glob of ['*.vb', '*.{json,yml}', 'src/**/*.cs', '*.csx']) {
    assert.equal(appliesToCSharp(glob), false, glob);
  }
});
