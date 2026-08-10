const menuToggle = document.querySelector('#menu-toggle');
const menu = document.querySelector('#primary-menu');
const mobileNavigation = window.matchMedia('(max-width: 760px)');

if (menuToggle && menu) {
  const setMenuState = (open, returnFocus = false) => {
    menuToggle.setAttribute('aria-expanded', String(open));
    menuToggle.setAttribute('aria-label', open ? 'Close navigation menu' : 'Open navigation menu');
    menu.dataset.open = String(open);

    if (!open && returnFocus) menuToggle.focus();
  };

  menuToggle.addEventListener('click', () => {
    const open = menuToggle.getAttribute('aria-expanded') !== 'true';
    setMenuState(open);
  });

  menu.addEventListener('click', (event) => {
    if (event.target.closest('a')) setMenuState(false);
  });

  document.addEventListener('keydown', (event) => {
    if (event.key === 'Escape' && menuToggle.getAttribute('aria-expanded') === 'true') {
      setMenuState(false, true);
    }
  });

  document.addEventListener('click', (event) => {
    if (!event.target.closest('.nav') && menuToggle.getAttribute('aria-expanded') === 'true') {
      setMenuState(false);
    }
  });

  mobileNavigation.addEventListener('change', () => setMenuState(false));
}
