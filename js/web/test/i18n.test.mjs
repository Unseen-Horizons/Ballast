// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { describe, it } from 'node:test';

const require = createRequire(import.meta.url);
const { createI18n } = require('../browser/i18n.js');

const BUNDLED_EN = {
  greeting: 'Hello',
  msg_request_failed: 'Request failed.',
  lang_en: 'English',
  lang_hr: 'Hrvatski',
  status_open: 'Open',
  err_full: 'Fully booked.'
};
const CROATIAN = { greeting: 'Bok', msg_request_failed: 'Zahtjev nije uspio.', status_open: 'Otvoreno', err_full: 'Termin je pun.' };

// A test double for the browser objects the loader uses.
function fakeEnv({ search = '', language = 'en-US', stored = {}, fetchImpl, readyState = 'complete', switcher = null } = {}) {
  const storage = new Map(Object.entries(stored));
  const events = [];
  const cookies = [];
  const listeners = {};
  const elements = [];
  const win = {
    location: { search },
    dispatchEvent(event) { events.push(event); return true; },
    addEventListener(type, fn) { (listeners[type] ||= []).push(fn); }
  };
  const documentElement = { lang: '' };
  const doc = {
    readyState,
    documentElement,
    set cookie(value) { cookies.push(value); },
    get cookie() { return cookies.join('; '); },
    querySelectorAll(selector) {
      if (selector === '[data-i18n]') return elements;
      if (switcher && selector === '.lang-btn') return switcher.buttons;
      return [];
    },
    getElementById(id) { return switcher && id === 'lang-switcher' ? switcher.element : null; },
    createElement(tag) {
      const listenersOfButton = {};
      return {
        tag, className: '', textContent: '', attributes: {},
        setAttribute(k, v) { this.attributes[k] = v; },
        getAttribute(k) { return this.attributes[k] ?? null; },
        classList: { add() {}, remove() {} },
        addEventListener(type, fn) { listenersOfButton[type] = fn; },
        dispatchClick() { listenersOfButton.click.call(this); }
      };
    },
    addEventListener(type, fn) { (listeners[`doc:${type}`] ||= []).push(fn); }
  };
  const storageObject = {
    getItem: (k) => (storage.has(k) ? storage.get(k) : null),
    setItem: (k, v) => { storage.set(k, String(v)); }
  };
  class FakeEvent {
    constructor(type, init) { this.type = type; this.detail = init && init.detail; }
  }
  return {
    env: {
      window: win,
      document: doc,
      storage: storageObject,
      navigator: { language },
      fetch: fetchImpl || (() => Promise.reject(new Error('no network in this test'))),
      CustomEvent: FakeEvent
    },
    storage, events, cookies, documentElement, elements, listeners
  };
}

function texts(data) {
  return { json: async () => data };
}

// Lets the pending promise chain of the loader finish.
async function settle() {
  await new Promise((resolve) => setTimeout(resolve, 5));
}

const BASE = {
  langs: ['en', 'hr'],
  bundled: BUNDLED_EN,
  statusMap: { Open: 'status_open' },
  errorCodeMap: { SLOT_FULLY_BOOKED: 'err_full' }
};

describe('createI18n: options', () => {
  it('needs a non-empty list of languages', () => {
    assert.throws(() => createI18n({ env: fakeEnv().env }), TypeError);
    assert.throws(() => createI18n({ langs: [], env: fakeEnv().env }), TypeError);
    assert.throws(() => createI18n({ langs: ['en', ''], env: fakeEnv().env }), TypeError);
  });
});

