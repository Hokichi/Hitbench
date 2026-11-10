(() => {
  function initFeatureCarousel(root) {
    const track = root.querySelector('[data-carousel-track]');
    const slides = [...root.querySelectorAll('[data-carousel-slide]')];
    const selectors = [...root.querySelectorAll('[data-feature-index]')];
    const choices = root.querySelector('.feature-choices');
    const status = root.querySelector('[data-carousel-status]');
    const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
    let selected = 0;
    let destination = null;
    let settleTimer;

    function slideOffset(index) {
      return slides[index].offsetLeft - slides[0].offsetLeft;
    }

    function setActive(index) {
      selected = index;
      selectors.forEach((button, i) => button.setAttribute('aria-pressed', String(i === index)));
      status.textContent = `${String(index + 1).padStart(2, '0')} / ${String(slides.length).padStart(2, '0')} · ${selectors[index].textContent}`;

      const buttonBounds = selectors[index].getBoundingClientRect();
      const listBounds = choices.getBoundingClientRect();
      const adjustment = buttonBounds.left < listBounds.left
        ? buttonBounds.left - listBounds.left
        : Math.max(0, buttonBounds.right - listBounds.right);
      if (adjustment) choices.scrollBy({ left: adjustment, behavior: 'instant' });
    }

    function selectFeature(index) {
      const next = (index + slides.length) % slides.length;
      destination = next;
      setActive(next);
      const left = slideOffset(next);
      track.scrollTo({ left, behavior: reducedMotion.matches ? 'instant' : 'smooth' });
      if (Math.abs(track.scrollLeft - left) < 2) destination = null;
    }

    function syncFromScroll() {
      if (destination !== null && Math.abs(track.scrollLeft - slideOffset(destination)) >= 2) return;
      destination = null;
      let nearest = 0;
      slides.forEach((slide, index) => {
        if (Math.abs(slideOffset(index) - track.scrollLeft) < Math.abs(slideOffset(nearest) - track.scrollLeft)) nearest = index;
      });
      setActive(nearest);
    }

    selectors.forEach((button, index) => button.addEventListener('click', () => selectFeature(index)));
    root.querySelector('[data-carousel-prev]').addEventListener('click', () => selectFeature(selected - 1));
    root.querySelector('[data-carousel-next]').addEventListener('click', () => selectFeature(selected + 1));
    root.addEventListener('keydown', event => {
      if (event.altKey || event.ctrlKey || event.metaKey) return;
      if (event.target !== track && !event.target.closest('button')) return;
      const targets = { ArrowLeft: selected - 1, ArrowRight: selected + 1, Home: 0, End: slides.length - 1 };
      if (Object.hasOwn(targets, event.key)) {
        event.preventDefault();
        selectFeature(targets[event.key]);
      }
    });

    track.addEventListener('scroll', () => {
      clearTimeout(settleTimer);
      settleTimer = setTimeout(syncFromScroll, 150);
    }, { passive: true });
    track.addEventListener('scrollend', syncFromScroll);
    for (const event of ['pointerdown', 'wheel']) {
      track.addEventListener(event, () => { destination = null; }, { passive: true });
    }
    new ResizeObserver(() => {
      destination = null;
      track.scrollTo({ left: slideOffset(selected), behavior: 'instant' });
    }).observe(track);

    status.setAttribute('aria-live', 'off');
    setActive(0);
    root.querySelector('[data-carousel-controls]').hidden = false;
    root.setAttribute('data-carousel-enhanced', '');
    requestAnimationFrame(() => status.setAttribute('aria-live', 'polite'));
  }

  const carousel = document.querySelector('[data-carousel]');
  if (carousel) initFeatureCarousel(carousel);
})();
