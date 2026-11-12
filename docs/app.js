const menuButton = document.getElementById('menu');
const links = document.getElementById('links');

if (menuButton && links) {
  menuButton.addEventListener('click', () => links.classList.toggle('open'));
  links.addEventListener('click', (event) => {
    if (event.target.tagName === 'A') {
      links.classList.remove('open');
    }
  });
}
