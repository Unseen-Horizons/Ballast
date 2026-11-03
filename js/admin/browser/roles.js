// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

// The staff and roles page: the roles a console has, what each may do, a read-only card for the rider role, and (when the console
// has one) the directory sync, which says which roles the directory no longer lists.
//
// The page reads and writes through a data adapter, so the same page serves a console whose roles are named (the key is the
// name) and one whose roles are numbered (the key is the id). The adapter is:
//   listRoles()            -> [{ key, name, permissions: string[], fromMicrosoft?: boolean }]
//   listPermissions()      -> [{ key, displayName, description, category }]
//   saveRole(key, perms)   -> resolves when the permissions are saved
//   deleteRole(key)        -> optional; resolves when the role is deleted. Only offered for a role the directory no longer lists.
//   syncStatus()           -> optional; the directory sync's snapshot, or null when it cannot be read
//   runSync()              -> optional; runs the sync and returns its snapshot
// A console without a directory sync leaves out syncStatus and runSync, and the sync panel is not drawn.
//
// A failure is shown as the server words it. A 403 names the permission the page needs, because the server's answer for it has no
// body of its own.
//
// In a browser this file is a classic <script>: it installs createRolesPage on window.BallastAdmin. Under CommonJS (Node tests) it
// exports it instead.
(function () {
  'use strict';

  var root = typeof window !== 'undefined' ? window : globalThis;

  var DEFAULT_TEXT = {
    intro: 'Tick what each role may do, then save.',
    loading: 'Loading roles',
    noRoles: 'No roles yet.',
    fullAccessBadge: 'Full access',
    fullAccessNote: 'Passes every permission.',
    riderNote: 'Set by the application, and read only.',
    noPermissions: 'No permissions.',
    fromDirectory: 'From Microsoft',
    notInDirectory: 'Not in Microsoft',
    save: 'Save',
    remove: 'Delete',
    saved: 'Saved the permissions of %s.',
    deleted: 'Deleted the role %s.',
    deleteConfirm: 'Delete the role %s? Microsoft no longer lists it. Its permissions are removed here. This cannot be undone.',
    forbidden: 'You need the %s permission.',
    syncTitle: 'Microsoft directory sync',
    syncNow: 'Sync now',
    syncLoading: 'Loading the Microsoft sync…',
    syncOff: 'Microsoft sign-in is not set up on this server, so nothing is read from Microsoft.',
    syncNotRun: 'No sync has run on this server copy since it started. Only the copy that runs the background jobs syncs, so another copy may hold the last run.',
    syncRoles: 'Roles Microsoft no longer lists (kept here, their permissions unchanged)',
    problems: 'Problems (listed, never applied)',
    secretHint: 'Create a new client secret before it ends.',
    result: 'Result',
    lastRun: 'Last run',
    lastSuccess: 'last success',
    usersAssigned: 'Users assigned in Microsoft',
    appRoles: 'app roles',
    accountsUpdated: 'accounts updated',
    switchedOff: 'switched off',
    secretEnds: 'Client secret ends',
    never: 'never',
    unknown: 'unknown'
  };

  var DEFAULT_PERMISSION_NAMES = {
    view: 'View roles',
    manage: 'Manage roles',
    viewUsers: 'View users',
    manageUsers: 'Manage users'
  };

  var DEFAULT_RESULTS = {
    NotRun: 'Not run on this server copy since it started',
    Off: 'Off: Microsoft sign-in is not set up',
    Applied: 'Applied',
    NotApplied: 'Not applied: it would have switched off every Microsoft account, so it looked wrong',
    Failed: 'Failed'
  };

  var DEFAULT_FAILURES = {
    Token: 'Microsoft refused the sign-in of the sync. Check the client secret.',
    Graph: 'Microsoft Graph refused the read. Check the app permissions and their admin consent.',
    Timeout: 'Microsoft did not answer in time.',
    Response: 'Microsoft answered in a shape this console does not read.',
    Unexpected: 'The sync failed inside this console. The server log has the details.'
  };

  var DEFAULT_SECRET_STATES = {
    Ok: ['Ok', 'bg-success'],
    Soon: ['Ends soon', 'bg-warning text-dark'],
    Expired: ['Expired', 'bg-danger'],
    Unknown: ['Unknown', 'bg-secondary']
  };

  function format(template, value) {
    return String(template).replace('%s', value);
  }

  function sameName(a, b) {
    return String(a).toLowerCase() === String(b).toLowerCase();
  }

  /**
   * The message for a failed request. A 403 names the permission the page needs; anything else is the server's own text.
   * @param {Error & {status?: number}} error
   * @param {string} permission the name of the permission the request needs
   * @param {object} labels the page's wording
   * @returns {string}
   */
  function explainError(error, permission, labels) {
    return error.status === 403 ? format(labels.forbidden, permission) : error.message;
  }

  /**
   * The roles in their order: the full-access role first, then the rider role, then the rest by name.
   */
  function sortRoles(roles, riderRole, fullAccessRole) {
    function rank(role) {
      if (fullAccessRole && sameName(role.name, fullAccessRole)) return 0;
      if (riderRole && sameName(role.name, riderRole)) return 1;
      return 2;
    }
    return roles.slice().sort(function (a, b) {
      return rank(a) - rank(b) || String(a.name).localeCompare(String(b.name));
    });
  }

  function permissionLabel(permissions, key) {
    var found = permissions.filter(function (p) { return p.key === key; })[0];
    return found ? found.displayName : key;
  }

  function isInDirectoryGap(snapshot, name) {
    return !!snapshot && (snapshot.rolesNotInMicrosoft || []).some(function (listed) { return sameName(listed, name); });
  }

  // The catalogue's permissions in the order the server gives them, grouped by category. A box is ticked when the role has that
  // permission, and its description is the tooltip.
  function permissionBoxes(granted, permissions, esc) {
    var boxes = '';
    var category = null;
    permissions.forEach(function (p) {
      if (p.category !== category) {
        category = p.category;
        boxes += '<div class="fw-semibold text-muted mt-2 mb-1">' + esc(category) + '</div>';
      }
      boxes += '<label class="form-check d-block mb-1" title="' + esc(p.description) + '">' +
        '<input class="form-check-input" type="checkbox" data-perm="' + esc(p.key) + '"' + (granted.indexOf(p.key) >= 0 ? ' checked' : '') + '>' +
        '<span class="form-check-label">' + esc(p.displayName) + '</span></label>';
    });
    return boxes;
  }

  /**
   * One role's card.
   * @param {object} role {key, name, permissions, fromMicrosoft}
   * @param {object} ctx {esc, labels, permissions, snapshot, riderRole, fullAccessRole, canDelete}
   * @returns {string}
   */
  function roleCard(role, ctx) {
    var esc = ctx.esc;
    var labels = ctx.labels;
    var name = esc(role.name);

    if (ctx.fullAccessRole && sameName(role.name, ctx.fullAccessRole)) {
      return '<div class="card mb-3">' +
        '<div class="card-header d-flex align-items-center gap-2 flex-wrap">' +
          '<span class="fw-semibold">' + name + '</span>' +
          '<span class="badge bg-success">' + esc(labels.fullAccessBadge) + '</span>' +
        '</div>' +
        '<div class="card-body small"><p class="mb-0">' + esc(labels.fullAccessNote) + '</p></div>' +
      '</div>';
    }

    if (ctx.riderRole && sameName(role.name, ctx.riderRole)) {
      var riderItems = (role.permissions || []).map(function (key) {
        return '<li>' + esc(permissionLabel(ctx.permissions, key)) + '</li>';
      }).join('') || '<li class="text-muted">' + esc(labels.noPermissions) + '</li>';
      return '<div class="card mb-3">' +
        '<div class="card-header d-flex align-items-center gap-2 flex-wrap"><span class="fw-semibold">' + name + '</span></div>' +
        '<div class="card-body small">' +
          '<p class="mb-2">' + esc(labels.riderNote) + '</p>' +
          '<ul class="mb-0 ps-3">' + riderItems + '</ul>' +
        '</div>' +
      '</div>';
    }

    var gone = isInDirectoryGap(ctx.snapshot, role.name);
    var fromDirectory = role.fromMicrosoft ? '<span class="badge bg-info text-dark">' + esc(labels.fromDirectory) + '</span>' : '';
    var notListed = gone ? '<span class="badge bg-warning text-dark">' + esc(labels.notInDirectory) + '</span>' : '';
    var deleteButton = gone && ctx.canDelete
      ? '<button type="button" class="btn btn-sm btn-outline-danger" data-delete>' + esc(labels.remove) + '</button>'
      : '';
    return '<div class="card mb-3" data-role-key="' + esc(role.key) + '">' +
      '<div class="card-header d-flex align-items-center gap-2 flex-wrap">' +
        '<span class="fw-semibold">' + name + '</span>' + fromDirectory + notListed +
      '</div>' +
      '<div class="card-body small">' +
        permissionBoxes(role.permissions || [], ctx.permissions, esc) +
        '<div class="d-flex gap-2 mt-3">' +
          '<button type="button" class="btn btn-sm btn-primary" data-save>' + esc(labels.save) + '</button>' +
          deleteButton +
        '</div>' +
      '</div>' +
    '</div>';
  }

  /**
   * The body of the sync panel, from the snapshot the sync wrote.
   * @param {object} s the snapshot
   * @param {object} ctx {esc, labels, results, failures, secretStates, dateTime, date}
   * @returns {string}
   */
  function syncBody(s, ctx) {
    var esc = ctx.esc;
    var labels = ctx.labels;
    if (s.result === 'Off') return '<p class="text-muted mb-0">' + esc(labels.syncOff) + '</p>';
    if (s.result === 'NotRun') return '<p class="text-muted mb-0">' + esc(labels.syncNotRun) + '</p>';

    var counts = s.counts || {};
    var result = esc(ctx.results[s.result] || s.result);
    var failure = s.result === 'Failed' ? ' — ' + esc(ctx.failures[s.failure] || 'The sync did not finish.') : '';
    var httpStatus = s.failureStatus ? ' (HTTP ' + esc(s.failureStatus) + ')' : '';
    var attempt = esc(s.lastAttemptUtc ? ctx.dateTime(s.lastAttemptUtc) : labels.never);
    var success = esc(s.lastSuccessUtc ? ctx.dateTime(s.lastSuccessUtc) : labels.never);

    var problems = (s.problems || []).map(function (p) {
      return '<li><span class="font-monospace">' + esc(p.code) + '</span> ' + esc(p.subject) + '</li>';
    }).join('');
    var problemList = problems
      ? '<div class="mt-2"><div class="text-muted mb-1">' + esc(labels.problems) + '</div><ul class="mb-0">' + problems + '</ul></div>'
      : '';

    var gaps = (s.rolesNotInMicrosoft || []).map(function (name) {
      return '<span class="badge bg-warning text-dark me-1">' + esc(name) + ' · ' + esc(labels.notInDirectory) + '</span>';
    }).join('');
    var roleList = gaps
      ? '<div class="mt-2"><div class="text-muted mb-1">' + esc(labels.syncRoles) + '</div>' + gaps + '</div>'
      : '';

    var secret = ctx.secretStates[s.secretState] || ctx.secretStates.Unknown;
    var secretEnds = esc(s.secretEndsUtc ? ctx.date(s.secretEndsUtc) : labels.unknown);
    var secretHint = s.secretState === 'Soon' || s.secretState === 'Expired'
      ? '<div class="text-warning mt-1">' + esc(labels.secretHint) + '</div>'
      : '';

    return '<div class="mb-2"><span class="text-muted">' + esc(labels.result) + '</span> ' + result + failure + httpStatus + '</div>' +
      '<div class="mb-2"><span class="text-muted">' + esc(labels.lastRun) + '</span> ' + attempt +
        ' <span class="text-muted">· ' + esc(labels.lastSuccess) + '</span> ' + success + '</div>' +
      '<div class="mb-2"><span class="text-muted">' + esc(labels.usersAssigned) + '</span> ' + esc(counts.usersAssigned || 0) +
        ' <span class="text-muted">· ' + esc(labels.appRoles) + '</span> ' + esc(counts.appRoles || 0) +
        ' <span class="text-muted">· ' + esc(labels.accountsUpdated) + '</span> ' + esc(counts.updated || 0) +
        ' <span class="text-muted">· ' + esc(labels.switchedOff) + '</span> ' + esc(counts.switchedOff || 0) + '</div>' +
      '<div class="mb-0"><span class="text-muted">' + esc(labels.secretEnds) + '</span> ' + secretEnds +
        ' <span class="badge ' + esc(secret[1]) + '">' + esc(secret[0]) + '</span>' + secretHint + '</div>' +
      problemList + roleList;
  }

  /**
   * The sync panel: its result, or the note of a failed read, with the Sync now button.
   */
  function syncPanel(snapshot, note, ctx) {
    var esc = ctx.esc;
    var noteHtml = note ? '<div class="alert alert-danger mb-2">' + esc(note) + '</div>' : '';
    var body = snapshot
      ? syncBody(snapshot, ctx)
      : (note ? '' : '<p class="text-muted mb-0">' + esc(ctx.labels.syncLoading) + '</p>');
    return '<div class="card">' +
      '<div class="card-header d-flex align-items-center gap-2 flex-wrap">' +
        '<span class="fw-semibold small">' + esc(ctx.labels.syncTitle) + '</span>' +
        '<div class="ms-auto"><button type="button" class="btn btn-sm btn-outline-secondary" data-sync-now>' +
          esc(ctx.labels.syncNow) + '</button></div>' +
      '</div>' +
      '<div class="card-body small">' + noteHtml + body + '</div>' +
    '</div>';
  }

  /**
   * Builds the page. Nothing is drawn until start() is called.
   * @param {object} options
   * @param {object} options.shell the shell from createShell. Required.
   * @param {object} options.adapter the data adapter (see the top of this file). Required.
   * @param {object} options.page the top bar: {active, title, crumb?, meta?, actions?}. Required.
   * @param {string|null} [options.riderRole] the role the app sets, shown read only. Null for none.
   * @param {string|null} [options.fullAccessRole] the role that passes every permission. Null for none.
   * @param {object} [options.text] the wording of the page; it replaces the defaults one by one.
   * @param {object} [options.permissionNames] the names of the permissions the page needs, for the 403 message.
   * @param {object} [options.results] the sync's result names; replaces the defaults one by one.
   * @param {object} [options.failures] the sync's failure names; replaces the defaults one by one.
   * @param {object} [options.secretStates] the client secret states; replaces the defaults one by one.
   * @param {object} [options.document] document, by default.
   * @returns {{start: function(): Promise<void>}}
   */
  function createRolesPage(options) {
    var opt = options || {};
    if (!opt.shell) throw new TypeError('createRolesPage needs the shell from createShell.');
    if (!opt.adapter || typeof opt.adapter.listRoles !== 'function' || typeof opt.adapter.saveRole !== 'function') {
      throw new TypeError('createRolesPage needs an adapter with listRoles and saveRole.');
    }
    var shell = opt.shell;
    var adapter = opt.adapter;
    var labels = Object.assign({}, DEFAULT_TEXT, opt.text || {});
    var names = Object.assign({}, DEFAULT_PERMISSION_NAMES, opt.permissionNames || {});
    var results = Object.assign({}, DEFAULT_RESULTS, opt.results || {});
    var failures = Object.assign({}, DEFAULT_FAILURES, opt.failures || {});
    var secretStates = Object.assign({}, DEFAULT_SECRET_STATES, opt.secretStates || {});
    var hasSync = typeof adapter.syncStatus === 'function' && typeof adapter.runSync === 'function';
    var canDelete = typeof adapter.deleteRole === 'function';
    var esc = function (value) { return shell.esc(value); };

    var state = { roles: null, rolesError: '', permissions: null, snapshot: null, syncNote: '' };
    var rolesByKey = {};

    function doc() { return opt.document || root.document; }
    function ctx() {
      return {
        esc: esc, labels: labels, permissions: state.permissions || [], snapshot: state.snapshot,
        riderRole: opt.riderRole || null, fullAccessRole: opt.fullAccessRole || null, canDelete: canDelete,
        results: results, failures: failures, secretStates: secretStates,
        dateTime: function (v) { return shell.dateTime(v); }, date: function (v) { return shell.date(v); }
      };
    }

    function setNotice(message, kind) {
      var host = doc().getElementById('notice');
      if (host) host.innerHTML = message ? '<div class="alert alert-' + kind + ' py-2 mb-3">' + esc(message) + '</div>' : '';
    }

    function renderRoles() {
      var host = doc().getElementById('roles');
      if (!host) return;
      if (state.rolesError) {
        host.innerHTML = '<div class="alert alert-danger mb-0">' + esc(state.rolesError) + '</div>';
        return;
      }
      if (!state.roles || !state.permissions) return;
      rolesByKey = {};
      state.roles.forEach(function (role) { rolesByKey[String(role.key)] = role; });
      var c = ctx();
      host.innerHTML = state.roles.length
        ? sortRoles(state.roles, c.riderRole, c.fullAccessRole).map(function (role) { return roleCard(role, c); }).join('')
        : '<div class="alert alert-info mb-0">' + esc(labels.noRoles) + '</div>';
    }

    function renderSync() {
      var host = doc().getElementById('sync');
      if (host) host.innerHTML = syncPanel(state.snapshot, state.syncNote, ctx());
    }

    async function loadRoles() {
      try {
        var pair = await Promise.all([adapter.listRoles(), adapter.listPermissions ? adapter.listPermissions() : []]);
        state.roles = pair[0];
        state.permissions = pair[1];
        state.rolesError = '';
      } catch (e) {
        state.rolesError = explainError(e, names.view, labels);
      }
      renderRoles();
    }

    // The sync snapshot says which roles the directory no longer lists. If it cannot be read, the roles still show, without that badge.
    async function loadSync() {
      if (!hasSync) return;
      try {
        state.snapshot = await adapter.syncStatus();
        state.syncNote = '';
      } catch (e) {
        state.snapshot = null;
        state.syncNote = explainError(e, names.viewUsers, labels);
      }
      renderSync();
      renderRoles();
    }

    async function runSync() {
      var button = doc().querySelector('[data-sync-now]');
      if (button) button.disabled = true;
      try {
        state.snapshot = await adapter.runSync();
        state.syncNote = '';
      } catch (e) {
        // 409 (this copy does not run the jobs) and 422 (not set up) are shown as the server words them.
        state.syncNote = explainError(e, names.manageUsers, labels);
      }
      renderSync();
      await loadRoles(); // a run can create roles, and can list roles as not in the directory
    }

    function roleFromCard(card) {
      return rolesByKey[card.getAttribute('data-role-key')] || null;
    }

    async function saveRole(card) {
      var role = card && roleFromCard(card);
      if (!role) return;
      var button = card.querySelector('[data-save]');
      var permissions = Array.prototype.map.call(card.querySelectorAll('input[data-perm]:checked'), function (box) {
        return box.getAttribute('data-perm');
      });
      if (button) button.disabled = true;
      try {
        await adapter.saveRole(role.key, permissions);
        setNotice(format(labels.saved, role.name), 'success');
        await loadRoles();
      } catch (e) {
        setNotice(explainError(e, names.manage, labels), 'danger');
        if (button) button.disabled = false;
      }
    }

    async function deleteRole(card) {
      var role = card && roleFromCard(card);
      if (!role || !canDelete) return;
      if (!root.confirm(format(labels.deleteConfirm, role.name))) return;
      try {
        await adapter.deleteRole(role.key);
        setNotice(format(labels.deleted, role.name), 'success');
        await loadRoles();
      } catch (e) {
        setNotice(explainError(e, names.manage, labels), 'danger');
      }
    }

    /**
     * Draws the page and loads the roles, the permissions and the sync. The shell's own check runs first: an operator who is not signed
     * in is sent to the sign-in page, and nothing is drawn.
     */
    async function start() {
      if (!shell.mount(opt.page)) return;
      var content = doc().getElementById('content');
      content.innerHTML =
        '<p class="text-muted small mb-3">' + esc(labels.intro) + '</p>' +
        '<div id="notice"></div>' +
        (hasSync ? '<div id="sync" class="mb-3"></div>' : '') +
        '<div id="roles"><div class="text-center py-4 text-muted">' + esc(labels.loading) + '</div></div>';

      // The buttons are redrawn with the cards, so one listener on the content handles them all.
      content.addEventListener('click', function (e) {
        var save = e.target.closest('[data-save]');
        if (save) { saveRole(save.closest('[data-role-key]')); return; }
        var remove = e.target.closest('[data-delete]');
        if (remove) { deleteRole(remove.closest('[data-role-key]')); return; }
        if (hasSync && e.target.closest('[data-sync-now]')) runSync();
      });

      await Promise.all([loadRoles(), loadSync()]);
    }

    return { start: start };
  }

  var exported = {
    createRolesPage: createRolesPage,
    sortRoles: sortRoles,
    explainError: explainError,
    roleCard: roleCard,
    permissionBoxes: permissionBoxes,
    syncBody: syncBody,
    syncPanel: syncPanel
  };

  if (typeof module === 'object' && module && module.exports) {
    module.exports = exported;
  } else {
    root.BallastAdmin = Object.assign(root.BallastAdmin || {}, exported);
  }
})();
