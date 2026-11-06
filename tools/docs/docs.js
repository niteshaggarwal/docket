// Docket docs: theme toggle (shared with the main site), copy buttons, mobile menu, on-page TOC and search.
(() => {
  const root = document.documentElement;
  const $ = (s, r = document) => r.querySelector(s);
  const $$ = (s, r = document) => [...r.querySelectorAll(s)];

  // theme: same key and behaviour as site/index.html
  $('#theme')?.addEventListener('click', () => {
    const dark = root.dataset.theme ? root.dataset.theme === 'dark' : matchMedia('(prefers-color-scheme: dark)').matches;
    root.dataset.theme = dark ? 'light' : 'dark';
    try { localStorage.setItem('docket-theme', root.dataset.theme); } catch (e) {}
  });

  // copy buttons
  $$('.code .copy').forEach((b) => b.addEventListener('click', async () => {
    const text = b.closest('.code').querySelector('code').innerText;
    try { await navigator.clipboard.writeText(text); }
    catch (e) { const t = document.createElement('textarea'); t.value = text; document.body.appendChild(t); t.select(); document.execCommand('copy'); t.remove(); }
    b.textContent = 'Copied'; b.classList.add('done');
    setTimeout(() => { b.textContent = 'Copy'; b.classList.remove('done'); }, 1600);
  }));

  // mobile menu
  const menu = $('.menu-btn'), scrim = $('.scrim'), side = $('#sidebar');
  const setOpen = (open) => {
    root.classList.toggle('nav-open', open); menu?.setAttribute('aria-expanded', String(open));
    if (scrim) scrim.hidden = !open;
    document.body.style.overflow = open ? 'hidden' : '';
  };
  menu?.addEventListener('click', () => setOpen(!root.classList.contains('nav-open')));
  scrim?.addEventListener('click', () => setOpen(false));
  addEventListener('keydown', (e) => { if (e.key === 'Escape' && root.classList.contains('nav-open')) setOpen(false); });
  side?.addEventListener('click', (e) => { if (e.target.closest('a') && innerWidth <= 900) setOpen(false); });
  $('.sidebar a.on')?.scrollIntoView({ block: 'center' });

  // on-page TOC highlight
  const tocLinks = $$('.toc a');
  if (tocLinks.length && 'IntersectionObserver' in window) {
    const targets = tocLinks.map((a) => document.getElementById(decodeURIComponent(a.hash.slice(1)))).filter(Boolean);
    const visible = new Set();
    const pick = () => {
      let cur = targets[0];
      for (const t of targets) if (t.getBoundingClientRect().top < 140) cur = t;
      tocLinks.forEach((a) => a.classList.toggle('on', a.hash === '#' + cur.id));
    };
    const io = new IntersectionObserver((es) => { es.forEach((e) => e.isIntersecting ? visible.add(e.target) : visible.delete(e.target)); pick(); }, { rootMargin: '-90px 0px -60% 0px' });
    targets.forEach((t) => io.observe(t));
    addEventListener('scroll', pick, { passive: true });
    pick();
  }

  // search
  const input = $('#search'), results = $('#results');
  const base = root.dataset.base || './';
  let sel = -1;
  const escH = (s) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;');
  const run = () => {
    const q = input.value.trim().toLowerCase();
    const index = window.DOCKET_SEARCH || [];
    if (!q) { results.hidden = true; results.innerHTML = ''; return; }
    const words = q.split(/\s+/);
    const hits = [];
    for (const p of index) {
      const title = p.t.toLowerCase();
      if (words.every((w) => title.includes(w))) hits.push({ score: 100, url: p.u, title: p.t, sub: p.s });
      for (const [h, id] of p.h) if (words.every((w) => h.toLowerCase().includes(w))) hits.push({ score: 60, url: p.u + '#' + id, title: h, sub: p.t });
      const x = p.x.toLowerCase();
      if (words.every((w) => x.includes(w)) && !hits.some((h) => h.url === p.u)) {
        const at = x.indexOf(words[0]);
        hits.push({ score: 20, url: p.u, title: p.t, sub: '…' + p.x.slice(Math.max(0, at - 30), at + 60).trim() + '…' });
      }
    }
    hits.sort((a, b) => b.score - a.score);
    const top = hits.slice(0, 10);
    sel = top.length ? 0 : -1;
    results.innerHTML = top.length ? top.map((h, i) => `<a href="${base}${h.url}"${i === 0 ? ' class="sel"' : ''}>${escH(h.title)}<small>${escH(h.sub)}</small></a>`).join('') : '<p>No matches. Try other words.</p>';
    results.hidden = false;
  };
  input?.addEventListener('input', run);
  input?.addEventListener('keydown', (e) => {
    const links = $$('a', results);
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault(); if (!links.length) return;
      sel = (sel + (e.key === 'ArrowDown' ? 1 : -1) + links.length) % links.length;
      links.forEach((a, i) => a.classList.toggle('sel', i === sel));
    } else if (e.key === 'Enter' && links[sel]) { location.href = links[sel].href; }
    else if (e.key === 'Escape') { input.value = ''; run(); input.blur(); }
  });
  addEventListener('keydown', (e) => {
    if (e.key === '/' && document.activeElement?.tagName !== 'INPUT' && document.activeElement?.tagName !== 'TEXTAREA') {
      e.preventDefault(); if (innerWidth <= 900) setOpen(true); input?.focus();
    }
  });
})();