describe('createI18n: the starting language', () => {
  it('starts in the default language with the bundled texts, and marks the page as English', () => {
    const { env, documentElement } = fakeEnv();
    const i18n = createI18n({ ...BASE, env, autoStart: false });

    assert.equal(i18n.getLang(), 'en');
    assert.equal(i18n.t('greeting'), 'Hello');
    assert.equal(i18n.textsReady(), true);
    assert.equal(documentElement.lang, 'en');
  });

  it('takes the language of the page address, and saves it as the choice and in a cookie', () => {
    const f = fakeEnv({ search: '?lang=HR' });
    const i18n = createI18n({ ...BASE, env: f.env, autoStart: false });

    assert.equal(i18n.getLang(), 'hr');
    assert.equal(f.storage.get('lang'), 'hr');
    assert.match(f.cookies.join(';'), /lang=hr;path=\/$/);
    // Croatian has no bundled texts and no cache, so nothing is shown until its file arrives.
    assert.equal(i18n.textsReady(), false);
    assert.equal(i18n.t('greeting'), null);
  });

  it('ignores a language in the address that the site does not have', () => {
    const f = fakeEnv({ search: '?lang=de' });
    const i18n = createI18n({ ...BASE, env: f.env, autoStart: false });

    assert.equal(i18n.getLang(), 'en');
    assert.equal(f.storage.has('lang'), false);
  });

  it('uses the saved choice when the site has it', () => {
    const f = fakeEnv({ stored: { lang: 'hr', i18n_hr: JSON.stringify(CROATIAN) } });
    const i18n = createI18n({ ...BASE, env: f.env, autoStart: false });

    assert.equal(i18n.getLang(), 'hr');
    assert.equal(i18n.t('greeting'), 'Bok');
  });

  it('ignores a saved choice the site does not have, and uses the browser language', () => {
    const f = fakeEnv({ stored: { lang: 'de' }, language: 'hr-HR' });
    const i18n = createI18n({ ...BASE, env: f.env, autoStart: false });

    assert.equal(i18n.getLang(), 'hr');
  });

  it('falls back to the default language when the browser language is not one the site has', () => {
    const f = fakeEnv({ language: 'fr-FR' });
    const i18n = createI18n({ ...BASE, env: f.env, autoStart: false });

    assert.equal(i18n.getLang(), 'en');
  });

  it('uses the cached texts of a language on a return visit', () => {
    const f = fakeEnv({ search: '?lang=hr', stored: { i18n_hr: JSON.stringify(CROATIAN) } });
    const i18n = createI18n({ ...BASE, env: f.env, autoStart: false });

    assert.equal(i18n.t('greeting'), 'Bok');
  });

  it('ignores a damaged cache and waits for the file', () => {
    const f = fakeEnv({ search: '?lang=hr', stored: { i18n_hr: '{not json' } });
    const i18n = createI18n({ ...BASE, env: f.env, autoStart: false });

    assert.equal(i18n.textsReady(), false);
  });
});

describe('createI18n: loading the texts of a language', () => {
  it('fetches the file, caches it, and announces the texts once', async () => {
    const requested = [];
    const f = fakeEnv({
      search: '?lang=hr',
      fetchImpl: (url) => { requested.push(url); return Promise.resolve(texts(CROATIAN)); }
    });
    const i18n = createI18n({ ...BASE, env: f.env });
    await settle();

    assert.deepEqual(requested, ['/i18n/hr.json']);
    assert.equal(i18n.t('greeting'), 'Bok');
    assert.equal(JSON.parse(f.storage.get('i18n_hr')).greeting, 'Bok');
    assert.deepEqual(f.events.map((e) => e.type), ['i18nReady']);
  });

  it('keeps the bundled English when a file cannot be read and no texts are shown yet', async () => {
    const f = fakeEnv({ fetchImpl: () => Promise.reject(new Error('offline')) });
    const i18n = createI18n({ ...BASE, env: f.env });
    await settle();

    assert.equal(i18n.t('greeting'), 'Hello');
    assert.equal(i18n.textsReady(), true);
  });

  it('shows the key when a file cannot be read and there is no bundled text', async () => {
    const f = fakeEnv({ search: '?lang=hr', fetchImpl: () => Promise.reject(new Error('offline')) });
    const i18n = createI18n({ langs: ['en', 'hr'], env: f.env });
    await settle();

    assert.equal(i18n.textsReady(), true);
    assert.equal(i18n.t('greeting'), 'greeting');
  });

  it('treats an empty texts file as a failure', async () => {
    const f = fakeEnv({ search: '?lang=hr', fetchImpl: () => Promise.resolve(texts({})) });
    const i18n = createI18n({ ...BASE, env: f.env });
    await settle();

    assert.equal(f.storage.has('i18n_hr'), false);
    assert.equal(i18n.textsReady(), true);
  });
});

