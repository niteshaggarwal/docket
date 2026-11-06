// A small, dependency-free Markdown renderer for Docket's docs. It covers what the docs use:
// headings with GitHub-style ids, paragraphs, nested lists, fenced code, GFM tables, blockquotes and
// GitHub callouts (> [!NOTE]), horizontal rules, inline code, bold, italic, strikethrough, links, images,
// autolinks and a few inline HTML tags. Output is deterministic for the same input.

const esc = (s) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');

/** GitHub's heading slug: lower case, drop punctuation except - and _, spaces become hyphens. */
export function slugify(text) {
  return text.toLowerCase().trim().replace(/<[^>]+>/g, '').replace(/[^\p{L}\p{N}\s_-]/gu, '').replace(/\s/g, '-');
}

/** Plain text of inline Markdown (for slugs, the TOC and search). */
export function plainInline(s) {
  return s
    .replace(/!\[([^\]]*)\]\([^)]*\)/g, '$1')
    .replace(/\[([^\]]*)\]\([^)]*\)/g, '$1')
    .replace(/`([^`]*)`/g, '$1')
    .replace(/\*\*([^*]+)\*\*/g, '$1')
    .replace(/(^|[^\w*])\*([^*]+)\*/g, '$1$2')
    .replace(/(^|\W)_([^_]+)_(?=\W|$)/g, '$1$2')
    .replace(/~~([^~]+)~~/g, '$1')
    .replace(/<[^>]+>/g, '')
    .replace(/\\([\\`*_{}[\]()#+\-.!|>~])/g, '$1');
}

const INLINE_TAGS = /^<\/?(kbd|br|sup|sub|small|mark|span|b|i|em|strong|code|abbr)\b[^>]*>/i;
const BLOCK_TAGS = /^<\/?(p|div|img|details|summary|table|section|figure|picture|video|h[1-6]|ul|ol|br|hr)\b/i;

export class Renderer {
  /**
   * @param {object} opts
   * @param {(href: string, kind: 'link'|'image') => string} opts.link  rewrites hrefs and image sources
   */
  constructor(opts = {}) {
    this.link = opts.link ?? ((h) => h);
    this.headings = []; // { level, text, id }
    this.ids = new Map();
  }

  uniqueId(text) {
    const base = slugify(text) || 'section';
    const n = this.ids.get(base) ?? 0;
    this.ids.set(base, n + 1);
    return n === 0 ? base : `${base}-${n}`;
  }

