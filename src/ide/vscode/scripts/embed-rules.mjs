// Embeds /rules/*.yaml into the extension as resources/rules.json (run at build time).
import { mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parse } from 'yaml';

const here = dirname(fileURLToPath(import.meta.url));
const rulesDir = join(here, '..', '..', '..', '..', 'rules');
const target = join(here, '..', 'resources', 'rules.json');

const rules = readdirSync(rulesDir)
  .filter((file) => file.endsWith('.yaml'))
  .sort()
  .map((file) => {
    const rule = parse(readFileSync(join(rulesDir, file), 'utf8'));
    if (!rule?.id || !rule.title || !rule.type || !rule.defaultSeverity) {
      throw new Error(`${file}: id, title, type and defaultSeverity are required`);
    }
    return { ...rule, options: rule.options ?? [] };
  });

mkdirSync(dirname(target), { recursive: true });
writeFileSync(target, JSON.stringify(rules, null, 2) + '\n');
console.log(`Embedded ${rules.length} rule(s) into ${target}`);