describe('createI18n: switching the language', () => {
  it('refuses a language the site does not have, and changes nothing', () => {
    const f = fakeEnv();
    const i18n = createI18n({ ...BASE, env: f.env, autoStart: false });

    assert.equal(i18n.setLanguage('de'), false);
    assert.equal(i18n.getLang(), 'en');
    assert.equal(f.storage.get('lang'), undefined);
  });

  it('saves the choice, shows the cached texts at once, and announces languageChanged after the file arrives', async () => {
    const f = fakeEnv({
      stored: { i18n_hr: JSON.stringify(CROATIAN) },
      fetchImpl: () => Promise.resolve(texts(CROATIAN))
    });
    const i18n = createI18n({ ...BASE, env: f.env, autoStart: false });

    assert.equal(i18n.setLanguage('hr'), true);
    assert.equal(i18n.getLang(), 'hr');
    assert.equal(i18n.t('greeting'), 'Bok');
    assert.equal(f.storage.get('lang'), 'hr');
    assert.match(f.cookies.join(';'), /lang=hr;path=\/$/);
    assert.equal(f.documentElement.lang, 'hr');

    await settle();
    assert.deepEqual(f.events.map((e) => e.type), ['i18nReady', 'i18nReady', 'languageChanged']);
    assert.deepEqual(f.events[2].detail, { lang: 'hr' });
  });

  it('shows nothing for a language whose texts are not yet loaded, rather than the old language', () => {
    const f = fakeEnv({ fetchImpl: () => new Promise(() => {}) });
    const i18n = createI18n({ ...BASE, env: f.env, autoStart: false });

    i18n.setLanguage('hr');
    assert.equal(i18n.textsReady(), false);
    assert.equal(i18n.t('greeting'), null);
  });
});

describe('createI18n: text for keys, statuses and errors', () => {
  it('returns the text of a key, the key itself when it is missing, and null while loading', () => {
    const f = fakeEnv({ search: '?lang=hr' });
    const i18n = createI18n({ ...BASE, env: f.env, autoStart: false });
    assert.equal(i18n.t(''), '');
    assert.equal(i18n.t('greeting'), null);

    const english = createI18n({ ...BASE, env: fakeEnv().env, autoStart: false });
    assert.equal(english.t('no_such_key'), 'no_such_key');
    assert.equal(english.t('constructor'), 'constructor');
  });

  it('gives the English text while the texts are loading, and never null', () => {
    const f = fakeEnv({ search: '?lang=hr' });
    const i18n = createI18n({ ...BASE, env: f.env, autoStart: false });

    assert.equal(i18n.text('greeting', 'Hello there'), 'Hello there');
    assert.equal(i18n.text('no_such_key', 'Fallback'), 'Fallback');
  });

  it('maps a status code to its text, and gives the code itself for an unknown status', () => {
    const i18n = createI18n({ ...BASE, env: fakeEnv().env, autoStart: false });

    assert.equal(i18n.status('Open'), 'Open');
    assert.equal(i18n.status('Closed'), 'Closed');
    assert.equal(i18n.status(''), '');
  });

  it('maps an error code to its text, and otherwise gives the fallback or the request-failed text', () => {
    const i18n = createI18n({ ...BASE, env: fakeEnv().env, autoStart: false });

    assert.equal(i18n.error('SLOT_FULLY_BOOKED'), 'Fully booked.');
    assert.equal(i18n.error('UNKNOWN_CODE', 'Try again.'), 'Try again.');
    assert.equal(i18n.error('UNKNOWN_CODE'), 'Request failed.');
    assert.equal(i18n.error('constructor'), 'Request failed.');
  });

  it('gives the text of a failed answer: the detail for a same-language 4xx with a code, else the code text', () => {
    const i18n = createI18n({ ...BASE, env: fakeEnv().env, autoStart: false });

    assert.equal(i18n.problemText({ code: 'SLOT_FULLY_BOOKED', detail: 'Server detail.' }, 409), 'Server detail.');
    assert.equal(i18n.problemText({ code: 'SLOT_FULLY_BOOKED', detail: 'Server detail.' }, 500), 'Fully booked.');
    assert.equal(i18n.problemText(null, 500, 'Fallback.'), 'Fallback.');
  });

  it('uses the code text, not the detail, when the request was sent in another language', () => {
    const i18n = createI18n({ ...BASE, env: fakeEnv().env, autoStart: false });

    assert.equal(i18n.problemText({ code: 'SLOT_FULLY_BOOKED', detail: 'Old language.' }, 409, 'Fallback.', 'hr'), 'Fully booked.');
    assert.equal(i18n.problemText({ code: 'NOPE', detail: 'Old language.' }, 409, 'Fallback.', 'hr'), 'Fallback.');
  });

  it('looks up the code, or the title when there is no code (code || title)', () => {
    const i18n = createI18n({ ...BASE, env: fakeEnv().env, autoStart: false });

    assert.equal(i18n.problemText({ title: 'SLOT_FULLY_BOOKED' }, 409), 'Fully booked.');
    assert.equal(i18n.problemText({ title: 'Ordinary title.' }, 409, 'Fallback.'), 'Fallback.');
    assert.equal(i18n.problemText({ code: 'NOPE', title: 'SLOT_FULLY_BOOKED' }, 500, 'Fallback.'), 'Fallback.');
  });
});

