// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Escaping for text and attribute values that a page builds into HTML.
//
// In a browser this file is a classic <script>: it installs its functions on window.BallastWeb. Under CommonJS (Node tests)
// it exports them instead.
(function () {
  'use strict';

  var root = typeof window !== 'undefined' ? window : globalThis;

  var ENTITIES = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' };

  /**
   * Escapes the five characters that matter in HTML text and in a quoted attribute value. Quotes are escaped too, so the
   * same function is safe inside "..." and '...' attributes. A null or undefined value becomes an empty string.
   * @param {*} value
   * @returns {string}
   */
  function escapeHtml(value) {
    if (value === null || value === undefined) return '';
    return String(value).replace(/[&<>"']/g, function (c) { return ENTITIES[c]; });
  }

  /**
   * Returns the value when it is an http or https URL, and '#' for anything else (a javascript: or data: address, a value
   * that does not parse, or an empty value). Escaping does not stop a javascript: href, so a link built from data needs this
   * check as well. A relative value is resolved against base.
   * @param {*} value
   * @param {string} [base] the origin relative values resolve against; defaults to the page's origin in a browser
   * @returns {string}
   */
  function safeUrl(value, base) {
    if (!value) return '#';
    try {
      var origin = base || (root.location ? root.location.origin : undefined);
      var parsed = new URL(String(value), origin);
      return (parsed.protocol === 'http:' || parsed.protocol === 'https:') ? value : '#';
    } catch (e) {
      return '#';
    }
  }

  var api = { escapeHtml: escapeHtml, safeUrl: safeUrl };

  if (typeof module === 'object' && module && module.exports) {
    module.exports = api;
  } else {
    root.BallastWeb = Object.assign(root.BallastWeb || {}, api);
  }
})();
