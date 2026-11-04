// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The admin console's shell: the rail with its navigation, the top bar, the API call helper, pagination, error banners and the
// counts on the navigation.
//
// The shell is built for one console by createShell(options). The console supplies its navigation, its brand, its footer, its
// counts (a function that reads its own endpoints) and, optionally, the endpoint that says which permissions the signed-in person
// has. Nothing here names a product or an endpoint of one.
//
// Navigation items may name a permission (`permission`, or `anyPermission` for any of several). An item whose permission the
// person lacks is not drawn. Until the permissions are known, every item is drawn, and the API still refuses what the person may not do.
//
// In a browser this file is a classic <script>: it installs createShell on window.BallastAdmin. Under CommonJS (Node tests) it
// exports it instead.
(function () {
  'use strict';

  var root = typeof window !== 'undefined' ? window : globalThis;

  function web() {
    if (!root.BallastWeb) throw new Error('@ballast/web must be loaded before @ballast/admin: window.BallastWeb is missing.');
    return root.BallastWeb;
  }

  var LOCAL_HOSTS = /^(localhost|127\.0\.0\.1|\[::1\])$/;

  function defaultEnvironment(hostname) {
    return LOCAL_HOSTS.test(hostname) ? 'LOCAL' : 'PRODUCTION';
  }

  /**
   * Creates the shell for one console.
   * @param {object} options
   * @param {object} options.auth the sign-in state from createAuth. Required.
   * @param {Array<{label: string, items: object[]}>} [options.nav] the navigation groups. An item has key, text and href, and
   *   optionally: count (a data-count key, drawn as a number), badge or pill (a data-count key, drawn only when non-zero), pills
   *   (several of those, each {count, cls, title}), live (a dot), permission, or anyPermission (an array).
   * @param {{mark?: string, name?: string, sub?: string}} [options.brand] the rail's mark, name and sub-line.
   * @param {string} [options.footerHtml] markup for the rail's footer, written by the console. It is trusted markup, not data.
   * @param {boolean} [options.showEnvironment] draws the LOCAL or PRODUCTION badge; true by default.
   * @param {function(string): string} [options.environmentFor] the badge for a host name; by default LOCAL for localhost.
   * @param {function(object): Promise<object>} [options.counts] reads the counts to draw, as { key: number }. It is given the shell.
   * @param {number} [options.countsDelayMs] how long after the page opens the counts are read; 250 by default.
   * @param {string} [options.mePath] the endpoint that answers { permissions: string[], fullAccess: boolean }. Optional.
   * @param {boolean} [options.highlightHash] brings the element named by the address's hash into view; true by default.
   * @param {object} [options.window] window, by default.
   * @param {object} [options.document] document, by default.
   * @param {object} [options.storage] sessionStorage, by default, for the counts.
   * @param {string} [options.countsKey] the storage key of the counts; '_shellCounts' by default.
   * @param {function(string, object): Promise<Response>} [options.fetch] fetch, by default the page's own.
   * @param {object} [options.problem] the options problemMessage takes (fieldErrors, traceId, codeText, preferDetail, fallback),
   *   passed on every error banner the shell writes. None by default, which gives the full text. For httpErrorMessage a given
   *   fallback replaces the 'HTTP <status>' one.
   * @returns {object} the shell
   */
  function createShell(options) {
    var opt = options || {};
    if (!opt.auth) throw new TypeError('createShell needs the sign-in state: pass auth from createAuth.');
    var auth = opt.auth;
    var nav = opt.nav || [];
    var brand = opt.brand || {};
    var showEnvironment = opt.showEnvironment !== false;
    var environmentFor = opt.environmentFor || defaultEnvironment;
    var countsDelayMs = opt.countsDelayMs === undefined ? 250 : opt.countsDelayMs;
    var countsKey = opt.countsKey || '_shellCounts';
    var problem = opt.problem || {};
    var permissions = null;
    var fullAccess = false;
    var mounted = null;
    var shell = null;

    function win() { return opt.window || root; }
    function doc() { return opt.document || root.document; }
    function sessionStore() { return opt.storage || root.sessionStorage; }
    function doFetch(path, init) { return (opt.fetch || root.fetch)(path, init); }

    function can(permission) {
      if (permissions === null || fullAccess) return true;
      return permissions.indexOf(permission) >= 0;
    }

    function visible(item) {
      if (item.permission && !can(item.permission)) return false;
      if (item.anyPermission && !item.anyPermission.some(function (p) { return can(p); })) return false;
      return true;
    }

    /**
     * Sets the permissions the person has, and redraws the navigation if it is drawn. Pass null to draw every item again.
     * @param {string[]|null} list
     * @param {boolean} [everything] true when the person has every permission (a full-access role)
     */
    function setPermissions(list, everything) {
      permissions = Array.isArray(list) ? list.slice() : null;
      fullAccess = everything === true;
      redrawNav();
    }

    /**
     * Reads the permissions from mePath and redraws the navigation with them. A failed read rejects; the navigation stays as it was.
     * @returns {Promise<string[]|null>} the permissions, or null when mePath is not set
     */
    async function loadPermissions() {
      if (!opt.mePath) return null;
      var me = await apiCall(opt.mePath);
      setPermissions(me && me.permissions, me && me.fullAccess === true);
      return permissions;
    }

    function headersFor(init) {
      var merged = Object.assign({}, init || {});
      merged.headers = Object.assign(auth.headers(), merged.headers || {});
      // A multipart body sets its own Content-Type, with the boundary; a JSON one would break the upload.
      if (typeof FormData !== 'undefined' && merged.body instanceof FormData) delete merged.headers['Content-Type'];
      return merged;
    }

    /**
     * Calls the console's API with the token. A 401 renews the token once and retries; a 429 waits for Retry-After once. A failure
     * throws an Error whose status is the HTTP status, so a page can tell a 403 from a server error. A 204 gives null.
     */
    async function apiCall(path, init) {
      var res = await doFetch(path, headersFor(init));

      if (res.status === 401) {
        var refreshed = await auth.refresh();
        if (refreshed) res = await doFetch(path, headersFor(init));
      }
      if (res.status === 401) {
        auth.signOut();
        throw new Error('Session expired.');
      }

      if (res.status === 429) {
        var retry = parseInt(res.headers.get('Retry-After') || '2', 10);
        if (!isNaN(retry) && retry > 0 && retry <= 10) {
          await new Promise(function (r) { setTimeout(r, Math.min(retry, 3) * 1000); });
          res = await doFetch(path, headersFor(init));
          if (res.status === 401) {
            var renewed = await auth.refresh();
            if (renewed) res = await doFetch(path, headersFor(init));
          }
        }
        if (res.status === 429) {
          throw new Error('Too many requests — retry in ' + (res.headers.get('Retry-After') || '60') + 's');
        }
      }

      if (!res.ok) {
        var body = await web().readProblemBody(res);
        var failure = new Error(web().problemMessage(res.status, body, problem));
        failure.status = res.status;
        throw failure;
      }
      if (res.status === 204) return null;
      return res.json();
    }

    /** Same as apiCall, but resolves to null instead of throwing. */
    async function tryApi(path) {
      try { return await apiCall(path); } catch (e) { return null; }
    }

    /**
     * The one line of text for a failed HTTP answer. ASP.NET validation failures carry the useful detail in errors, while the
     * title is generic, so the field messages are appended.
     */
    function httpErrorMessage(status, body) {
      return web().problemMessage(status, body, Object.assign({ fallback: 'HTTP ' + status }, problem));
    }

    function fail(message) {
      var el = doc().getElementById('content');
      if (el) el.innerHTML = '<div class="alert">' + web().escapeHtml(message) + '</div>';
    }

    // The page-level error banner. It hides the spinner too, because a failed load would otherwise leave it on screen. Without an
    // error element the message goes to the content area, so it is never lost.
    function showError(message) {
      var loading = doc().getElementById('loading');
      if (loading && loading.classList) loading.classList.add('d-none');
      var el = doc().getElementById('error');
      if (!el) { fail(message); return; }
      el.textContent = message;
      el.classList.remove('d-none');
    }

    /**
     * Draws a Bootstrap pager. state is { page, totalPages, hidden, summary, container }: hidden is the page's own rule for nothing
     * to page through; summary is the text after "Page X of Y"; container defaults to #pagination. onPage(n) is called with the
     * page the operator chose.
     */
    function renderPagination(state, onPage) {
      var el = state.container || doc().getElementById('pagination');
      if (state.hidden) { el.classList.add('d-none'); return; }
      el.classList.remove('d-none');
      var cur = state.page, total = state.totalPages;
      var pages = [];
      for (var i = Math.max(1, cur - 2); i <= Math.min(total, cur + 2); i++) pages.push(i);
      var esc = web().escapeHtml;
      el.innerHTML = `<nav><ul class="pagination pagination-sm justify-content-center mt-2">
    <li class="page-item ${cur <= 1 ? 'disabled' : ''}"><button class="page-link" data-page="${cur - 1}">Previous</button></li>
    ${cur > 3 ? '<li class="page-item disabled"><span class="page-link">...</span></li>' : ''}
    ${pages.map(p => `<li class="page-item ${p === cur ? 'active' : ''}"><button class="page-link" data-page="${p}">${p}</button></li>`).join('')}
    ${cur < total - 2 ? '<li class="page-item disabled"><span class="page-link">...</span></li>' : ''}
    <li class="page-item ${cur >= total ? 'disabled' : ''}"><button class="page-link" data-page="${cur + 1}">Next</button></li>
  </ul><small class="text-muted d-block text-center">Page ${cur} of ${total} (${esc(state.summary)})</small></nav>`;
      // Assigned, not added, so a redraw never stacks handlers on the same container.
      el.onclick = function (ev) {
        var b = ev.target.closest('button[data-page]');
        if (!b || b.closest('.page-item.disabled')) return;
        onPage(parseInt(b.getAttribute('data-page'), 10));
      };
    }

    function num(value) {
      if (value === null || value === undefined) return '—';
      return Number(value).toLocaleString('en-GB').replace(/,/g, ' ');
    }

    function date(value) {
      var d = web().utcInstant(value);
      return d ? d.toLocaleDateString('en-GB') : '—';
    }

    function dateTime(value) {
      var d = web().utcInstant(value);
      return d ? d.toLocaleString('en-GB', { dateStyle: 'short', timeStyle: 'short' }) : '—';
    }

    function ago(value) {
      var d = web().utcInstant(value);
      if (!d) return '—';
      var minutes = Math.round((Date.now() - d.getTime()) / 60000);
      if (minutes < 1) return 'just now';
      if (minutes < 60) return minutes + ' min ago';
      if (minutes < 60 * 36) return Math.round(minutes / 60) + ' h ago';
      return date(value);
    }

    function initials(text) {
      if (!text) return '··';
      var clean = String(text).split('@')[0].replace(/[._-]+/g, ' ').trim();
      var parts = clean.split(/\s+/).filter(Boolean);
      if (!parts.length) return '··';
      var out = parts.length === 1 ? parts[0].slice(0, 2) : parts[0][0] + parts[1][0];
      return out.toUpperCase();
    }

    function storedCounts() {
      try {
        return JSON.parse(sessionStore().getItem(countsKey) || '{}');
      } catch (e) {
        return {};
      }
    }

    /**
     * Draws the counts. A null or undefined value means the read failed, and the figure already drawn stays. The counts are kept in
     * sessionStorage, merged, so a page that knows fewer keys does not blank the others.
     */
    function applyCounts(counts) {
      var list = counts || {};
      try {
        var stored = storedCounts();
        Object.keys(list).forEach(function (k) {
          if (list[k] !== null && list[k] !== undefined) stored[k] = list[k];
        });
        sessionStore().setItem(countsKey, JSON.stringify(stored));
      } catch (e) {
        // Storage blocked or full: the figures below are still drawn.
      }
      Object.keys(list).forEach(function (key) {
        doc().querySelectorAll('[data-count="' + key + '"]').forEach(function (el) {
          var value = list[key];
          if (value === null || value === undefined) return;
          if (el.dataset.countStyle) {
            if (!value) { el.classList.add('hidden'); return; }
            el.classList.remove('hidden');
            el.textContent = value;
            return;
          }
          el.classList.remove('hidden');
          el.textContent = num(value);
        });
      });
    }

    function restoreCounts() {
      var counts = storedCounts();
      if (Object.keys(counts).length) applyCounts(counts);
    }

    async function fetchCounts() {
      if (!opt.counts) return;
      var counts = await opt.counts(shell);
      if (counts && Object.keys(counts).length) applyCounts(counts);
    }

    function renderGroup(active) {
      var esc = web().escapeHtml;
      return function (group) {
        var items = group.items.filter(visible);
        if (items.length === 0) return '';
        return '<div class="rail-group">' +
          '<div class="rail-label">' + esc(group.label) + '</div>' +
          items.map(function (item) {
            var right = '';
            if (item.count) right = '<span class="nav-count" data-count="' + esc(item.count) + '">—</span>';
            else if (item.badge) right = '<span class="badge is-warn hidden" data-count="' + esc(item.badge) + '" data-count-style="badge"></span>';
            else if (item.pill) right = '<span class="badge is-accent hidden" data-count="' + esc(item.pill) + '" data-count-style="badge"></span>';
            else if (item.live) right = '<span class="dot is-live"></span>';
            if (item.pills) {
              right = item.pills.map(function (p) {
                return '<span class="badge ' + esc(p.cls) + ' hidden" data-count="' + esc(p.count) + '" data-count-style="badge" title="' + esc(p.title) + '"></span>';
              }).join('');
            }
            return '<a class="nav-item' + (item.key === active ? ' is-active' : '') + '" href="' + esc(item.href) + '">' +
              '<span><span class="nav-dot"></span>' + esc(item.text) + '</span>' + right +
            '</a>';
          }).join('') +
        '</div>';
      };
    }

    function railNav(active) {
      return nav.map(renderGroup(active)).join('');
    }

    function redrawNav() {
      if (!mounted) return;
      var navHost = doc().querySelector('.rail-nav');
      if (navHost) navHost.innerHTML = railNav(mounted.active);
    }

    // The address names an element to bring into view, such as "#feed-12". It is highlighted for three seconds.
    function highlightHash() {
      var h = win().location.hash;
      if (!h) return;
      var el = doc().querySelector(h) || doc().getElementById(h.substring(1));
      if (!el) return;
      el.scrollIntoView({ behavior: 'smooth', block: 'center' });
      el.classList.add('is-highlighted');
      setTimeout(function () { el.classList.remove('is-highlighted'); }, 3000);
    }

    function buildShell(page, contentHtml) {
      if (!auth.requireSignedIn()) return false;

      var host = doc().getElementById('shell');
      if (!host) return false;

      var esc = web().escapeHtml;
      var claims = auth.claims() || {};
      var who = claims.email || claims.name || claims.sub || '';
      var environment = showEnvironment ? environmentFor(win().location.hostname) : '';

      host.className = 'shell';
      host.innerHTML =
        '<aside class="rail">' +
          '<div class="rail-head">' +
            '<div class="rail-mark">' + esc(brand.mark || '') + '</div>' +
            '<div class="rail-name"><b>' + esc(brand.name || '') + '</b><span>' + esc(brand.sub || '') + '</span></div>' +
          '</div>' +
          '<nav class="rail-nav">' + railNav(page.active) + '</nav>' +
          '<div class="rail-foot">' +
            (opt.footerHtml || '') +
            (showEnvironment ? '<div class="env-badge">' + esc(environment) + '</div>' : '') +
          '</div>' +
        '</aside>' +
        '<div class="main">' +
          '<header class="topbar">' +
            (page.crumb ? '<span class="crumb">' + esc(page.crumb) + '</span><span class="sep">/</span>' : '') +
            '<h1>' + esc(page.title) + '</h1>' +
            (page.meta ? '<div class="topbar-meta">' + esc(page.meta) + '</div>' : '') +
            '<div class="spacer"></div>' +
            (page.actions || '') +
            '<div class="avatar" id="railAvatar" title="' + esc(who) + ' — sign out">' +
              esc(initials(who)) + '</div>' +
          '</header>' +
          contentHtml +
        '</div>';

      doc().getElementById('railAvatar').addEventListener('click', function () {
        if (win().confirm('Sign out of the admin console?')) auth.signOut();
      });

      mounted = { active: page.active };
      // The counts are restored at once, and read again after a short delay, so the page is not held up by them.
      restoreCounts();
      setTimeout(function () {
        fetchCounts().catch(function (error) { console.error('The navigation counts could not be read:', error); });
      }, countsDelayMs);
      if (opt.highlightHash !== false) setTimeout(highlightHash, 600);
      return true;
    }

    /** Draws the rail and top bar, and an empty #content for the page to fill. */
    function mount(page) {
      return buildShell(page, '<div class="content" id="content"></div>');
    }

    /**
     * Draws the rail and top bar around a page that still renders its own markup, moving that markup inside instead of replacing it.
     * The content div has no id, because the page already owns an element with id="content". #page is never hidden: the worst case is
     * the page as it was before the shell, and never a blank screen.
     */
    function mountLegacy(page) {
      var body = doc().getElementById('page');
      if (!body) return false;
      if (!buildShell(page, '<div class="content is-legacy" data-legacy-content></div>')) return false;

      var target = doc().querySelector('[data-legacy-content]');
      // Moved, not copied: the nodes may already have listeners bound, and re-serialising them would drop every one.
      while (body.firstChild) target.appendChild(body.firstChild);
      body.remove();
      doc().body.classList.add('has-shell');
      return true;
    }

    shell = {
      mount: mount,
      mountLegacy: mountLegacy,
      api: apiCall,
      tryApi: tryApi,
      httpErrorMessage: httpErrorMessage,
      showError: showError,
      fail: fail,
      renderPagination: renderPagination,
      applyCounts: applyCounts,
      restoreCounts: restoreCounts,
      fetchCounts: fetchCounts,
      can: can,
      setPermissions: setPermissions,
      loadPermissions: loadPermissions,
      highlightHash: highlightHash,
      esc: function (value) { return web().escapeHtml(value); },
      safeUrl: function (value) { return web().safeUrl(value, win().location && win().location.origin); },
      num: num,
      date: date,
      dateTime: dateTime,
      ago: ago,
      initials: initials
    };
    return shell;
  }

  var exported = { createShell: createShell };

  if (typeof module === 'object' && module && module.exports) {
    module.exports = exported;
  } else {
    root.BallastAdmin = Object.assign(root.BallastAdmin || {}, exported);
  }
})();
