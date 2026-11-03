// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Reads a failed HTTP answer into one line of text for a person to read.
//
// The answer is a problem answer (RFC 9457): the members a server writes are detail, title, message, error, code, errors
// (field to messages, for a validation failure) and traceId. The text is chosen in this order:
//   1. the detail, when preferDetail is set, the status is a 4xx and the answer has a code (the server wrote the detail in
//      the language of the request, so the caller decides when it is safe to show it);
//   2. the text the codeText hook gives for the code, or for the title when the answer has no code (for example, a
//      translation of the code);
//   3. the detail, else the title, the message, the error, else the fallback.
// Field messages are appended, and so is the trace reference.
//
// In a browser this file is a classic <script>: it installs its functions on window.BallastWeb. Under CommonJS (Node tests)
// it exports them instead.
(function () {
  'use strict';

  var root = typeof window !== 'undefined' ? window : globalThis;

  // An HTML page from a proxy or an error page is never shown as a message: it is too long and it is not a sentence.
  var MAX_PLAIN_TEXT = 300;

  function asText(value) {
    return typeof value === 'string' ? value : '';
  }

  /**
   * Reads the body of a failed answer. A JSON object or array comes back parsed. A plain-text body comes back as text when it is
   * short and does not start with '<' (an HTML page is never a message). An empty body, or one that is neither, is null.
   * The body is read once, so the caller reads it here and never again.
   * @param {Response} response
   * @returns {Promise<object|string|null>}
   */
  async function readProblemBody(response) {
    var text;
    try {
      text = await response.text();
    } catch (e) {
      return null;
    }
    if (!text) return null;
    try {
      return JSON.parse(text);
    } catch (e) {
      var plain = text.trim();
      return plain && plain.charAt(0) !== '<' && plain.length <= MAX_PLAIN_TEXT ? plain : null;
    }
  }

  /**
   * The one line of text for a failed answer.
   * @param {number} status the HTTP status of the answer
   * @param {object|string|null} body what readProblemBody returned, or a parsed body
   * @param {{fallback?: string, preferDetail?: boolean, codeText?: function(string, object): (string|null|undefined)}} [options]
   *   fallback: the text when the body says nothing (default "Request failed (status)");
   *   preferDetail: show the detail first for a 4xx answer that has a code (default false);
   *   codeText: the hook that turns an error code into text, or null when it has none. It is asked with the code, or with
   *   the title when the answer has no code.
   * @returns {string}
   */
  function problemMessage(status, body, options) {
    var opt = options || {};
    var fallback = opt.fallback || ('Request failed (' + status + ')');
    if (typeof body === 'string') return body || fallback;
    if (!body || typeof body !== 'object') return fallback;

    var detail = asText(body.detail);
    var code = asText(body.code);
    var title = asText(body.title);
    var base = '';

    if (opt.preferDetail && status >= 400 && status < 500 && code && detail) base = detail;
    // The text hook gets the code, or the title when the answer has no code (code || title).
    var key = code || title;
    if (!base && key && typeof opt.codeText === 'function') {
      var mapped = opt.codeText(key, body);
      if (mapped) base = String(mapped);
    }
    if (!base) {
      base = detail || title || asText(body.message) || asText(body.error) || fallback;
    }

    var errors = body.errors;
    if (errors && typeof errors === 'object') {
      var parts = Object.keys(errors).map(function (key) {
        var messages = Array.isArray(errors[key]) ? errors[key].join('; ') : String(errors[key]);
        return key + ': ' + messages;
      });
      if (parts.length) base += ' — ' + parts.join(' | ');
    }

    var trace = asText(body.traceId);
    if (trace) base += ' (ref ' + trace + ')';
    return base;
  }

  var api = { readProblemBody: readProblemBody, problemMessage: problemMessage };

  if (typeof module === 'object' && module && module.exports) {
    module.exports = api;
  } else {
    root.BallastWeb = Object.assign(root.BallastWeb || {}, api);
  }
})();
