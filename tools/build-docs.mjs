#!/usr/bin/env node
// Builds the Docket documentation website from the Markdown in docs/ (and CHANGELOG.md).
//
//   node tools/build-docs.mjs            regenerate site/docs/, the skill zip and the synced blocks
//   node tools/build-docs.mjs --check    also verify that every internal link and #anchor resolves
//
// No dependencies beyond Node 18+. The output is plain static files: site/ is deployed as-is.
// It also keeps a few Markdown blocks in sync with the code, between <!-- sync:NAME --> and <!-- /sync:NAME -->:
//   tools   the AI tool reference, read from src/Docket.Mcp/*Tools.cs
//   cli     the `docket --help` text, read from src/Docket.Cli/Program.cs
// and packages skills/docket/ as site/downloads/docket-skill.zip.
import fs from 'node:fs';
import path from 'node:path';
import zlib from 'node:zlib';
import { fileURLToPath } from 'node:url';
import { Renderer, plainInline } from './docs/markdown.mjs';
import { NAV, LABELS } from './docs/nav.mjs';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const DOCS = path.join(ROOT, 'docs');
const OUT = path.join(ROOT, 'site', 'docs');
const REPO = 'https://github.com/niteshaggarwal/docket';
const ASSET_SRC = path.join(ROOT, 'tools', 'docs');
const posix = (p) => p.split(path.sep).join('/');
const rel = (p) => posix(path.relative(ROOT, p));
const read = (p) => fs.readFileSync(p, 'utf8').replace(/^﻿/, '').replace(/\r\n?/g, '\n');
const write = (p, s) => { fs.mkdirSync(path.dirname(p), { recursive: true }); fs.writeFileSync(p, s); };
const warnings = [];
const warn = (m) => warnings.push(m);

