const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');

function loadCarousel({ reducedMotion = false, present = true } = {}) {
  const element = () => ({
    handlers: {}, attrs: {}, hidden: true, textContent: '',
    addEventListener(name, handler) { this.handlers[name] = handler; },
    setAttribute(name, value) { this.attrs[name] = value; },
    getBoundingClientRect() { return { left: 0, right: 100 }; }
  });
  const track = Object.assign(element(), { scrollLeft: 0, calls: [],
    scrollTo(options) { this.calls.push(options); this.scrollLeft = options.left; } });
  const slides = Array.from({ length: 6 }, (_, i) => ({ offsetLeft: i * 100 }));
  const selectors = ['Timeline', 'Tracks', 'Instruments', 'Samples', 'Control points', 'Export'].map(textContent => Object.assign(element(), { textContent }));
  const choices = Object.assign(element(), { scrollBy() {} });
  const status = element(), controls = element(), previous = element(), next = element();
  const nodes = { '[data-carousel-track]': track, '.feature-choices': choices, '[data-carousel-status]': status,
    '[data-carousel-controls]': controls, '[data-carousel-prev]': previous, '[data-carousel-next]': next };
  const root = Object.assign(element(), {
    querySelector: key => nodes[key] ?? null,
    querySelectorAll: key => key === '[data-carousel-slide]' ? slides : key === '[data-feature-index]' ? selectors : []
  });
  let resize;
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../../docs/carousel.js'), 'utf8'), {
    document: { querySelector: () => present ? root : null },
    window: { matchMedia: () => ({ matches: reducedMotion }) },
    ResizeObserver: class { constructor(callback) { resize = callback; } observe() {} },
    requestAnimationFrame: callback => callback(), setTimeout: () => 1, clearTimeout() {}
  });
  return { track, slides, selectors, status, controls, previous, next, root, resize };
}

test('carousel works without dots and synchronizes all remaining navigation', () => {
  const c = loadCarousel();
  assert.equal(c.controls.hidden, false);
  c.selectors[3].handlers.click();
  assert.equal(c.selectors[3].attrs['aria-pressed'], 'true');
  assert.equal(c.status.textContent, '04 / 06 · Samples');
  c.selectors[0].handlers.click(); c.previous.handlers.click();
  assert.equal(c.track.scrollLeft, 500);
  c.next.handlers.click(); assert.equal(c.track.scrollLeft, 0);
  c.selectors[4].handlers.click(); c.selectors[2].handlers.click(); c.previous.handlers.click();
  assert.equal(c.track.scrollLeft, 100);
  c.track.handlers.wheel(); c.track.scrollLeft = 390; c.track.handlers.scrollend();
  assert.equal(c.selectors[4].attrs['aria-pressed'], 'true');
  c.resize(); assert.equal(c.track.scrollLeft, 400);
  let prevented = false;
  const event = { target: c.track, key: 'Home', preventDefault() { prevented = true; } };
  c.root.handlers.keydown(event); assert.equal(prevented, true); assert.equal(c.track.scrollLeft, 0);
  c.root.handlers.keydown({ ...event, key: 'End' }); assert.equal(c.track.scrollLeft, 500);
  c.root.handlers.keydown({ ...event, key: 'ArrowLeft' }); assert.equal(c.track.scrollLeft, 400);
  prevented = false;
  c.root.handlers.keydown({ ...event, ctrlKey: true });
  assert.equal(prevented, false); assert.equal(c.track.scrollLeft, 400);
});

test('reduced motion is immediate and missing carousels are safe', () => {
  const c = loadCarousel({ reducedMotion: true });
  c.next.handlers.click(); assert.equal(c.track.calls.at(-1).behavior, 'instant');
  assert.doesNotThrow(() => loadCarousel({ present: false }));
});
