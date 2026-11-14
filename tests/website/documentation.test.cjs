const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function loadGuide({ width = 1440, hash = '', reducedMotion = false, present = true, observer = true } = {}) {
  let document;
  function element(id = '') {
    return {
      id, attrs: {}, handlers: {}, children: [], open: true, hidden: false, top: 0,
      addEventListener(name, handler) { (this.handlers[name] ??= []).push(handler); },
      fire(name, input = {}) { const event = { button: 0, defaultPrevented: false,
        preventDefault() { this.defaultPrevented = true; }, ...input }; this.handlers[name]?.forEach(fn => fn(event)); return event; },
      setAttribute(name, value) { this.attrs[name] = value; },
      getAttribute(name) { return this.attrs[name] ?? null; },
      removeAttribute(name) { delete this.attrs[name]; },
      contains(node) { return node === this || this.children.some(child => child.contains(node)); },
      focus(options) { document.activeElement = this; this.focusOptions = options; },
      getBoundingClientRect() { return { top: this.top, bottom: this.top + 500 }; }
    };
  }
  const ids = [['installation', 'first-project', 'mapsets'], ['workstation', 'playback', 'tracks', 'timeline-editing', 'control-points'],
    ['sample-library', 'saved-samples', 'defaults', 'hitnormal', 'instruments', 'key-mapping', 'midi'], ['export', 'export-output'],
    ['updates', 'shortcuts', 'troubleshooting', 'raising-an-issue', 'glossary']];
  const groups = ids.map(() => element());
  groups.forEach((group, i) => { group.open = i === 0; });
  const topics = ids.flat().map((id, i) => Object.assign(element(id), { top: 300 + i * 600 }));
  const links = ids.flatMap((category, i) => category.map(id => {
    const link = element(); link.attrs.href = '#' + id; link.closest = () => groups[i]; groups[i].children.push(link); return link;
  }));
  const contents = element('contents'); contents.children = groups;
  contents.querySelectorAll = () => groups;
  const article = element('guide-content'), pageTop = element('page-top'), topControl = element('back-to-top');
  const nodes = { '#contents': contents, '#guide-content': article, '#page-top': pageTop, '#back-to-top': topControl };
  const root = element('main');
  root.querySelector = selector => nodes[selector] ?? null;
  root.querySelectorAll = selector => selector === '.guide-topic[id]' ? topics : selector === '.toc-group' ? groups : links;
  const mobile = Object.assign(element(), { matches: width <= 760 });
  const motion = { matches: reducedMotion };
  const window = Object.assign(element(), { location: { hash }, scrollY: 0, innerHeight: 800,
    matchMedia: query => query.includes('max-width') ? mobile : motion,
    scrollTo(options) { scrollCalls.push(options); } });
  const scrollCalls = [];
  document = { querySelector: () => present ? root : null, getElementById: id => nodes['#' + id] ?? topics.find(t => t.id === id),
    activeElement: pageTop, documentElement: { scrollHeight: 20000 } };
  const context = { document, window, requestAnimationFrame: fn => { fn(); return 1; },
    IntersectionObserver: observer ? class { observe() {} } : undefined };
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../../docs/documentation.js'), 'utf8'), context);
  const link = id => links.find(a => a.getAttribute('href') === '#' + id);
  return { contents, article, pageTop, topControl, topics, groups, links, scrollCalls, motion, window, document,
    link, categoryFor: id => link(id).closest(), activeLinks: () => links.filter(a => a.getAttribute('aria-current') === 'location'),
    get focusedElement() { return document.activeElement; },
    focusCategoryFor(id) { document.activeElement = link(id).closest(); }, focusPage() { document.activeElement = pageTop; },
    scroll(y) { window.scrollY = y; window.fire('scroll'); },
    readAt(id) { const offset = topics.findIndex(t => t.id === id); topics.forEach((t, i) => t.top = (i - offset) * 600 + 40); window.fire('scroll'); },
    hashchange(value) { window.location.hash = value; window.fire('hashchange'); },
    resize(value) { mobile.matches = value <= 760; mobile.fire('change'); },
    clickTopic(id, input) { return link(id).fire('click', input); },
    activateTop(input) { return topControl.fire('click', input); }
  };
}

