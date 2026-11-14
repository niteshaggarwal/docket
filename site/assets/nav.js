// Site menu: on tablets and phones the page links fold into a menu behind the button at the right of the nav.
(() => {
  const btn = document.querySelector('.site-menu-btn');
  const nav = btn?.closest('.nav');
  const links = nav?.querySelector('.nav-links');
  if (!btn || !nav || !links) return;
  const set = (open) => {
    nav.classList.toggle('menu-open', open);
    btn.setAttribute('aria-expanded', String(open));
    btn.setAttribute('aria-label', open ? 'Close menu' : 'Open menu');
  };
  btn.addEventListener('click', (e) => { e.stopPropagation(); set(!nav.classList.contains('menu-open')); });
  links.addEventListener('click', (e) => { if (e.target.closest('a')) set(false); });
  document.addEventListener('click', (e) => { if (nav.classList.contains('menu-open') && !nav.contains(e.target)) set(false); });
  addEventListener('keydown', (e) => { if (e.key === 'Escape' && nav.classList.contains('menu-open')) { set(false); btn.focus(); } });
  matchMedia('(min-width: 1101px)').addEventListener('change', (m) => { if (m.matches) set(false); });
})();
