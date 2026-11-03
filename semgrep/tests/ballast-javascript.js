// Test targets for semgrep/ballast-javascript.yml. No page loads this file.
// A line marked "ruleid: <id>" must be flagged by that rule. A line marked "ok: <id>" must not be flagged by it.

function render(el, userText, message, token) {
  // ruleid: ballast-javascript-innerhtml-non-literal
  el.innerHTML = userText;

  // ok: ballast-javascript-innerhtml-non-literal
  el.innerHTML = '<b>Saved</b>';

  // ruleid: ballast-javascript-insert-adjacent-html
  el.insertAdjacentHTML('beforeend', message);

  // ok: ballast-javascript-insert-adjacent-html
  el.insertAdjacentHTML('beforeend', '<i>ok</i>');
}

function write() {
  // ruleid: ballast-javascript-document-write
  document.write('<p>hi</p>');

  // ok: ballast-javascript-document-write
  console.log('hi');
}

function run(code, text) {
  // ruleid: ballast-javascript-eval
  eval(code);

  // ruleid: ballast-javascript-eval
  const fn = new Function('a', 'return a');

  // ok: ballast-javascript-eval
  const value = JSON.parse(text);
  return [fn, value];
}

function notify(win) {
  // ruleid: ballast-javascript-postmessage-wildcard
  win.postMessage({ type: 'ready' }, '*');

  // ok: ballast-javascript-postmessage-wildcard
  win.postMessage({ type: 'ready' }, 'https://app.example.test');
}

function remember(accessToken) {
  // ruleid: ballast-javascript-token-in-web-storage
  localStorage.setItem('authToken', accessToken);

  // ok: ballast-javascript-token-in-web-storage
  localStorage.setItem('theme', 'dark');
}
