import assert from 'node:assert/strict';
import { test } from 'node:test';
import { referencesNeolinter } from '../nuget';

test('detects a package reference', () => {
  assert.equal(referencesNeolinter('<ItemGroup><PackageReference Include="Neolinter" Version="0.1.0" PrivateAssets="all" /></ItemGroup>'), true);
  assert.equal(referencesNeolinter("<PackageReference Version='0.1.0' Include='neolinter' />"), true);
});

test('ignores other packages, versions only and comments', () => {
  assert.equal(referencesNeolinter('<PackageReference Include="Neolinter.Cli" />'), false);
  assert.equal(referencesNeolinter('<PackageVersion Include="Neolinter" Version="0.1.0" />'), false);
  assert.equal(referencesNeolinter('<!-- <PackageReference Include="Neolinter" /> -->'), false);
});
