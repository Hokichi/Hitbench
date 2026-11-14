(() => {
  function initDocumentationGuide(root) {
    const contents = root.querySelector('#contents');
    const topics = Array.from(root.querySelectorAll('.guide-topic[id]'));
    const links = Array.from(root.querySelectorAll('.group-links a[href^="#"]'));
    const groups = Array.from(root.querySelectorAll('.toc-group'));
    const topControl = document.getElementById('back-to-top');
    const pageTop = document.getElementById('page-top');
    if (!contents || !topics.length || !links.length) return;

    const mobile = window.matchMedia('(max-width: 760px)');
    const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
    const topicLinks = new Map(links.map(link => [link.getAttribute('href').slice(1), link]));
    let currentId;
    let queued = false;

    function setCurrent(id, explicit = false) {
      if (!topicLinks.has(id)) return;
      if (id !== currentId) {
        links.forEach(link => link.removeAttribute('aria-current'));
        topicLinks.get(id).setAttribute('aria-current', 'location');
        currentId = id;
      }
      // Never rearrange an open mobile menu or close a category being browsed.
      if (!explicit && (mobile.matches || contents.contains(document.activeElement))) return;
      const activeGroup = topicLinks.get(id).closest('.toc-group');
      groups.forEach(group => {
        if (!group.contains(document.activeElement)) group.open = group === activeGroup;
      });
      activeGroup.open = true;
    }

    function readPosition() {
      let current = topics[0];
      for (const topic of topics) {
        if (topic.getBoundingClientRect().top > 120) break;
        current = topic;
      }
      setCurrent(current.id);
    }

    function refresh() {
      queued = false;
      if (topControl) {
        const hide = window.scrollY < 400;
        if (hide && document.activeElement === topControl) pageTop?.focus({ preventScroll: true });
        topControl.hidden = hide;
      }
      readPosition();
    }

    function scheduleRefresh() {
      if (queued) return;
      queued = true;
      requestAnimationFrame(refresh);
    }

    function hashTopic() {
      try { return decodeURIComponent(window.location.hash.slice(1)); }
      catch { return ''; }
    }

    function followHash() {
      const id = hashTopic();
      if (topicLinks.has(id)) setCurrent(id, true);
      else readPosition();
    }

    function resizeContents() {
      // Opening the desktop menu preserves focus; only collapsing can hide it.
      if (mobile.matches && contents.contains(document.activeElement)) return;
      contents.open = !mobile.matches;
      if (!mobile.matches && currentId) setCurrent(currentId);
    }

    function ordinaryClick(event) {
      return !event.defaultPrevented && event.button === 0 &&
        !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey;
    }

    links.forEach(link => link.addEventListener('click', event => {
      if (!ordinaryClick(event)) return;
      const id = link.getAttribute('href').slice(1);
      setCurrent(id, true);
      if (mobile.matches) {
        const topic = topics.find(topic => topic.id === id);
        topic?.setAttribute('tabindex', '-1');
        topic?.focus({ preventScroll: true });
        contents.open = false;
      }
      // Native anchors supply scrolling, URL fragments, and Back/Forward history.
    }));

    topControl?.addEventListener('click', event => {
      if (!ordinaryClick(event)) return;
      event.preventDefault();
      pageTop?.focus({ preventScroll: true });
      window.scrollTo({ top: 0, behavior: reducedMotion.matches ? 'instant' : 'smooth' });
    });

    window.addEventListener('scroll', scheduleRefresh, { passive: true });
    window.addEventListener('resize', scheduleRefresh);
    window.addEventListener('hashchange', followHash);
    mobile.addEventListener('change', resizeContents);
    resizeContents();
    refresh();
    followHash();
  }

  const root = document.querySelector('[data-documentation]');
  if (root) initDocumentationGuide(root);
})();
