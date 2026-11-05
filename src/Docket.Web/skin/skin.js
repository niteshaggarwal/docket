/* Docket branding for Vikunja's web app. Cosmetic only: it renames "Vikunja" to "Docket" in the page title and in
   the app's own interface text, shows the Docket logo, and hides links to vikunja.io. It never touches anything
   you can edit (inputs, the description editor) or your own content (task, project and label names, comments). */
(function () {
  'use strict';
  var NAME = 'Docket';
  var WORD = /Vikunja/g;
  var HAS = /Vikunja/;
  // Your content and anything editable: never rewritten, even when it mentions Vikunja.
  var SKIP = 'input,textarea,select,option,script,style,noscript,code,pre,[contenteditable],[contenteditable] *,'
    + '.tiptap,.ProseMirror,.tasktext,.task-title,.kanban-card__title-link,.project-menu-title,.project-title,'
    + '.comment-content,.comments .content,.description,.task-view .heading,.tag,.label-wrapper,.search-results,'
    + '.multiselect,.attachment,.gantt-bar-text,.table td,.project-grid-item,.list-menu-link,.docket-keep';

  function logoFor(dark) { return dark ? '/docket-skin/logo-dark.svg' : '/docket-skin/logo.svg'; }
  function isDark() { return document.documentElement.classList.contains('dark'); }

  function fixTitle() {
    var t = document.title;
    if (HAS.test(t)) document.title = t.replace(WORD, NAME);
  }

  function skipped(el) {
    return !el || (el.closest && el.closest(SKIP));
  }

  function fixText(root) {
    if (!root) return;
    if (root.nodeType === 3) {
      if (HAS.test(root.nodeValue) && !skipped(root.parentElement)) root.nodeValue = root.nodeValue.replace(WORD, NAME);
      return;
    }
    if (root.nodeType !== 1 || skipped(root)) return;
    var walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, {
      acceptNode: function (n) { return HAS.test(n.nodeValue) ? NodeFilter.FILTER_ACCEPT : NodeFilter.FILTER_REJECT; }
    });
    var hits = [];
    while (walker.nextNode()) hits.push(walker.currentNode);
    hits.forEach(function (n) { if (!skipped(n.parentElement)) n.nodeValue = n.nodeValue.replace(WORD, NAME); });
    fixAttributes(root);
  }

  function fixAttributes(root) {
    var els = root.querySelectorAll ? root.querySelectorAll('[title*="Vikunja"],[alt*="Vikunja"],[aria-label*="Vikunja"],[placeholder*="Vikunja"]') : [];
    var list = Array.prototype.slice.call(els);
    if (root.matches && root.matches('[title*="Vikunja"],[alt*="Vikunja"],[aria-label*="Vikunja"],[placeholder*="Vikunja"]')) list.push(root);
    list.forEach(function (el) {
      if (skipped(el) && el.tagName !== 'INPUT' && el.tagName !== 'TEXTAREA') return;
      ['title', 'alt', 'aria-label', 'placeholder'].forEach(function (a) {
        var v = el.getAttribute(a);
        if (v && HAS.test(v)) el.setAttribute(a, v.replace(WORD, NAME));
      });
    });
  }

  // Vikunja's logo images: always the Docket wordmark for the current theme.
  function fixLogos(root) {
    var imgs = (root.querySelectorAll ? root.querySelectorAll('img.logo, .logo img, img[src*="logo"]') : []);
    Array.prototype.forEach.call(imgs, function (img) {
      if (img.closest('.tiptap,.ProseMirror,.attachment,.comments')) return;
      var want = logoFor(isDark());
      if (img.getAttribute('src') !== want && !/\/docket-skin\//.test(img.getAttribute('src') || '')) img.setAttribute('src', want);
      if (img.alt !== NAME) img.alt = NAME;
    });
  }

  // Links to vikunja.io (help, "powered by", sponsor) are hidden; the CSS does most of it, this covers inline styles.
  function hideVendorLinks(root) {
    var links = root.querySelectorAll ? root.querySelectorAll('a[href*="vikunja.io"], a[href*="vikunja.cloud"]') : [];
    Array.prototype.forEach.call(links, function (a) {
      if (a.closest('.tiptap,.ProseMirror,.comments,.description,.docket-keep')) return; // links you wrote yourself
      a.style.setProperty('display', 'none', 'important');
      a.setAttribute('aria-hidden', 'true');
    });
  }

  function sweep(root) {
    fixText(root);
    if (root.nodeType === 1) { fixLogos(root); hideVendorLinks(root); }
  }

  var pending = new Set();
  var scheduled = false;
  function flush() {
    scheduled = false;
    pending.forEach(function (n) { if (n.isConnected) sweep(n); });
    pending.clear();
    fixTitle();
  }
  function queue(n) {
    pending.add(n);
    if (!scheduled) { scheduled = true; requestAnimationFrame(flush); }
  }

  function start() {
    sweep(document.body);
    fixTitle();
    new MutationObserver(function (records) {
      records.forEach(function (r) {
        if (r.type === 'characterData') queue(r.target);
        else if (r.type === 'attributes') queue(r.target);
        else r.addedNodes.forEach(queue);
      });
    }).observe(document.body, { childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: ['title', 'alt', 'aria-label', 'placeholder', 'src'] });
    var titleEl = document.querySelector('head > title');
    if (titleEl) new MutationObserver(fixTitle).observe(titleEl, { childList: true, characterData: true, subtree: true });
    // Theme switches swap the logo.
    new MutationObserver(function () { fixLogos(document.body); })
      .observe(document.documentElement, { attributes: true, attributeFilter: ['class'] });
  }

  fixTitle();
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
  else start();
})();
