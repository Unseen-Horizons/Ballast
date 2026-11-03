// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The page translation loader: the language a page starts in, the texts of that language, the language switcher, and the
// text for a status code, an error code or a failed answer.
//
// The texts of a language come from a bundled dictionary (for the default language, when the page ships one) or from a JSON
// file named by the language (textsPath + lang + '.json'). A language chosen by the visitor is kept in localStorage and as a
// cookie, and its texts are cached there, so a return visit shows them at once. Until a language's texts are available, t()
// answers null, so a page never draws raw keys.
//
// Nothing here names an application. The languages, the bundled texts, the status and error-code maps and the storage keys
// all come in through the options. createI18n(options) returns the loader; exposeGlobals(window) installs the names pages
// call (__, __text, getLang, __status, __error, __problemText, setLanguage, i18nTextsReady, i18nReady).
//
// In a browser this file is a classic <script>: it installs createI18n on window.BallastWeb. Under CommonJS (Node tests)
// it exports it instead.
(function () {
  'use strict';

  var root = typeof window !== 'undefined' ? window : globalThis;

  function hasOwn(object, key) {
    return Object.prototype.hasOwnProperty.call(object, key);
  }

  function normalizeLangs(list) {
    if (!Array.isArray(list) || list.length === 0) {
      throw new TypeError('createI18n needs a non-empty langs array. The first language is the default.');
    }
    list.forEach(function (lang) {
      if (typeof lang !== 'string' || lang.length === 0) throw new TypeError('Every language code must be a non-empty string.');
    });
    return list.slice();
  }

  /**
   * Creates the loader. It chooses the starting language and loads its texts at once, and it waits for the DOM (or runs at
   * once when the DOM is ready) to translate the page.
   *
   * @param {object} options
   * @param {string[]} options.langs the languages the site has; the first one is the default. Required.
   * @param {object|null} [options.bundled] the texts of bundledLang, when the page ships them. Optional.
   * @param {string} [options.bundledLang] the language of bundled; defaults to the first language.
   * @param {string} [options.textsPath] where the texts are, '/i18n/' by default; the file is textsPath + lang + '.json'.
   * @param {string} [options.langParam] the query parameter that names a language, 'lang' by default.
   * @param {string} [options.storageKey] the localStorage key of the visitor's choice, 'lang' by default.
   * @param {string} [options.cachePrefix] the prefix of the cached texts, 'i18n_' by default.
   * @param {string|null} [options.cookieName] the cookie that carries the choice, 'lang' by default; null or '' for none.
   * @param {object} [options.statusMap] status code to the key of its text.
   * @param {object} [options.errorCodeMap] error code to the key of its text.
   * @param {string} [options.fallbackKey] the key of the text for a failed request, 'msg_request_failed' by default.
   * @param {string} [options.fallbackText] that text's English, 'Request failed.' by default.
   * @param {string} [options.languageKeyPrefix] the keys of the language names in the switcher, 'lang_' by default.
   * @param {string} [options.switcherId] the id of the language switcher element, 'lang-switcher' by default.
   * @param {string} [options.buttonClass] the class of its buttons, 'lang-btn' by default.
   * @param {boolean} [options.events] whether to dispatch i18nReady and languageChanged on the window; true by default.
   * @param {boolean} [options.autoStart] whether to translate the page as soon as the DOM is ready; true by default.
   * @param {object} [options.env] the browser objects, for tests: window, document, storage, navigator, fetch, CustomEvent.
   * @returns {object} the loader: getLang, setLanguage, t, text, status, error, problemText, textsReady, start, exposeGlobals.
   * @throws {TypeError} langs is missing, empty, or holds a value that is not a non-empty string.
   */
  function createI18n(options) {
    var opt = options || {};
    var env = opt.env || {};
    var langs = normalizeLangs(opt.langs);
    var defaultLang = langs[0];
    var bundledLang = opt.bundledLang || defaultLang;
    var bundled = opt.bundled || null;
    var textsPath = opt.textsPath !== undefined ? opt.textsPath : '/i18n/';
    var langParam = opt.langParam || 'lang';
    var storageKey = opt.storageKey || 'lang';
    var cachePrefix = opt.cachePrefix || 'i18n_';
    var cookieName = opt.cookieName === undefined ? 'lang' : opt.cookieName;
    var statusMap = opt.statusMap || {};
    var errorCodeMap = opt.errorCodeMap || {};
    var fallbackKey = opt.fallbackKey || 'msg_request_failed';
    var fallbackText = opt.fallbackText || 'Request failed.';
    var languageKeyPrefix = opt.languageKeyPrefix || 'lang_';
    var switcherId = opt.switcherId || 'lang-switcher';
    var buttonClass = opt.buttonClass || 'lang-btn';
    var events = opt.events !== false;

    var currentLang;
    var translations = {};
    var ready = false;
    var saved = {};
    var languageChangePending = false;

    function windowObject() { return env.window || root; }
    function documentObject() { return env.document || root.document || null; }
    function fetchTexts(url) {
      var doFetch = env.fetch || root.fetch;
      return doFetch(url);
    }

    // Storage can be blocked (a private window, a full quota), so every access is guarded: the choice then lasts for this page.
    function storage() {
      try {
        return env.storage !== undefined ? env.storage : root.localStorage;
      } catch (e) {
        return null;
      }
    }

    function readStorage(key) {
      try {
        var s = storage();
        return s ? s.getItem(key) : null;
      } catch (e) {
        return null;
      }
    }

    function writeStorage(key, value) {
      try {
        var s = storage();
        if (s) s.setItem(key, value);
      } catch (e) {
        // Storage blocked or full: the choice only lasts for this page.
      }
    }

    function setCookie(lang) {
      if (!cookieName) return;
      try {
        var d = documentObject();
        if (d) d.cookie = cookieName + '=' + lang + ';path=/';
      } catch (e) {
        // Cookies blocked: the stored choice is enough.
      }
    }

    function setHtmlLang(lang) {
      var d = documentObject();
      if (d && d.documentElement) d.documentElement.lang = lang;
    }

    function emit(name, detail) {
      if (!events) return;
      var CustomEventType = env.CustomEvent || root.CustomEvent;
      windowObject().dispatchEvent(new CustomEventType(name, detail ? { detail: detail } : undefined));
    }

    // A language named in the page address (?lang=hr), for links written in a language. Null when there is none, or the site
    // does not have that language.
    function langFromUrl() {
      try {
        var search = windowObject().location.search;
        var requested = (new URLSearchParams(search).get(langParam) || '').trim().toLowerCase();
        return langs.indexOf(requested) >= 0 ? requested : null;
      } catch (e) {
        return null;
      }
    }

    function navigatorLanguage() {
      var nav = env.navigator || root.navigator || {};
      return (nav.language || nav.userLanguage || '').substring(0, 2).toLowerCase();
    }

    // The language the page starts in, chosen before any text is: the language of the page address (also saved as the choice,
    // like a click on the switcher), else the saved choice, else the browser's language when the site has it, else the default.
    function pickInitialLang() {
      var requested = langFromUrl();
      if (requested) {
        writeStorage(storageKey, requested);
        setCookie(requested);
        return requested;
      }
      var savedChoice = readStorage(storageKey);
      if (savedChoice && langs.indexOf(savedChoice) >= 0) return savedChoice;
      var browser = navigatorLanguage();
      if (langs.indexOf(browser) >= 0) return browser;
      return defaultLang;
    }

    // The texts available without a request: the copy an earlier visit cached, or, for the bundled language only, the bundled
    // dictionary. Null for any other language, which waits for its file. (Showing the bundled English meanwhile is what made
    // a visitor who prefers another language see English first.)
    function localTexts(lang) {
      var cached = readStorage(cachePrefix + lang);
      if (cached) {
        try {
          var parsed = JSON.parse(cached);
          if (parsed && typeof parsed === 'object') return parsed;
        } catch (e) {
          // A damaged cache is ignored: the file is fetched instead.
        }
      }
      return lang === bundledLang && bundled ? bundled : null;
    }

    // 'languageChanged' tells pages to refetch content the API returns in the UI language. It fires once per setLanguage call,
    // after the new texts are loaded, and not on the first load (i18nReady does that).
    function finishLanguageChange(lang) {
      if (!languageChangePending || lang !== currentLang) return;
      languageChangePending = false;
      emit('languageChanged', { lang: lang });
    }

    function loadFromJson(lang) {
      var url = textsPath + lang + '.json';
      Promise.resolve()
        .then(function () { return fetchTexts(url); })
        .then(function (r) { return r.json(); })
        .then(function (data) {
          if (!data || Object.keys(data).length === 0) throw new Error('empty texts');
          writeStorage(cachePrefix + lang, JSON.stringify(data));
          // A slow answer for a language the visitor has already left must not replace the current texts.
          if (lang !== currentLang) return;
          translations = data;
          ready = true;
          setHtmlLang(lang);
          processDOM();
          emit('i18nReady');
          finishLanguageChange(lang);
        })
        .catch(function () {
          if (lang !== currentLang) return;
          // No texts at all for this language (first visit, file unreachable): the bundled English is better than raw keys.
          if (!ready && bundled) {
            translations = bundled;
            setHtmlLang(bundledLang);
          }
          ready = true;
          processDOM();
          emit('i18nReady');
          finishLanguageChange(lang);
        });
    }

    function applyTexts(lang, texts) {
      languageChangePending = true;
      currentLang = lang;
      writeStorage(storageKey, lang);
      setCookie(lang);
      setHtmlLang(lang);
      if (texts) {
        translations = texts;
        ready = true;
        processDOM();
        emit('i18nReady');
      } else {
        // Nothing to show yet: keep the page as it is until the file arrives (it fires i18nReady), instead of drawing raw keys.
        translations = {};
        ready = false;
      }
    }

    /**
     * Switches the language. A language the site does not have is refused and nothing changes.
     * @param {string} lang
     * @returns {boolean} true when the switch started, false for a language the site does not have.
     */
    function setLanguage(lang) {
      if (langs.indexOf(lang) < 0) return false;
      applyTexts(lang, localTexts(lang));
      loadFromJson(lang);
      return true;
    }

    function getLang() { return currentLang; }

    // The text for a key: null while the texts are loading, else the text, else the key itself.
    function __(key) {
      if (!key) return '';
      if (!ready) return null;
      var value = hasOwn(translations, key) ? translations[key] : undefined;
      return value || key;
    }

    // The text for a key, or english while the texts are loading or the key is missing. Never null.
    function __text(key, english) {
      var t = __(key);
      return t && t !== key ? t : english;
    }

    // The text of a status code (its key comes from statusMap), or the code itself.
    function __status(s) {
      if (!s) return '';
      var key = hasOwn(statusMap, s) ? statusMap[s] : null;
      if (key) return __(key) || s;
      return s;
    }

    // A usable message for an error code: its text in the UI language, else the fallback, else the text for a failed request.
    function __error(code, fallback) {
      if (code && hasOwn(errorCodeMap, code) && errorCodeMap[code]) {
        var key = errorCodeMap[code];
        var t = __(key);
        if (t && t !== key) return t;
      }
      return fallback || __text(fallbackKey, fallbackText);
    }

    /**
     * The text for a failed answer, from its problem body, the HTTP status and the language the request was sent in.
     * A 4xx answer with a code and a detail shows the detail: the server wrote it in that language. Anything else goes through
     * __error. After a language switch the detail is in the old language, so the text of the code is used.
     * @param {object} problem the parsed problem body (may be null)
     * @param {number} status the HTTP status
     * @param {string} [fallback] the text when there is nothing better
     * @param {string} [requestLang] the language the request was sent in
     * @returns {string}
     */
    function __problemText(problem, status, fallback, requestLang) {
      var p = problem && typeof problem === 'object' ? problem : {};
      var sameLang = !requestLang || requestLang === getLang();
      if (sameLang && status >= 400 && status < 500 && p.code && p.detail) return p.detail;
      return __error(p.code || p.title, sameLang ? (p.detail || fallback) : fallback);
    }

    function processDOM() {
      var d = documentObject();
      if (!d) return;
      var els = d.querySelectorAll('[data-i18n]');
      for (var i = 0; i < els.length; i++) {
        var el = els[i];
        var key = el.getAttribute('data-i18n');
        var attr = el.getAttribute('data-i18n-attr');
        if (attr) {
          var orig = el.getAttribute(attr);
          if (!saved[key + '@' + attr]) saved[key + '@' + attr] = orig;
          el.setAttribute(attr, __(key));
        } else {
          if (!saved[key]) saved[key] = el.innerHTML;
          el.innerHTML = __(key);
        }
      }
    }

    function initLanguageSwitcher() {
      var d = documentObject();
      var switcher = d ? d.getElementById(switcherId) : null;
      if (!switcher) return;
      var btns = switcher.querySelectorAll('.' + buttonClass);
      if (btns.length === 0) {
        langs.forEach(function (code) {
          var btn = d.createElement('button');
          btn.className = buttonClass;
          btn.setAttribute('data-lang', code);
          btn.textContent = __(languageKeyPrefix + code) || code.toUpperCase();
          btn.addEventListener('click', function () {
            var lang = this.getAttribute('data-lang');
            var all = switcher.querySelectorAll('.' + buttonClass);
            for (var j = 0; j < all.length; j++) all[j].classList.remove('active');
            this.classList.add('active');
            setLanguage(lang);
          });
          switcher.appendChild(btn);
        });
        btns = switcher.querySelectorAll('.' + buttonClass);
      }
      for (var i = 0; i < btns.length; i++) {
        if (btns[i].getAttribute('data-lang') === currentLang) btns[i].classList.add('active');
      }
    }

    // Translates the page once the DOM is ready: the switcher, the markup it already has, then the texts' file.
    function start() {
      function onDomReady() {
        initLanguageSwitcher();
        // Without texts the page keeps the markup it was written with until the file arrives.
        if (ready) processDOM();
        loadFromJson(currentLang);
      }
      var d = documentObject();
      if (d && d.readyState === 'loading') d.addEventListener('DOMContentLoaded', onDomReady);
      else onDomReady();
    }

    /**
     * Installs the names pages call on a window object. i18nReady is a flag that becomes true when the texts are ready.
     * @param {object} [target] the object to install on; the window by default.
     */
    function exposeGlobals(target) {
      var w = target || windowObject();
      w.__ = __;
      w.__text = __text;
      w.getLang = getLang;
      w.__status = __status;
      w.__error = __error;
      w.__problemText = __problemText;
      w.setLanguage = setLanguage;
      w.i18nTextsReady = function () { return ready; };
      w.i18nReady = false;
      windowObject().addEventListener('i18nReady', function () { w.i18nReady = true; });
    }

    currentLang = pickInitialLang();
    var initial = localTexts(currentLang);
    if (initial) {
      translations = initial;
      ready = true;
    }
    setHtmlLang(currentLang);

    var loader = {
      getLang: getLang,
      setLanguage: setLanguage,
      t: __,
      text: __text,
      status: __status,
      error: __error,
      problemText: __problemText,
      textsReady: function () { return ready; },
      start: start,
      exposeGlobals: exposeGlobals
    };

    if (opt.autoStart !== false) start();
    return loader;
  }

  var api = { createI18n: createI18n };

  if (typeof module === 'object' && module && module.exports) {
    module.exports = api;
  } else {
    root.BallastWeb = Object.assign(root.BallastWeb || {}, api);
  }
})();
