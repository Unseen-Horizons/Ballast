// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

// A small stand-in for the parts of the DOM the admin scripts use. It is not a DOM: it keeps what a test reads back (innerHTML,
// text, classes, attributes, listeners) and calls listeners when a test dispatches an event.

export function fakeClassList(initial = []) {
  const set = new Set(initial);
  return {
    add(...names) { names.forEach((n) => set.add(n)); },
    remove(...names) { names.forEach((n) => set.delete(n)); },
    contains(name) { return set.has(name); },
    toString() { return [...set].join(' '); }
  };
}

export function fakeElement(id = '', extra = {}) {
  const attributes = { ...(extra.attributes || {}) };
  const el = {
    id,
    innerHTML: '',
    textContent: '',
    className: '',
    title: '',
    type: extra.type || 'text',
    value: extra.value || '',
    disabled: false,
    hidden: false,
    dataset: extra.dataset || {},
    style: {},
    classList: fakeClassList(extra.classes || []),
    listeners: {},
    attributes,
    firstElementChild: extra.firstElementChild || null,
    children: [],
    addEventListener(type, fn) { (this.listeners[type] ||= []).push(fn); },
    dispatch(type, event = {}) {
      const fns = this.listeners[type] || [];
      fns.forEach((fn) => fn(event));
      return fns.length;
    },
    getAttribute(name) { return Object.prototype.hasOwnProperty.call(this.attributes, name) ? this.attributes[name] : null; },
    setAttribute(name, value) { this.attributes[name] = String(value); },
    closest(selector) { return (this.closestMap || {})[selector] || null; },
    // Moves the child, as the DOM does: it leaves its old parent first.
    appendChild(child) {
      if (child.parentNode) child.parentNode.children = child.parentNode.children.filter((c) => c !== child);
      this.children.push(child);
      child.parentNode = this;
      return child;
    },
    get firstChild() { return this.children[0] || null; },
    remove() { this.removed = true; }
  };
  if (el.firstElementChild === null && extra.withIcon) el.firstElementChild = fakeElement('', { classes: [] });
  return el;
}

export function fakeDocument(elements = {}) {
  const byId = new Map(Object.entries(elements));
  const bodyEl = fakeElement('body');
  return {
    body: bodyEl,
    addElement(id, el) { byId.set(id, el); return el; },
    getElementById(id) { return byId.get(id) || null; },
    querySelector(selector) { return (this.bySelector || {})[selector] || null; },
    querySelectorAll(selector) { return (this.bySelectorAll || {})[selector] || []; },
    bySelector: {},
    bySelectorAll: {},
    listeners: {},
    addEventListener(type, fn) { (this.listeners[type] ||= []).push(fn); }
  };
}

// A storage with the sessionStorage methods, backed by a Map.
export function fakeStorage(initial = {}) {
  const map = new Map(Object.entries(initial));
  return {
    map,
    getItem(key) { return map.has(key) ? map.get(key) : null; },
    setItem(key, value) { map.set(key, String(value)); },
    removeItem(key) { map.delete(key); }
  };
}

// A location with the parts the scripts read, and a href that records navigation.
export function fakeLocation(href = 'https://console.example.test/admin/index.html') {
  const url = new URL(href);
  const location = {
    href: url.href,
    origin: url.origin,
    pathname: url.pathname,
    search: url.search,
    hash: url.hash,
    hostname: url.hostname,
    navigations: []
  };
  return new Proxy(location, {
    set(target, key, value) {
      if (key === 'href') target.navigations.push(String(value));
      target[key] = value;
      return true;
    }
  });
}

// A JWT with a payload, unsigned. The admin scripts only read the payload.
export function fakeJwt(payload) {
  const encode = (value) => Buffer.from(JSON.stringify(value), 'utf8').toString('base64url');
  return `${encode({ alg: 'none', typ: 'JWT' })}.${encode(payload)}.signature`;
}