test('deep links, history and reading position maintain one active topic', () => {
  const g = loadGuide({ hash: '#midi' });
  assert.equal(g.link('midi').getAttribute('aria-current'), 'location');
  assert.equal(g.categoryFor('midi').open, true);
  g.hashchange('#export');
  assert.equal(g.link('export').getAttribute('aria-current'), 'location');
  g.readAt('glossary');
  assert.equal(g.link('glossary').getAttribute('aria-current'), 'location');
  assert.equal(g.activeLinks().length, 1);
  assert.doesNotThrow(() => g.hashchange('#%invalid'));
  assert.doesNotThrow(() => g.hashchange(''));
});

test('resizing and selection preserve manual keyboard browsing and mobile reading', () => {
  const g = loadGuide();
  g.categoryFor('midi').open = true;
  g.focusCategoryFor('midi'); g.readAt('export');
  assert.equal(g.categoryFor('midi').open, true);
  g.resize(760); assert.equal(g.contents.open, true);
  g.focusPage(); g.resize(761); g.resize(760);
  assert.equal(g.contents.open, false);
  g.contents.open = true;
  g.readAt('glossary');
  assert.equal(g.contents.open, true);
  const openBefore = g.groups.map(group => group.open);
  g.readAt('export'); assert.deepEqual(g.groups.map(group => group.open), openBefore);
  g.clickTopic('midi');
  assert.equal(g.contents.open, false); assert.equal(g.article.hidden, false);
  assert.equal(g.focusedElement.id, 'midi');
  g.resize(761); assert.equal(g.contents.open, true);
});

test('top arrow follows the 400 px visibility boundary', () => {
  const g = loadGuide();
  g.scroll(399); assert.equal(g.topControl.hidden, true);
  g.scroll(400); assert.equal(g.topControl.hidden, false);
  g.scroll(0); assert.equal(g.topControl.hidden, true);
});

test('returning to desktop reveals contents even when its closed mobile summary has focus', () => {
  const g = loadGuide({ width: 375 });
  assert.equal(g.contents.open, false);
  g.document.activeElement = g.contents;
  g.resize(1024);
  assert.equal(g.contents.open, true);
});

test('top action uses live motion preference, focuses page start, and preserves native modified clicks', () => {
  for (const reducedMotion of [false, true]) {
    const g = loadGuide(); g.motion.matches = reducedMotion; g.scroll(500);
    assert.equal(g.activateTop().defaultPrevented, true);
    assert.equal(g.scrollCalls.at(-1).top, 0);
    assert.equal(g.scrollCalls.at(-1).behavior, reducedMotion ? 'instant' : 'smooth');
    assert.equal(g.focusedElement, g.pageTop);
    assert.equal(g.pageTop.focusOptions.preventScroll, true);
  }
  for (const input of [{ ctrlKey: true }, { metaKey: true }, { shiftKey: true }, { altKey: true }, { button: 1 }]) {
    const g = loadGuide();
    assert.equal(g.activateTop(input).defaultPrevented, false);
    assert.equal(g.clickTopic('midi', input).defaultPrevented, false);
    assert.equal(g.scrollCalls.length, 0);
  }
});

test('missing guide and observer are safe and anchors keep native navigation', () => {
  assert.doesNotThrow(() => loadGuide({ present: false }));
  const g = loadGuide({ observer: false, width: 375 });
  g.hashchange('#raising-an-issue');
  assert.equal(g.link('raising-an-issue').getAttribute('aria-current'), 'location');
  assert.equal(g.clickTopic('midi').defaultPrevented, false);
});
