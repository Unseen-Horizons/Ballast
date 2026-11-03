// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The server's instants, read safely in a browser.
//
// In a browser this file is a classic <script>: it installs its functions on window.BallastWeb. Under CommonJS (Node tests)
// it exports them instead.
(function () {
  'use strict';

  var root = typeof window !== 'undefined' ? window : globalThis;

  /**
   * A server instant as a Date, or null when the value is empty or is not a date.
   *
   * The server writes its instants without a zone designator, and they are UTC, so a value with a time part and no designator
   * gets a 'Z'. A date-only value (a place's day, such as a price's as-of date) is not given a time: it is read as the day it
   * is, which is what 'yyyy-MM-dd' means. Any value with an offset is read as it is.
   * @param {*} value
   * @returns {Date|null}
   */
  function utcInstant(value) {
    if (!value) return null;
    var text = String(value);
    if (/T\d{2}:\d{2}/.test(text) && !/(Z|[+-]\d{2}:?\d{2})$/i.test(text)) text += 'Z';
    var d = new Date(text);
    return isNaN(d) ? null : d;
  }

  var api = { utcInstant: utcInstant };

  if (typeof module === 'object' && module && module.exports) {
    module.exports = api;
  } else {
    root.BallastWeb = Object.assign(root.BallastWeb || {}, api);
  }
})();