describe('createI18n: the page and its language switcher', () => {
  it('translates the elements marked data-i18n, and their marked attributes', () => {
    const f = fakeEnv();
    const title = { tag: 'h1', innerHTML: 'Original', attrs: {}, getAttribute(k) { return k === 'data-i18n' ? 'greeting' : k === 'data-i18n-attr' ? null : null; }, setAttribute() {} };
    const input = {
      innerHTML: '', attrs: { placeholder: 'Type' },
      getAttribute(k) { return k === 'data-i18n' ? 'greeting' : k === 'data-i18n-attr' ? 'placeholder' : this.attrs[k] ?? null; },
      setAttribute(k, v) { this.attrs[k] = v; }
    };
    title.setAttribute = () => {};
    f.elements.push(title, input);

    createI18n({ ...BASE, env: f.env, autoStart: false }).start();

    assert.equal(title.innerHTML, 'Hello');
    assert.equal(input.attrs.placeholder, 'Hello');
  });

  it('builds one button per language, marks the current one, and switches on a click', () => {
    const switcherChildren = [];
    const buttons = [];
    const switcher = {
      buttons,
      element: {
        querySelectorAll: () => buttons,
        appendChild(button) { switcherChildren.push(button); buttons.push(button); }
      }
    };
    const f = fakeEnv({ switcher, stored: { lang: 'en' } });
    const i18n = createI18n({ ...BASE, env: f.env, autoStart: false });
    i18n.start();

    assert.equal(switcherChildren.length, 2);
    assert.equal(switcherChildren[0].textContent, 'English');
    assert.equal(switcherChildren[1].textContent, 'Hrvatski');
    switcherChildren[1].dispatchClick();
    assert.equal(i18n.getLang(), 'hr');
  });

  it('waits for the DOM when the document is still loading', () => {
    const f = fakeEnv({ readyState: 'loading' });
    createI18n({ ...BASE, env: f.env, autoStart: false }).start();

    assert.equal(f.listeners['doc:DOMContentLoaded'].length, 1);
  });

  it('installs the page names, with i18nReady becoming true when the texts are ready', () => {
    const f = fakeEnv();
    const i18n = createI18n({ ...BASE, env: f.env, autoStart: false });
    const target = {};
    i18n.exposeGlobals(target);

    assert.equal(typeof target.__, 'function');
    assert.equal(typeof target.__text, 'function');
    assert.equal(typeof target.__problemText, 'function');
    assert.equal(target.__('greeting'), 'Hello');
    assert.equal(target.i18nReady, false);
    f.listeners.i18nReady.forEach((fn) => fn());
    assert.equal(target.i18nReady, true);
    assert.equal(target.i18nTextsReady(), true);
  });
});