  // ---------------------------------------------------------------- inline
  inline(src) {
    const keep = [];
    const hold = (html) => `\u0000${keep.push(html) - 1}\u0000`;
    let s = src;
    // backslash escapes
    s = s.replace(/\\([\\`*_{}[\]()#+\-.!|>~<])/g, (_, c) => hold(esc(c)));
    // code spans
    s = s.replace(/(`+)([\s\S]*?[^`])\1(?!`)/g, (_, _t, code) => hold(`<code>${esc(code.replace(/^ (.*) $/, '$1'))}</code>`));
    // autolinks <https://...>
    s = s.replace(/<(https?:\/\/[^\s>]+)>/g, (_, u) => hold(`<a href="${esc(this.link(u, 'link'))}">${esc(u)}</a>`));
    // allowed inline html
    s = s.replace(/<\/?[a-z][^>]*>/gi, (m) => (INLINE_TAGS.test(m) ? hold(m) : m));
    // images
    s = s.replace(/!\[([^\]]*)\]\(\s*([^)\s]+)(?:\s+"([^"]*)")?\s*\)/g, (_, alt, href, title) =>
      hold(`<img src="${esc(this.link(href, 'image'))}" alt="${esc(alt)}"${title ? ` title="${esc(title)}"` : ''} loading="lazy">`));
    // links (text may contain held code spans or images)
    s = s.replace(/\[((?:[^\]\[]|\[[^\]]*\])*)\]\(\s*([^)\s]+)(?:\s+"([^"]*)")?\s*\)/g, (_, text, href, title) => {
      const url = this.link(href, 'link');
      const ext = /^https?:\/\//.test(url);
      return hold(`<a href="${esc(url)}"${title ? ` title="${esc(title)}"` : ''}${ext ? ' target="_blank" rel="noopener"' : ''}>${this.emphasis(esc(text))}</a>`);
    });
    s = this.emphasis(esc(s));
    // restore (repeat: held items may contain other held items)
    for (let i = 0; i < 4 && s.includes('\u0000'); i++) s = s.replace(/\u0000(\d+)\u0000/g, (_, n) => keep[+n]);
    return s;
  }

  emphasis(s) {
    return s
      .replace(/\*\*(?=\S)([\s\S]*?\S)\*\*/g, '<strong>$1</strong>')
      .replace(/__(?=\S)([\s\S]*?\S)__/g, '<strong>$1</strong>')
      .replace(/(^|[^\w*])\*(?=[^\s*])([^*]*?[^\s*])\*(?!\w)/g, '$1<em>$2</em>')
      .replace(/(^|[^\w])_(?=\S)([^_]*?\S)_(?!\w)/g, '$1<em>$2</em>')
      .replace(/~~(?=\S)([\s\S]*?\S)~~/g, '<del>$1</del>');
  }

  // ---------------------------------------------------------------- blocks
  render(md) {
    const lines = md.replace(/\r\n?/g, '\n').replace(/\t/g, '    ').split('\n');
    return this.blocks(lines, false);
  }

  blocks(lines, tight) {
    const out = [];
    let i = 0;
    const isBlank = (l) => l === undefined || /^\s*$/.test(l);
    const listRx = /^( {0,3})([-*+]|\d{1,9}[.)])( +)(.*)$/;
    const tableSep = /^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$/;
    const startsBlock = (l, next) =>
      /^ {0,3}(`{3,}|~{3,})/.test(l) || /^ {0,3}#{1,6}(\s|$)/.test(l) || /^ {0,3}>/.test(l) ||
      /^ {0,3}([-*_])( *\1){2,} *$/.test(l) || listRx.test(l) || (l.includes('|') && next !== undefined && tableSep.test(next)) ||
      BLOCK_TAGS.test(l.trim());

    while (i < lines.length) {
      const line = lines[i];
      if (isBlank(line)) { i++; continue; }

      // fenced code
      let m = /^( {0,3})(`{3,}|~{3,})\s*([\w+#.-]*)[^\n]*$/.exec(line);
      if (m) {
        const fence = m[2], indent = m[1].length, lang = m[3];
        const body = [];
        i++;
        while (i < lines.length && !new RegExp(`^ {0,3}${fence[0]}{${fence.length},}\\s*$`).test(lines[i])) {
          body.push(lines[i].slice(Math.min(indent, lines[i].search(/\S|$/))));
          i++;
        }
        i++;
        out.push(this.code(body.join('\n'), lang));
        continue;
      }

      // heading
      m = /^ {0,3}(#{1,6})\s+(.*?)\s*#*\s*$/.exec(line);
      if (m) {
        const level = m[1].length, text = m[2];
        const id = this.uniqueId(plainInline(text));
        this.headings.push({ level, text: plainInline(text), id });
        const inner = this.inline(text);
        out.push(level === 1
          ? `<h1 id="${id}">${inner}</h1>`
          : `<h${level} id="${id}"><a class="anchor" href="#${id}" aria-label="Link to this section">#</a>${inner}</h${level}>`);
        i++;
        continue;
      }

      // horizontal rule
      if (/^ {0,3}([-*_])( *\1){2,} *$/.test(line)) { out.push('<hr>'); i++; continue; }

      // blockquote / callout
      if (/^ {0,3}>/.test(line)) {
        const body = [];
        while (i < lines.length && !isBlank(lines[i]) && (/^ {0,3}>/.test(lines[i]) || body.length)) {
          if (!/^ {0,3}>/.test(lines[i]) && startsBlock(lines[i], lines[i + 1])) break;
          body.push(lines[i].replace(/^ {0,3}> ?/, ''));
          i++;
        }
        const c = /^\[!(NOTE|TIP|IMPORTANT|WARNING|CAUTION)\]\s*(.*)$/i.exec(body[0] ?? '');
        if (c) {
          const kind = c[1].toLowerCase();
          const label = { note: 'Note', tip: 'Tip', important: 'Important', warning: 'Warning', caution: 'Caution' }[kind];
          const rest = (c[2] ? [c[2]] : []).concat(body.slice(1));
          out.push(`<div class="callout callout-${kind}"><p class="callout-title">${label}</p>${this.blocks(rest, false)}</div>`);
        } else {
          out.push(`<blockquote>${this.blocks(body, false)}</blockquote>`);
        }
        continue;
      }

      // table
      if (line.includes('|') && tableSep.test(lines[i + 1] ?? '')) {
        const cells = (l) => {
          let t = l.trim();
          if (t.startsWith('|')) t = t.slice(1);
          if (t.endsWith('|') && !t.endsWith('\\|')) t = t.slice(0, -1);
          const res = []; let cur = ''; let code = false;
          for (let k = 0; k < t.length; k++) {
            const ch = t[k];
            if (ch === '\\' && t[k + 1] === '|') { cur += '\\|'; k++; continue; }
            if (ch === '`') code = !code;
            if (ch === '|' && !code) { res.push(cur.trim()); cur = ''; continue; }
            cur += ch;
          }
          res.push(cur.trim());
          return res;
        };
        const head = cells(line);
        const align = cells(lines[i + 1]).map((c) => (/^:-+:$/.test(c) ? 'center' : /^-+:$/.test(c) ? 'right' : /^:-+$/.test(c) ? 'left' : ''));
        i += 2;
        const rows = [];
        while (i < lines.length && !isBlank(lines[i]) && lines[i].includes('|')) { rows.push(cells(lines[i])); i++; }
        const td = (tag, c, k) => `<${tag}${align[k] ? ` style="text-align:${align[k]}"` : ''}>${this.inline(c.replace(/\\\|/g, '|'))}</${tag}>`;
        out.push(`<div class="table-wrap"><table><thead><tr>${head.map((c, k) => td('th', c, k)).join('')}</tr></thead><tbody>` +
          rows.map((r) => `<tr>${head.map((_, k) => td('td', r[k] ?? '', k)).join('')}</tr>`).join('') + '</tbody></table></div>');
        continue;
      }

      // list
      m = listRx.exec(line);
      if (m) {
        const ordered = /\d/.test(m[2]);
        const start = ordered ? parseInt(m[2], 10) : 1;
        const items = [];
        let loose = false;
        while (i < lines.length) {
          const lm = listRx.exec(lines[i]);
          if (!lm || /\d/.test(lm[2]) !== ordered) break;
          const contentIndent = lm[1].length + lm[2].length + lm[3].length;
          const body = [lm[4]];
          i++;
          let sawBlank = false;
          while (i < lines.length) {
            const l = lines[i];
            if (isBlank(l)) { sawBlank = true; body.push(''); i++; continue; }
            const ind = l.search(/\S/);
            if (ind >= contentIndent || (ind >= 2 && listRx.test(l) && ind > lm[1].length)) {
              body.push(l.slice(Math.min(ind, contentIndent)));
              sawBlank = false; i++; continue;
            }
            if (!sawBlank && !startsBlock(l, lines[i + 1])) { body.push(l.trim()); i++; continue; } // lazy continuation
            break;
          }
          let trailing = 0;
          while (body.length && body[body.length - 1] === '') { body.pop(); trailing++; }
          if (trailing && i < lines.length && listRx.test(lines[i])) loose = true; // blank line between items
          if (body.includes('')) loose = true; // blank line inside an item
          items.push(body);
        }
        const tag = ordered ? 'ol' : 'ul';
        const attr = ordered && start !== 1 ? ` start="${start}"` : '';
        out.push(`<${tag}${attr}>${items.map((b) => {
          let check = '';
          const cm = /^\[( |x|X)\]\s+(.*)$/.exec(b[0]);
          if (cm) { check = `<input type="checkbox" disabled${cm[1] !== ' ' ? ' checked' : ''}> `; b = [cm[2], ...b.slice(1)]; }
          return `<li${check ? ' class="task-item"' : ''}>${check}${this.blocks(b, !loose)}</li>`;
        }).join('')}</${tag}>`);
        continue;
      }

      // html block
      if (BLOCK_TAGS.test(line.trim())) {
        const body = [];
        while (i < lines.length && !isBlank(lines[i])) { body.push(lines[i]); i++; }
        out.push(body.join('\n').replace(/\s(href|src)="([^"]+)"/g, (_, attr, url) => ` ${attr}="${esc(this.link(url.replace(/&amp;/g, '&'), attr === 'src' ? 'image' : 'link'))}"`));
        continue;
      }

      // paragraph
      const para = [line.trim()];
      i++;
      while (i < lines.length && !isBlank(lines[i]) && !startsBlock(lines[i], lines[i + 1])) { para.push(lines[i].trim()); i++; }
      const text = para.join('\n').replace(/ {2,}\n/g, '<br>\n');
      const html = this.inline(text).replace(/&lt;br&gt;/g, '<br>');
      out.push(tight ? html : `<p>${html}</p>`);
    }
    return out.join(tight ? '\n' : '\n');
  }

  code(body, lang) {
    const label = { sh: 'Shell', bash: 'Shell', powershell: 'PowerShell', ps1: 'PowerShell', json: 'JSON', python: 'Python', text: 'Text', yaml: 'YAML', md: 'Markdown', markdown: 'Markdown', http: 'HTTP', js: 'JavaScript', csharp: 'C#' }[lang.toLowerCase()] ?? (lang || 'Text');
    return `<div class="code"><div class="code-head"><span>${esc(label)}</span><button type="button" class="copy" aria-label="Copy code">Copy</button></div><pre><code${lang ? ` class="language-${esc(lang)}"` : ''}>${esc(body)}</code></pre></div>`;
  }
}
