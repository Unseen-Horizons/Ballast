// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { describe, it } from 'node:test';

const require = createRequire(import.meta.url);
const { problemMessage, readProblemBody } = require('../browser/problem.js');

// A minimal stand-in for a fetch Response: only text() is used.
function answer(text) {
  return { text: async () => text };
}

describe('readProblemBody', () => {
  it('parses a JSON object body', async () => {
    const body = await readProblemBody(answer('{"title":"Bad","status":400,"code":"X"}'));
    assert.deepEqual(body, { title: 'Bad', status: 400, code: 'X' });
  });

  it('gives null for an empty body', async () => {
    assert.equal(await readProblemBody(answer('')), null);
  });

  it('gives a short plain-text body as text', async () => {
    assert.equal(await readProblemBody(answer('  Gateway timeout  ')), 'Gateway timeout');
  });

  it('gives null for an HTML page, which is never shown as a message', async () => {
    assert.equal(await readProblemBody(answer('<html><body>502 Bad Gateway</body></html>')), null);
  });

  it('gives null for a plain-text body that is too long to be a sentence', async () => {
    assert.equal(await readProblemBody(answer('x'.repeat(301))), null);
  });

  it('gives null when the body cannot be read at all', async () => {
    const broken = { text: async () => { throw new Error('stream closed'); } };
    assert.equal(await readProblemBody(broken), null);
  });
});

describe('problemMessage', () => {
  it('uses the detail first, then the title, the message and the error', () => {
    assert.equal(problemMessage(400, { detail: 'Detail.', title: 'Title.' }), 'Detail.');
    assert.equal(problemMessage(400, { title: 'Title.', message: 'Message.' }), 'Title.');
    assert.equal(problemMessage(400, { message: 'Message.', error: 'Error.' }), 'Message.');
    assert.equal(problemMessage(400, { error: 'Error.' }), 'Error.');
  });

  it('gives the fallback, else "Request failed (status)", when the body says nothing', () => {
    assert.equal(problemMessage(500, null), 'Request failed (500)');
    assert.equal(problemMessage(500, {}), 'Request failed (500)');
    assert.equal(problemMessage(503, null, { fallback: 'The service is busy.' }), 'The service is busy.');
  });

  it('gives a plain-text body as it is', () => {
    assert.equal(problemMessage(409, 'Already done.'), 'Already done.');
  });

  it('ignores a field that is not text', () => {
    assert.equal(problemMessage(400, { detail: { nested: true }, title: 'Title.' }), 'Title.');
  });

  it('appends the field messages of a validation failure, and the trace reference', () => {
    const body = {
      title: 'One or more validation errors occurred.',
      errors: { Email: ['Required.'], Name: ['Too long.', 'Not allowed.'] },
      traceId: '00-abc-01'
    };
    assert.equal(problemMessage(400, body),
      'One or more validation errors occurred. — Email: Required. | Name: Too long.; Not allowed. (ref 00-abc-01)');
  });

  it('gives the trace reference with a detail that has no field messages', () => {
    assert.equal(problemMessage(500, { detail: 'Failed.', traceId: 't1' }), 'Failed. (ref t1)');
  });

  it('uses the text of the code from the code hook when the answer has a code', () => {
    const seen = [];
    const text = problemMessage(409, { code: 'SLOT_FULLY_BOOKED', detail: 'Full.' }, {
      codeText: (code, body) => { seen.push([code, body.detail]); return 'Fully booked.'; }
    });
    assert.equal(text, 'Fully booked.');
    assert.deepEqual(seen, [['SLOT_FULLY_BOOKED', 'Full.']]);
  });

  it('falls back to the detail when the code hook has no text for the code', () => {
    const text = problemMessage(409, { code: 'UNKNOWN', detail: 'Detail.' }, { codeText: () => null });
    assert.equal(text, 'Detail.');
  });

  it('does not call the code hook when the answer has no code', () => {
    let called = false;
    const text = problemMessage(400, { detail: 'Detail.' }, { codeText: () => { called = true; return 'x'; } });
    assert.equal(text, 'Detail.');
    assert.equal(called, false);
  });

  it('shows the detail first for a 4xx answer with a code, when preferDetail is set', () => {
    const body = { code: 'PHONE_REQUIRED', detail: 'Enter a phone number.' };
    assert.equal(problemMessage(400, body, { preferDetail: true, codeText: () => 'Phone required.' }), 'Enter a phone number.');
  });

  it('does not show the detail first for a 5xx answer, or without preferDetail', () => {
    const body = { code: 'PHONE_REQUIRED', detail: 'Enter a phone number.' };
    assert.equal(problemMessage(500, body, { preferDetail: true, codeText: () => 'Phone required.' }), 'Phone required.');
    assert.equal(problemMessage(400, body, { codeText: () => 'Phone required.' }), 'Phone required.');
  });

  it('asks the code hook with the title when the answer has no code (code || title)', () => {
    const seen = [];
    const text = problemMessage(409, { title: 'SOME_CODE', detail: 'Server detail.' }, {
      codeText: (key, body) => { seen.push([key, body.detail]); return key === 'SOME_CODE' ? 'Mapped text.' : null; }
    });
    assert.equal(text, 'Mapped text.');
    assert.deepEqual(seen, [['SOME_CODE', 'Server detail.']]);
  });

  it('keeps an ordinary title when the code hook has no text for it', () => {
    const text = problemMessage(400, { title: 'Ordinary title.' }, { codeText: () => null });
    assert.equal(text, 'Ordinary title.');
  });

  it('asks the code hook with the code, not the title, when the answer has both', () => {
    const seen = [];
    problemMessage(409, { code: 'SOME_CODE', title: 'Conflict.' }, {
      codeText: (key) => { seen.push(key); return 'Mapped text.'; }
    });
    assert.deepEqual(seen, ['SOME_CODE']);
  });
});