// ------------------------------------------------------------------ synced blocks
function csString(lit, consts) {
  // lit is either "..." or NAME + "..."
  return lit.split(/\s*\+\s*/).map((part) => {
    if (part.startsWith('"')) return part.slice(1, -1).replace(/\\"/g, '"').replace(/\\\\/g, '\\');
    if (consts[part] === undefined) throw new Error(`Unknown constant ${part} in a tool description`);
    return consts[part];
  }).join('');
}

function readTools() {
  const dir = path.join(ROOT, 'src', 'Docket.Mcp');
  const files = ['TaskTools.cs', 'ToDoTools.cs', 'TeamTools.cs'];
  const consts = {};
  const groups = [];
  for (const f of files) {
    const src = read(path.join(dir, f));
    for (const m of src.matchAll(/const string (\w+) = ("(?:[^"\\]|\\.)*");/g)) consts[m[1]] = csString(m[2], consts);
  }
  const titles = { 'TaskTools.cs': 'Tasks, lists and boards', 'ToDoTools.cs': 'My Day, stars, steps, repeats and reminders', 'TeamTools.cs': 'Subtasks, people and the overview' };
  for (const f of files) {
    const src = read(path.join(dir, f));
    const tools = [];
    const rx = /\[McpServerTool\(([^)]*)\)\]\s*\[Description\(("(?:[^"\\]|\\.)*")\)\]\s*public static [^(]+?\s(\w+)\(([\s\S]*?)\)\s*(?:=>|\{)/g;
    for (const m of src.matchAll(rx)) {
      const attr = m[1];
      const name = /Name\s*=\s*"([^"]+)"/.exec(attr)[1];
      const readOnly = /ReadOnly\s*=\s*true/.test(attr);
      const params = [];
      const prx = /\[Description\(((?:\w+\s*\+\s*)?"(?:[^"\\]|\\.)*")\)\]\s*([\w[\]?<>.]+)\s+(\w+)(?:\s*=\s*([^,]+?))?\s*(?=,|$)/g;
      for (const p of m[4].matchAll(prx)) {
        const t = p[2].replace('?', '');
        const type = { long: 'integer', int: 'integer', string: 'text', bool: 'true/false', 'long[]': 'list of ids', 'string[]': 'list of text' }[t] ?? t;
        params.push({ name: p[3], type, required: p[4] === undefined, def: p[4]?.trim(), desc: csString(p[1], consts) });
      }
      tools.push({ name, readOnly, desc: csString(m[2], consts), params });
    }
    groups.push({ title: titles[f], tools });
  }
  const count = groups.reduce((n, g) => n + g.tools.length, 0);
  if (count < 20) throw new Error(`Only found ${count} tools in src/Docket.Mcp; the parser needs updating.`);
  return { groups, count };
}

const cell = (s) => s.replace(/\|/g, '\\|').replace(/\n/g, ' ');

function toolsMarkdown({ groups, count }) {
  const all = groups.flatMap((g) => g.tools);
  const out = [`Docket's connector has **${count} tools**: ${all.filter((t) => t.readOnly).length} only read, and ${all.filter((t) => !t.readOnly).length} change tasks. None delete or share.`, ''];
  out.push('| Tool | Changes data | In short |', '|---|---|---|');
  for (const t of all) out.push(`| [\`${t.name}\`](#${t.name}) | ${t.readOnly ? 'no' : 'yes'} | ${cell(t.desc.split(/(?<=\.)\s/)[0])} |`);
  out.push('');
  for (const g of groups) {
    out.push(`## ${g.title}`, '');
    for (const t of g.tools) {
      out.push(`### ${t.name}`, '', t.desc, '', t.readOnly ? '*Read-only.*' : '*Changes data.*', '');
      if (t.params.length === 0) out.push('No arguments.', '');
      else {
        for (const p of t.params) {
          const def = p.def && p.def !== 'null' && !/default/i.test(p.desc) ? ` Default: \`${p.def.replace(/^"|"$/g, '')}\`.` : '';
          out.push(`- ${p.required ? `**\`${p.name}\`** (${p.type}, required)` : `\`${p.name}\` (${p.type})`}: ${p.desc}${def}`);
        }
        out.push('');
      }
    }
  }
  out.push('This reference is generated from `src/Docket.Mcp` by `tools/build-docs.mjs`, so it always matches the connector.');
  return out.join('\n');
}

function skillToolsMarkdown({ groups, count }) {
  const out = ['# Docket connector tools', '', `The ${count} tools of the Docket connector (\`docket-mcp\`), generated from its source. Required arguments are marked *required*.`,
    'Results are readable text with task ids written as `#123`. No tool deletes or shares anything.', ''];
  for (const g of groups) {
    out.push(`## ${g.title}`, '');
    for (const t of g.tools) {
      out.push(`### ${t.name}${t.readOnly ? ' (read-only)' : ''}`, '', t.desc, '');
      for (const p of t.params) out.push(`- \`${p.name}\` (${p.type}${p.required ? ', required' : ''}): ${p.desc}${p.def && p.def !== 'null' && !p.desc.includes('Default') ? ` Default ${p.def}.` : ''}`);
      if (t.params.length) out.push('');
    }
  }
  return out.join('\n').trimEnd() + '\n';
}

function cliHelp() {
  const src = read(path.join(ROOT, 'src', 'Docket.Cli', 'Program.cs'));
  const m = /const string Help = """\n([\s\S]*?)\n\s*""";/.exec(src);
  if (!m) throw new Error('Could not find the CLI help text in src/Docket.Cli/Program.cs');
  const lines = m[1].split('\n');
  const indent = Math.min(...lines.filter((l) => l.trim()).map((l) => l.search(/\S/)));
  return '```text\n' + lines.map((l) => l.slice(indent)).join('\n').trimEnd() + '\n```';
}

function sync(file, name, body) {
  const p = path.join(ROOT, file);
  if (!fs.existsSync(p)) return;
  const s = read(p);
  const rx = new RegExp(`(<!-- sync:${name} -->)[\\s\\S]*?(<!-- /sync:${name} -->)`);
  if (!rx.test(s)) { warn(`${file}: no <!-- sync:${name} --> block`); return; }
  const next = s.replace(rx, `$1\n${body}\n$2`);
  if (next !== s) { write(p, next); console.log(`synced ${name} in ${file}`); }
}

// ------------------------------------------------------------------ pages
function walk(dir) {
  return fs.readdirSync(dir, { withFileTypes: true }).sort((a, b) => a.name.localeCompare(b.name)).flatMap((e) => {
    const p = path.join(dir, e.name);
    return e.isDirectory() ? walk(p) : [p];
  });
}

const outName = (navPath) => {
  if (navPath === '../CHANGELOG.md') return 'changelog.html';
  return navPath.replace(/(^|\/)README\.md$/, '$1index.html').replace(/\.md$/, '.html');
};

function buildPages() {
  const pages = [];
  const byRepoPath = new Map();
  for (const section of NAV) {
    for (const item of section.items) {
      if (typeof item !== 'string') continue; // a link to a site page, e.g. { label, site: 'pro.html' }
      const src = path.resolve(DOCS, item);
      if (!fs.existsSync(src)) { warn(`nav: ${item} does not exist`); continue; }
      const page = { item, section: section.title, src, repoPath: rel(src), out: path.join(OUT, outName(item)), label: LABELS[item] };
      pages.push(page);
      byRepoPath.set(page.repoPath, page);
    }
  }
  for (const f of walk(DOCS).filter((f) => f.endsWith('.md'))) if (!byRepoPath.has(rel(f))) warn(`${rel(f)} is not in tools/docs/nav.mjs, so it isn't on the site`);
  return { pages, byRepoPath };
}

function relFrom(fromFile, toFile) {
  const r = posix(path.relative(path.dirname(fromFile), toFile));
  return r || path.basename(toFile);
}

function makeLinker(page, byRepoPath, assets) {
  return (href, kind) => {
    if (/^(https?:|mailto:|tel:|#|data:)/.test(href)) return href;
    const [p, hash = ''] = href.split(/(?=#)/);
    const target = path.resolve(path.dirname(page.src), decodeURIComponent(p));
    const repoPath = rel(target);
    if (repoPath.startsWith('..')) { warn(`${page.repoPath}: link outside the repo: ${href}`); return href; }
    const linked = byRepoPath.get(repoPath);
    if (linked) return relFrom(page.out, linked.out) + hash;
    if (repoPath.startsWith('site/')) return relFrom(page.out, target) + hash;
    if (repoPath.startsWith('docs/') && fs.existsSync(target) && fs.statSync(target).isFile() && !repoPath.endsWith('.md')) {
      const dest = path.join(OUT, path.relative(DOCS, target));
      assets.set(dest, target);
      return relFrom(page.out, dest) + hash;
    }
    if (!fs.existsSync(target)) warn(`${page.repoPath}: broken link ${href}`);
    const isDir = fs.existsSync(target) && fs.statSync(target).isDirectory();
    if (kind === 'image') return `https://raw.githubusercontent.com/niteshaggarwal/docket/main/${repoPath}`;
    return `${REPO}/${isDir ? 'tree' : 'blob'}/main/${repoPath}${hash}`;
  };
}

// ------------------------------------------------------------------ layout
const ICONS = `<svg width="0" height="0" style="position:absolute" aria-hidden="true">
  <symbol id="i-win" viewBox="0 0 16 16"><path fill="currentColor" d="M1 2.6 6.6 1.8v5.4H1zM7.4 1.7 15 .6v6.6H7.4zM1 8h5.6v5.4L1 12.6zm6.4 0H15v6.6l-7.6-1.1z"/></symbol>
  <symbol id="i-sun" viewBox="0 0 24 24"><g fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"><circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"/></g></symbol>
  <symbol id="i-moon" viewBox="0 0 24 24"><path fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round" d="M20 14.5A8 8 0 0 1 9.5 4 8 8 0 1 0 20 14.5z"/></symbol>
  <symbol id="i-menu" viewBox="0 0 24 24"><path fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" d="M4 7h16M4 12h16M4 17h16"/></symbol>
  <symbol id="i-search" viewBox="0 0 24 24"><g fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"><circle cx="11" cy="11" r="6.5"/><path d="m16 16 4.5 4.5"/></g></symbol>
  <symbol id="i-edit" viewBox="0 0 24 24"><path fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" d="M4 20h4L19 9l-4-4L4 16zM14 6l4 4"/></symbol>
  <symbol id="i-left" viewBox="0 0 24 24"><path fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" d="m14 6-6 6 6 6"/></symbol>
  <symbol id="i-right" viewBox="0 0 24 24"><path fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" d="m10 6 6 6-6 6"/></symbol>
</svg>`;

const escAttr = (s) => s.replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;');

function layout({ page, title, description, html, toc, pages, prev, next }) {
  const docsRoot = relFrom(page.out, path.join(OUT, 'index.html')).replace(/index\.html$/, '') || './';
  const site = docsRoot + '../';
  const link = (p) => relFrom(page.out, p.out);
  const sidebar = NAV.map((s) => {
    const items = s.items.map((it) => typeof it === 'string' ? pages.find((p) => p.item === it) : it).filter(Boolean);
    return `<div class="side-group"><p class="side-title">${s.title}</p>${items.map((p) => p.site
      ? `<a href="${site}${p.site}">${escAttr(p.label)}</a>`
      : `<a href="${link(p)}"${p === page ? ' class="on" aria-current="page"' : ''}>${escAttr(p.label ?? p.title)}</a>`).join('')}</div>`;
  }).join('');
  const tocHtml = toc.length < 2 ? '' : `<nav class="toc" aria-label="On this page"><p class="toc-title">On this page</p>${toc.map((h) =>
    `<a href="#${h.id}" class="l${h.level}">${escAttr(h.text)}</a>`).join('')}</nav>`;
  const pn = (p, dir) => p ? `<a class="pn ${dir}" href="${link(p)}"><span>${dir === 'prev' ? 'Previous' : 'Next'}</span><b>${dir === 'prev' ? '<svg><use href="#i-left"/></svg>' : ''}${escAttr(p.label ?? p.title)}${dir === 'next' ? '<svg><use href="#i-right"/></svg>' : ''}</b></a>` : '<span></span>';
  const edit = page.item === '../CHANGELOG.md' ? 'CHANGELOG.md' : page.repoPath;
  return `<!doctype html>
<html lang="en" data-base="${docsRoot}">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<title>${escAttr(title)} · Docket docs</title>
<meta name="description" content="${escAttr(description)}">
<link rel="icon" href="${site}assets/docket-64.png">
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&family=JetBrains+Mono:wght@400;500&display=swap">
<link rel="stylesheet" href="${docsRoot}assets/docs.css">
<script>try { var t = localStorage.getItem('docket-theme'); if (t) document.documentElement.dataset.theme = t; } catch (e) {}</script>
</head>
<body>
${ICONS}
<a class="skip" href="#content">Skip to content</a>
<div class="nav-wrap"><div class="container wide">
  <nav class="nav" aria-label="Main">
    <button class="icon-btn menu-btn" type="button" aria-label="Open the docs menu" aria-controls="sidebar" aria-expanded="false"><svg><use href="#i-menu"/></svg></button>
    <a class="logo" href="${site}index.html"><img src="${site}assets/docket-64.png" alt="">Docket <span class="logo-sub">Docs</span></a>
    <div class="nav-links">
      <a href="${site}index.html#features">Features</a><a href="${site}index.html#ai">AI</a><a href="${site}index.html#mobile">Mobile</a><a href="${site}index.html#self-host">Self-host</a>
      <a href="${docsRoot}index.html" class="on">Docs</a><a href="${site}pro.html">Pro</a><a href="${docsRoot}changelog.html">Changelog</a>
    </div>
    <div class="nav-right">
      <a class="gh-link" href="${REPO}" target="_blank" rel="noopener">GitHub</a>
      <button class="icon-btn" id="theme" type="button" aria-label="Switch between light and dark"><svg class="theme-sun"><use href="#i-sun"/></svg><svg class="theme-moon"><use href="#i-moon"/></svg></button>
      <a class="btn" href="${REPO}/releases/latest" target="_blank" rel="noopener"><svg><use href="#i-win"/></svg><span>Download</span></a>
    </div>
  </nav>
</div></div>

<div class="container wide docs">
  <aside class="sidebar" id="sidebar" aria-label="Documentation">
    <label class="search"><svg><use href="#i-search"/></svg><input type="search" id="search" placeholder="Search the docs" autocomplete="off" aria-label="Search the docs"><kbd>/</kbd></label>
    <div class="search-results" id="results" hidden></div>
    <div class="side-site"><a href="${site}index.html">Docket home</a><a href="${REPO}" target="_blank" rel="noopener">GitHub</a></div>
    ${sidebar}
  </aside>
  <div class="scrim" hidden></div>
  <main class="content" id="content">
    <article class="prose">
      <p class="crumb">${escAttr(page.section)}</p>
      ${html}
    </article>
    <div class="page-foot">
      <a class="edit" href="${REPO}/blob/main/${edit}" target="_blank" rel="noopener"><svg><use href="#i-edit"/></svg>Edit this page on GitHub</a>
      <div class="pns">${pn(prev, 'prev')}${pn(next, 'next')}</div>
    </div>
  </main>
  ${tocHtml || '<div class="toc-empty"></div>'}
</div>

<footer class="foot">
  <div class="container wide foot-row">
    <a class="logo" href="${site}index.html"><img src="${site}assets/docket-64.png" alt="">Docket</a>
    <div class="foot-links"><a href="${docsRoot}index.html">Docs</a><a href="${site}pro.html">Pro and roadmap</a><a href="${docsRoot}changelog.html">Changelog</a><a href="${REPO}/releases" target="_blank" rel="noopener">Releases</a><a href="${REPO}/discussions" target="_blank" rel="noopener">Discussions</a><a href="${REPO}/issues" target="_blank" rel="noopener">Report a bug</a><a href="${REPO}/blob/main/LICENSE" target="_blank" rel="noopener">AGPL-3.0</a></div>
    <span class="foot-copy">© 2026 Quartifex · Built on Vikunja</span>
  </div>
</footer>
<script src="${docsRoot}search-index.js" defer></script>
<script src="${docsRoot}assets/docs.js" defer></script>
</body>
</html>
`;
}

// ------------------------------------------------------------------ zip (deterministic, deflate)
const CRC = (() => { const t = new Uint32Array(256); for (let n = 0; n < 256; n++) { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; t[n] = c >>> 0; } return t; })();
const crc32 = (buf) => { let c = 0xffffffff; for (const b of buf) c = CRC[(c ^ b) & 0xff] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; };

function zip(entries) {
  const locals = []; const centrals = []; let offset = 0;
  const DATE = 0x0021, TIME = 0x0000; // 1980-01-01 00:00, so the zip is the same on every build
  for (const { name, data } of entries) {
    const nameBuf = Buffer.from(name, 'utf8');
    const comp = zlib.deflateRawSync(data, { level: 9 });
    const crc = crc32(data);
    const h = Buffer.alloc(30);
    h.writeUInt32LE(0x04034b50, 0); h.writeUInt16LE(20, 4); h.writeUInt16LE(0x0800, 6); h.writeUInt16LE(8, 8);
    h.writeUInt16LE(TIME, 10); h.writeUInt16LE(DATE, 12); h.writeUInt32LE(crc, 14); h.writeUInt32LE(comp.length, 18);
    h.writeUInt32LE(data.length, 22); h.writeUInt16LE(nameBuf.length, 26); h.writeUInt16LE(0, 28);
    locals.push(h, nameBuf, comp);
    const c = Buffer.alloc(46);
    c.writeUInt32LE(0x02014b50, 0); c.writeUInt16LE(0x031e, 4); c.writeUInt16LE(20, 6); c.writeUInt16LE(0x0800, 8); c.writeUInt16LE(8, 10);
    c.writeUInt16LE(TIME, 12); c.writeUInt16LE(DATE, 14); c.writeUInt32LE(crc, 16); c.writeUInt32LE(comp.length, 20); c.writeUInt32LE(data.length, 24);
    c.writeUInt16LE(nameBuf.length, 28); c.writeUInt32LE((0o100644 << 16) >>> 0, 38); c.writeUInt32LE(offset, 42);
    centrals.push(c, nameBuf);
    offset += 30 + nameBuf.length + comp.length;
  }
  const central = Buffer.concat(centrals);
  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0); end.writeUInt16LE(entries.length, 8); end.writeUInt16LE(entries.length, 10);
  end.writeUInt32LE(central.length, 12); end.writeUInt32LE(offset, 16);
  return Buffer.concat([...locals, central, end]);
}

function packageSkill() {
  const dir = path.join(ROOT, 'skills', 'docket');
  if (!fs.existsSync(path.join(dir, 'SKILL.md'))) { warn('skills/docket/SKILL.md is missing; no skill zip made'); return; }
  const files = walk(dir).filter((f) => !path.basename(f).startsWith('.'));
  const entries = files.map((f) => ({ name: 'docket/' + posix(path.relative(dir, f)), data: Buffer.from(read(f), 'utf8') }));
  write(path.join(ROOT, 'site', 'downloads', 'docket-skill.zip'), zip(entries));
  console.log(`packaged ${entries.length} skill files into site/downloads/docket-skill.zip`);
}

// ------------------------------------------------------------------ link check
function checkLinks() {
  const htmlFiles = walk(path.join(ROOT, 'site')).filter((f) => f.endsWith('.html'));
  const ids = new Map();
  const idsOf = (f) => {
    if (!ids.has(f)) ids.set(f, new Set([...read(f).matchAll(/\sid="([^"]+)"/g)].map((m) => m[1])));
    return ids.get(f);
  };
  let bad = 0, checked = 0;
  for (const f of htmlFiles) {
    const s = read(f).replace(/<script[\s\S]*?<\/script>/g, '');
    for (const m of s.matchAll(/\s(?:href|src)="([^"]+)"/g)) {
      const href = m[1].replace(/&amp;/g, '&');
      if (/^(https?:|mailto:|data:|tel:|javascript:)/.test(href)) continue;
      checked++;
      const [p, hash] = href.split('#');
      const target = p ? path.resolve(path.dirname(f), decodeURIComponent(p)) : f;
      if (!fs.existsSync(target) || fs.statSync(target).isDirectory() && !fs.existsSync(path.join(target, 'index.html'))) { console.log(`BROKEN ${rel(f)} -> ${href}`); bad++; continue; }
      if (hash && target.endsWith('.html') && !idsOf(target).has(decodeURIComponent(hash))) { console.log(`BROKEN ANCHOR ${rel(f)} -> ${href}`); bad++; }
    }
  }
  console.log(`link check: ${checked} internal links, ${bad} broken`);
  return bad;
}

// ------------------------------------------------------------------ main
function main() {
  const tools = readTools();
  sync('docs/ai/tools.md', 'tools', toolsMarkdown(tools));
  sync('docs/using/cli.md', 'cli', cliHelp());
  write(path.join(ROOT, 'skills', 'docket', 'reference', 'tools.md'),
    '<!-- Generated by tools/build-docs.mjs from src/Docket.Mcp. Do not edit by hand. -->\n' + skillToolsMarkdown(tools));

  const { pages, byRepoPath } = buildPages();
  const assets = new Map();
  // first pass: titles
  for (const p of pages) {
    const md = read(p.src);
    const h1 = /^#\s+(.+)$/m.exec(md);
    p.md = md;
    p.title = h1 ? plainInline(h1[1]) : path.basename(p.src, '.md');
  }
  fs.rmSync(OUT, { recursive: true, force: true });
  const search = [];
  pages.forEach((p, i) => {
    const r = new Renderer({ link: makeLinker(p, byRepoPath, assets) });
    const html = r.render(p.md);
    const firstPara = /<p>([\s\S]*?)<\/p>/.exec(html);
    const description = firstPara ? firstPara[1].replace(/<[^>]+>/g, '').replace(/\s+/g, ' ').replace(/&amp;/g, '&').replace(/&quot;/g, '"').replace(/&lt;/g, '<').replace(/&gt;/g, '>').slice(0, 160) : p.title;
    const toc = r.headings.filter((h) => h.level === 2 || h.level === 3);
    write(p.out, layout({ page: p, title: p.title, description, html, toc, pages, prev: pages[i - 1], next: pages[i + 1] }));
    const text = html.replace(/<div class="code-head">[\s\S]*?<\/div>/g, ' ').replace(/<[^>]+>/g, ' ').replace(/&[a-z]+;|&#\d+;/g, ' ').replace(/\s+/g, ' ').trim();
    search.push({ t: p.label ?? p.title, s: p.section, u: posix(path.relative(OUT, p.out)), h: r.headings.filter((h) => h.level > 1 && h.level < 4).map((h) => [h.text, h.id]), x: text.slice(0, 6000) });
  });
  write(path.join(OUT, 'search-index.js'), `window.DOCKET_SEARCH=${JSON.stringify(search)};\n`);
  // every non-Markdown file under docs/ (screenshots) is copied with the same layout, so relative paths in raw HTML work too
  for (const f of walk(DOCS).filter((f) => !f.endsWith('.md'))) assets.set(path.join(OUT, path.relative(DOCS, f)), f);
  for (const [dest, src] of [...assets].sort()) { fs.mkdirSync(path.dirname(dest), { recursive: true }); fs.copyFileSync(src, dest); }
  for (const f of ['docs.css', 'docs.js']) { fs.mkdirSync(path.join(OUT, 'assets'), { recursive: true }); fs.copyFileSync(path.join(ASSET_SRC, f), path.join(OUT, 'assets', f)); }
  packageSkill();
  console.log(`built ${pages.length} pages and ${assets.size} images into site/docs/`);
  for (const w of warnings) console.warn('warning: ' + w);
  if (process.argv.includes('--check')) {
    const bad = checkLinks();
    if (bad || warnings.length) process.exitCode = 1;
  }
}

main();
