/*
 * Web Test Toolkit — inspect overlay.
 *
 * Injected into the page under test by InspectorSession. Three jobs:
 *   1. Highlight whatever the user hovers, so they can see what they're about to capture.
 *   2. Record clicks and typed values, WITHOUT interfering with the page — the user has to
 *      be able to walk the real flow (log in, navigate, submit) while we watch.
 *   3. Propose locator candidates. Only this script can see the live DOM, so uniqueness
 *      checks happen here; the *ranking policy* lives in C# (LocatorRanker) where it is
 *      unit-testable without a browser.
 *
 * Two constraints drive the odd-looking bits below:
 *
 * - It is re-injected on every poll, because a full page load wipes it. Re-injection must
 *   therefore be idempotent: if our version is already live we just re-enable and return,
 *   otherwise we would stack duplicate listeners and capture every click N times.
 *
 * - The queue lives in sessionStorage, not a JS variable. Clicking a submit button captures
 *   the click and then immediately destroys the JS context by navigating. sessionStorage
 *   writes are synchronous, so the event survives the unload; an in-memory array would not.
 *   (Cross-origin navigation still loses anything undrained — the C# side polls fast enough
 *   that this is a sub-second window.)
 */
(function () {
  'use strict';

  var VERSION = 4;
  var QUEUE_KEY = '__wtt_queue';
  var MAX_QUEUE = 200;
  var MAX_HTML = 300;
  var MAX_TEXT = 120;
  var MAX_OPTIONS = 25;

  // Already live at this version: just make sure capture is on and bail out. This is the
  // common path — the C# side re-injects blindly rather than asking first.
  if (window.__wtt && window.__wtt.version === VERSION) {
    window.__wtt.enable();
    return;
  }

  // A different version is live (toolkit upgraded mid-session). Tear it down first.
  if (window.__wtt && typeof window.__wtt.destroy === 'function') {
    try { window.__wtt.destroy(); } catch (e) { /* best effort */ }
  }

  var enabled = true;
  var memoryQueue = [];
  var storageWorks = true;

  // ---------------------------------------------------------------- queue

  function readQueue() {
    if (!storageWorks) return memoryQueue.slice();
    try {
      var raw = window.sessionStorage.getItem(QUEUE_KEY);
      return raw ? JSON.parse(raw) : [];
    } catch (e) {
      storageWorks = false;
      return memoryQueue.slice();
    }
  }

  function writeQueue(items) {
    if (!storageWorks) { memoryQueue = items; return; }
    try {
      window.sessionStorage.setItem(QUEUE_KEY, JSON.stringify(items));
    } catch (e) {
      storageWorks = false;
      memoryQueue = items;
    }
  }

  function push(record) {
    var items = readQueue();
    // Drop oldest rather than newest: a runaway page firing synthetic events should not
    // be able to hide the user's most recent real action.
    if (items.length >= MAX_QUEUE) items = items.slice(items.length - MAX_QUEUE + 1);
    items.push(record);
    writeQueue(items);
  }

  function drain() {
    var items = readQueue();
    writeQueue([]);
    return JSON.stringify(items);
  }

  // ---------------------------------------------------------------- helpers

  function esc(value) {
    var s = String(value);
    if (window.CSS && typeof CSS.escape === 'function') return CSS.escape(s);
    return s.replace(/([^\w-])/g, '\\$1');
  }

  function isUnique(selector) {
    try {
      return document.querySelectorAll(selector).length === 1;
    } catch (e) {
      return false;
    }
  }

  function trim(text, max) {
    if (!text) return null;
    var s = String(text).replace(/\s+/g, ' ').trim();
    if (!s) return null;
    return s.length > max ? s.slice(0, max) : s;
  }

  // Framework-generated ids (React's ":r3:", Ember's "ember512", GUIDs, long digit runs)
  // are unique today and gone after the next deploy. Still worth capturing — they are
  // better than an xpath for a single session — but C# scores them well below a real id.
  function looksVolatile(id) {
    return /^:r/.test(id) ||
      /\d{4,}/.test(id) ||
      /[0-9a-f]{8}-[0-9a-f]{4}/i.test(id) ||
      /^(ember|ext-gen|yui|mat-|cdk-|radix-|headlessui-)/i.test(id);
  }

  function labelTextFor(el) {
    try {
      if (el.labels && el.labels.length > 0) return trim(el.labels[0].textContent, MAX_TEXT);
    } catch (e) { /* labels is not on every element type */ }

    if (el.id) {
      var forLabel = document.querySelector('label[for="' + esc(el.id) + '"]');
      if (forLabel) return trim(forLabel.textContent, MAX_TEXT);
    }

    var wrapping = el.closest ? el.closest('label') : null;
    return wrapping ? trim(wrapping.textContent, MAX_TEXT) : null;
  }

  // Nearest meaningful container plus its heading — not used for locating anything, but it
  // is what lets the label-suggestion prompt say "the checkout form" instead of "the form".
  function ancestorContext(el) {
    var container = el.closest ? el.closest('form, dialog, [role="dialog"], section, main, nav, header, footer') : null;
    if (!container) return null;

    var parts = [container.tagName.toLowerCase()];
    if (container.id && !looksVolatile(container.id)) parts.push('#' + container.id);

    var heading = container.querySelector('h1, h2, h3, legend, [role="heading"]');
    if (heading) {
      var headingText = trim(heading.textContent, 60);
      if (headingText) parts.push('"' + headingText + '"');
    }
    return parts.join(' ');
  }

  // ---------------------------------------------------------------- locator candidates

  function cssPath(el) {
    var parts = [];
    var node = el;
    for (var depth = 0; node && node.nodeType === 1 && depth < 6; depth++) {
      var segment = node.tagName.toLowerCase();

      if (node.id && !looksVolatile(node.id)) {
        parts.unshift('#' + esc(node.id));
        break;
      }

      var parent = node.parentElement;
      if (parent) {
        var siblings = Array.prototype.filter.call(parent.children, function (c) {
          return c.tagName === node.tagName;
        });
        if (siblings.length > 1) {
          segment += ':nth-of-type(' + (siblings.indexOf(node) + 1) + ')';
        }
      }

      parts.unshift(segment);
      // Shortest selector that already identifies the element beats a longer one.
      if (isUnique(parts.join(' > '))) break;
      node = parent;
    }

    var selector = parts.join(' > ');
    return isUnique(selector) ? selector : null;
  }

  function absoluteXPath(el) {
    var parts = [];
    for (var node = el; node && node.nodeType === 1; node = node.parentElement) {
      var index = 1;
      for (var sib = node.previousElementSibling; sib; sib = sib.previousElementSibling) {
        if (sib.tagName === node.tagName) index++;
      }
      parts.unshift(node.tagName.toLowerCase() + '[' + index + ']');
    }
    return '/' + parts.join('/');
  }

  function attributeCandidate(el, attribute, kind, out) {
    var value = el.getAttribute(attribute);
    if (!value) return;
    var selector = el.tagName.toLowerCase() + '[' + attribute + '="' + value.replace(/"/g, '\\"') + '"]';
    if (isUnique(selector)) out.push({ strategy: 'css', value: selector, kind: kind });
  }

  function buildCandidates(el) {
    var out = [];

    if (el.id && isUnique('#' + esc(el.id))) {
      out.push({ strategy: 'id', value: el.id, kind: looksVolatile(el.id) ? 'volatileId' : 'id' });
    }

    ['data-testid', 'data-test-id', 'data-test', 'data-qa', 'data-cy', 'data-automation-id'].forEach(function (attribute) {
      var value = el.getAttribute(attribute);
      if (!value) return;
      var selector = '[' + attribute + '="' + value.replace(/"/g, '\\"') + '"]';
      if (isUnique(selector)) out.push({ strategy: 'css', value: selector, kind: 'testId' });
    });

    var name = el.getAttribute('name');
    if (name && isUnique(el.tagName.toLowerCase() + '[name="' + name.replace(/"/g, '\\"') + '"]')) {
      // Selenium's By.Name matches across tag names, so only claim the bare `name`
      // strategy when the attribute is unique document-wide, not just within the tag.
      if (isUnique('[name="' + name.replace(/"/g, '\\"') + '"]')) {
        out.push({ strategy: 'name', value: name, kind: 'name' });
      } else {
        out.push({
          strategy: 'css',
          value: el.tagName.toLowerCase() + '[name="' + name.replace(/"/g, '\\"') + '"]',
          kind: 'name'
        });
      }
    }

    attributeCandidate(el, 'aria-label', 'ariaLabel', out);
    attributeCandidate(el, 'placeholder', 'placeholder', out);

    // Text-based xpath: readable and fairly stable for buttons and links, but the first
    // thing to break under localisation, so C# ranks it below attribute-based locators.
    var text = trim(el.textContent, MAX_TEXT);
    if (text && text.length <= 50 && text.indexOf("'") === -1 && /^(a|button|label|h1|h2|h3|span|li|td)$/i.test(el.tagName)) {
      var textXPath = "//" + el.tagName.toLowerCase() + "[normalize-space(.)='" + text + "']";
      try {
        var matches = document.evaluate('count(' + textXPath + ')', document, null, XPathResult.NUMBER_TYPE, null).numberValue;
        if (matches === 1) out.push({ strategy: 'xpath', value: textXPath, kind: 'text' });
      } catch (e) { /* malformed xpath from odd text — just skip it */ }
    }

    var path = cssPath(el);
    if (path) out.push({ strategy: 'css', value: path, kind: 'cssPath' });

    // Always last, always works, always the first thing to break. It exists so that
    // HasLocator is never false — the user can pick something better in the UI.
    out.push({ strategy: 'xpath', value: absoluteXPath(el), kind: 'absoluteXPath' });

    return out;
  }

  // ---------------------------------------------------------------- element state
  //
  // Without this, the model only has the raw outerHTML snippet to infer a <select>'s real
  // options, or a checkbox's current state, from — a real bug in a sibling project came
  // from exactly that gap (guessing led to calling .SendKeys() on a dropdown). Capturing it
  // directly here means the model never has to guess.

  function selectOptionsFor(el) {
    if (!el.options) return null;
    var out = [];
    for (var i = 0; i < el.options.length && i < MAX_OPTIONS; i++) {
      var opt = el.options[i];
      out.push({ value: opt.value, text: trim(opt.textContent, MAX_TEXT) || '', selected: !!opt.selected });
    }
    return out;
  }

  function checkedStateFor(el) {
    var type = (el.getAttribute('type') || '').toLowerCase();
    if (el.tagName === 'INPUT' && (type === 'checkbox' || type === 'radio')) return !!el.checked;
    return null;
  }

  function maxLengthFor(el) {
    // HTMLInputElement/HTMLTextAreaElement default maxLength to -1 when unset.
    return typeof el.maxLength === 'number' && el.maxLength >= 0 ? el.maxLength : null;
  }

  // ---------------------------------------------------------------- capture

  function describe(el, kind, value) {
    return {
      kind: kind,
      tagName: el.tagName.toLowerCase(),
      id: el.id || null,
      name: el.getAttribute('name'),
      type: el.getAttribute('type'),
      placeholder: el.getAttribute('placeholder'),
      ariaLabel: el.getAttribute('aria-label'),
      labelText: labelTextFor(el),
      cssClasses: trim(el.getAttribute('class'), MAX_TEXT),
      text: trim(el.textContent, MAX_TEXT),
      value: value === undefined ? null : value,
      html: trim(el.outerHTML, MAX_HTML),
      ancestors: ancestorContext(el),
      url: window.location.href,
      at: Date.now(),
      candidates: buildCandidates(el),
      checked: checkedStateFor(el),
      required: typeof el.required === 'boolean' ? el.required : null,
      maxLength: maxLengthFor(el),
      options: selectOptionsFor(el)
    };
  }

  function isOurs(el) {
    return !!(el && el.closest && el.closest('[data-wtt-overlay]'));
  }

  function onClick(event) {
    if (!enabled) return;
    var el = event.target;
    if (!el || el.nodeType !== 1 || isOurs(el)) return;

    // The user aims at the label or icon inside a button; the test should click the button.
    var actionable = el.closest('a, button, [role="button"], input[type="submit"], input[type="button"], [onclick]');
    push(describe(actionable || el, 'click'));
  }

  function onChange(event) {
    if (!enabled) return;
    var el = event.target;
    if (!el || el.nodeType !== 1 || isOurs(el)) return;

    var type = (el.getAttribute('type') || '').toLowerCase();
    // Checkboxes, radios and buttons already produced a click record; recording the
    // change too would emit the same action twice.
    if (type === 'checkbox' || type === 'radio' || type === 'button' || type === 'submit') return;
    if (!/^(input|textarea|select)$/i.test(el.tagName)) return;

    push(describe(el, 'input', el.value == null ? null : String(el.value)));
  }

  // ---------------------------------------------------------------- highlight

  var box = null;
  var badge = null;

  function ensureChrome() {
    if (box && box.isConnected) return;

    box = document.createElement('div');
    box.setAttribute('data-wtt-overlay', 'box');
    box.style.cssText = [
      'position:fixed', 'z-index:2147483647', 'pointer-events:none',
      'border:2px solid #e0006c', 'background:rgba(224,0,108,0.08)',
      'border-radius:3px', 'display:none', 'transition:all 40ms linear'
    ].join(';');

    badge = document.createElement('div');
    badge.setAttribute('data-wtt-overlay', 'badge');
    badge.style.cssText = [
      'position:fixed', 'z-index:2147483647', 'pointer-events:none',
      'background:#e0006c', 'color:#fff', 'font:11px/1.5 monospace',
      'padding:1px 6px', 'border-radius:3px', 'display:none', 'white-space:nowrap'
    ].join(';');

    (document.body || document.documentElement).appendChild(box);
    (document.body || document.documentElement).appendChild(badge);
  }

  function hideChrome() {
    if (box) box.style.display = 'none';
    if (badge) badge.style.display = 'none';
  }

  function onMove(event) {
    if (!enabled) { hideChrome(); return; }
    var el = event.target;
    if (!el || el.nodeType !== 1 || isOurs(el)) return;

    ensureChrome();
    var rect = el.getBoundingClientRect();
    if (rect.width === 0 && rect.height === 0) { hideChrome(); return; }

    box.style.display = 'block';
    box.style.left = rect.left + 'px';
    box.style.top = rect.top + 'px';
    box.style.width = rect.width + 'px';
    box.style.height = rect.height + 'px';

    var best = buildCandidates(el)[0];
    badge.textContent = el.tagName.toLowerCase() + (best ? '  ' + best.strategy + '=' + best.value : '');
    badge.style.display = 'block';
    badge.style.left = rect.left + 'px';
    // Above the element unless that would run off the top of the viewport.
    badge.style.top = (rect.top > 20 ? rect.top - 18 : rect.bottom + 4) + 'px';
  }

  // ---------------------------------------------------------------- wiring

  // Capture phase throughout: we want to see the event even if the page stops propagation.
  // We never preventDefault — the page has to keep working so the user can walk the flow.
  document.addEventListener('click', onClick, true);
  document.addEventListener('change', onChange, true);
  document.addEventListener('mouseover', onMove, true);
  document.addEventListener('mouseout', function (e) { if (!e.relatedTarget) hideChrome(); }, true);
  window.addEventListener('scroll', hideChrome, true);

  window.__wtt = {
    version: VERSION,
    drain: drain,
    enable: function () { enabled = true; },
    disable: function () { enabled = false; hideChrome(); },
    status: function () {
      return JSON.stringify({
        version: VERSION,
        enabled: enabled,
        pending: readQueue().length,
        url: window.location.href,
        title: document.title
      });
    },
    destroy: function () {
      document.removeEventListener('click', onClick, true);
      document.removeEventListener('change', onChange, true);
      document.removeEventListener('mouseover', onMove, true);
      hideChrome();
      if (box) box.remove();
      if (badge) badge.remove();
      enabled = false;
    }
  };
})();
