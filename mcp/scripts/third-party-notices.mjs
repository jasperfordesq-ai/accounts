// Builds THIRD_PARTY_NOTICES.txt for the Claude Desktop bundle from the production node_modules it
// redistributes: every package's name, version, declared licence and its licence/notice files.
import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';

const NOTICE_FILE = /^(licen[cs]e|copying|notice)(\.(md|txt|markdown))?$/i;

function packageDirectories(nodeModules) {
  const found = [];
  if (!existsSync(nodeModules)) return found;
  for (const entry of readdirSync(nodeModules)) {
    if (entry.startsWith('.')) continue;
    const full = join(nodeModules, entry);
    if (!statSync(full).isDirectory()) continue;
    if (entry.startsWith('@')) {
      for (const scoped of readdirSync(full)) {
        const scopedDir = join(full, scoped);
        if (statSync(scopedDir).isDirectory()) found.push(scopedDir, ...packageDirectories(join(scopedDir, 'node_modules')));
      }
    } else {
      found.push(full, ...packageDirectories(join(full, 'node_modules')));
    }
  }
  return found;
}

/** Returns the notices text; throws if a bundled package declares no licence. */
export function buildThirdPartyNotices(stageDirectory, productName = 'FilingBridge MCP connector') {
  const seen = new Map();
  for (const directory of packageDirectories(join(stageDirectory, 'node_modules'))) {
    const manifestPath = join(directory, 'package.json');
    if (!existsSync(manifestPath)) continue;
    const pkg = JSON.parse(readFileSync(manifestPath, 'utf8'));
    if (!pkg.name || !pkg.version) continue;
    const key = `${pkg.name}@${pkg.version}`;
    if (seen.has(key)) continue;
    const license = typeof pkg.license === 'string' ? pkg.license
      : Array.isArray(pkg.licenses) ? pkg.licenses.map((item) => item.type ?? item).join(' OR ')
      : pkg.license?.type;
    if (!license) throw new Error(`${key} declares no licence; review it before redistributing.`);
    const texts = readdirSync(directory)
      .filter((file) => NOTICE_FILE.test(file))
      .sort()
      .map((file) => `--- ${file} ---\n${readFileSync(join(directory, file), 'utf8').trim()}`);
    seen.set(key, { key, license, texts, homepage: pkg.homepage ?? pkg.repository?.url ?? pkg.repository ?? '' });
  }
  const entries = [...seen.values()].sort((a, b) => a.key.localeCompare(b.key));
  const header = [
    `Third-party notices for the ${productName}`,
    '',
    'This bundle redistributes the open-source packages listed below. Each remains under its own',
    'licence and copyright; the full licence texts shipped with each package follow its entry.',
    'FilingBridge itself is licensed under the AGPL-3.0-or-later (see LICENSE and NOTICE).',
    '',
    `Packages: ${entries.length}`,
    '',
  ];
  const body = entries.map((entry) => [
    '='.repeat(78),
    `${entry.key}  (${entry.license})`,
    entry.homepage ? String(entry.homepage) : null,
    '',
    entry.texts.length ? entry.texts.join('\n\n') : '(no licence file shipped; licence declared in package.json)',
    '',
  ].filter((line) => line !== null).join('\n'));
  return header.join('\n') + body.join('\n') + '\n';
}
