import assert from 'node:assert/strict';
import { test } from 'node:test';
import { appliesToCSharp, readValues, setValue } from '../editorconfig';

const key = 'dotnet_diagnostic.NL0001.severity';

test('reads the value from the last section that applies to C#', () => {
  const text = ['root = true', '', '[*]', `${key} = warning`, '', '[*.cs]', `${key} = error`, '', '[*.vb]', `${key} = none`].join('\n');
  assert.equal(readValues(text, [key]).get(key), 'error');
});

test('reads keys case-insensitively and ignores inline comments', () => {
  const text = '[*.{cs,vb}]\nDOTNET_DIAGNOSTIC.nl0001.SEVERITY = silent # quiet\n';
  assert.equal(readValues(text, [key]).get(key), 'silent');
});

test('ignores the preamble and unrelated sections', () => {
  const text = `${key} = error\n[*.md]\n${key} = warning\n`;
  assert.equal(readValues(text, [key]).has(key), false);
});

test('updates the effective line in place and keeps comments', () => {
  const text = ['# Top comment', '[*.cs]', '  ' + key + ' = warning ; keep me', '# trailing', ''].join('\n');
  assert.equal(setValue(text, key, 'error'), ['# Top comment', '[*.cs]', '  ' + key + ' = error ; keep me', '# trailing', ''].join('\n'));
});

test('adds the key at the end of the existing [*.cs] section', () => {
  const text = ['[*.cs]', 'indent_size = 4', '', '[*.json]', 'indent_size = 2', ''].join('\n');
  assert.equal(
    setValue(text, 'nl0001.min_statements', '5'),
    ['[*.cs]', 'indent_size = 4', 'nl0001.min_statements = 5', '', '[*.json]', 'indent_size = 2', ''].join('\n'),
  );
});

test('appends a [*.cs] section when none exists', () => {
  const text = 'root = true\n\n[*]\nindent_style = space\n';
  assert.equal(setValue(text, key, 'none'), `root = true\n\n[*]\nindent_style = space\n\n[*.cs]\n${key} = none\n`);
});

test('creates the content of an empty file', () => {
  assert.equal(setValue('', key, 'warning'), `[*.cs]\n${key} = warning\n`);
});

test('preserves CRLF line endings', () => {
  const text = '[*.cs]\r\nindent_size = 4\r\n';
  assert.equal(setValue(text, key, 'error'), `[*.cs]\r\nindent_size = 4\r\n${key} = error\r\n`);
});

test('recognises the common C# globs', () => {
  for (const glob of ['*', '*.cs', '**.cs', '**/*.cs', '*.{cs,vb}', '**']) {
    assert.equal(appliesToCSharp(glob), true, glob);
  }
  for (const glob of ['*.vb', '*.{json,yml}', 'src/**/*.cs', '*.csx']) {
    assert.equal(appliesToCSharp(glob), false, glob);
  }
});

test('is lossless for everything else', () => {
  const text = ['; header', 'root = true', '', '[*]', 'charset = utf-8', '', '[*.cs]', '# rules', `${key} = suggestion`, ''].join('\n');
  const updated = setValue(text, key, 'warning');
  assert.equal(updated.replace('warning', 'suggestion'), text);
});

test('handles odd spacing and whitespace-heavy lines quickly', () => {
  const text = `[*.cs]\n\t${key}\t=\t\twarning   \t; note\n${' '.repeat(50000)}x${' '.repeat(50000)}\n`;
  assert.equal(readValues(text, [key]).get(key), 'warning');
  assert.equal(setValue(text, key, 'error'), text.replace('\t\twarning', '\t\terror'));
});
