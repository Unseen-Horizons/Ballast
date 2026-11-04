// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ballast.Jobs;

/// <summary>
/// Decides which copy of an app runs the background work that writes, with one SQL Server application lock.
/// <para>
/// Registered first among the hosted services, so it starts first and stops last. A copy that does not hold the
/// lock keeps asking for it; the moment the holder lets go, SQL grants it. A term runs in this order: the lock is
/// acquired, the term-start hooks run (<see cref="IsHolder"/> is still false and the heartbeat is already
/// running), then <see cref="IsHolder"/> turns true and the term's token is handed to everything waiting in
/// <see cref="WaitForLeadershipAsync"/>.
/// </para>
/// <para>
/// A heartbeat checks every few seconds that the session still holds the lock. Anything else is a lost term:
/// the token is cancelled, the connection is closed, and the copy goes back to asking.
/// </para>
/// <para>
/// At shutdown <see cref="IsHolder"/> is cleared and the term's token cancelled the moment the application starts
/// stopping, so every holder-only worker begins to wind down at once and in parallel. The lock itself is released
/// only in this service's own <see cref="StopAsync"/>, which the host runs after every other service has stopped:
/// the next copy cannot take over while the old one still has work in flight.
/// </para>
/// </summary>
public sealed class JobLeadership : BackgroundService, IJobLeadership
{
    private readonly JobLeadershipOptions _options;
    private readonly string _resource;
    private readonly Func<IAppLockSession> _sessionFactory;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IServiceProvider _services;
    private readonly TimeProvider _time;
    private readonly ILogger<JobLeadership> _logger;
    private readonly DateTime _startedUtc;

    private readonly object _gate = new();
    private TaskCompletionSource<CancellationToken> _nextTerm = NewWaiter();
    private CancellationTokenSource? _term;
    private bool _stopping;
    private bool _isHolder;
    private DateTime? _holderSinceUtc;
    private DateTime? _lastAttemptUtc;
    private JobLeadershipOutcome _lastOutcome = JobLeadershipOutcome.NotStarted;
    private string? _lastError;

    // The session whose lock is released in StopAsync. Set when the loop ends because the host is stopping.
    private IAppLockSession? _held;

    /// <summary>Creates the service. <see cref="JobLeadershipRegistration.AddJobLeadership"/> is the normal way to make one.</summary>
    /// <param name="options">The timing settings. Read once, when the service is created.</param>
    /// <param name="resourceName">The application-lock resource both copies compete for.</param>
    /// <param name="sessionFactory">Makes one lock session per attempt.</param>
    /// <param name="lifetime">The host's lifetime, whose stopping ends the terms.</param>
    /// <param name="services">Resolves the term-start hooks when a term begins.</param>
    /// <param name="time">The clock for the heartbeat and the retry delays.</param>
    /// <param name="logger">Receives the lock's log lines.</param>
    public JobLeadership(
        JobLeadershipOptions options,
        string resourceName,
        Func<IAppLockSession> sessionFactory,
        IHostApplicationLifetime lifetime,
        IServiceProvider services,
        TimeProvider time,
        ILogger<JobLeadership> logger)
    {
        _options = options;
        _resource = resourceName;
        _sessionFactory = sessionFactory;
        _lifetime = lifetime;
        _services = services;
        _time = time;
        _logger = logger;
        _startedUtc = time.GetUtcNow().UtcDateTime;
    }

    /// <inheritdoc />
    public bool Enabled => true;

    /// <inheritdoc />
    public bool IsHolder
    {
        get
        {
            lock (_gate)
            {
                return _isHolder;
            }
        }
    }

    /// <inheritdoc />
    public DateTime? HolderSinceUtc
    {
        get
        {
            lock (_gate)
            {
                return _holderSinceUtc;
            }
        }
    }

    /// <inheritdoc />
    public DateTime? LastAttemptUtc
    {
        get
        {
            lock (_gate)
            {
                return _lastAttemptUtc;
            }
        }
    }

    /// <inheritdoc />
    public JobLeadershipOutcome LastOutcome
    {
        get
        {
            lock (_gate)
            {
                return _lastOutcome;
            }
        }
    }

    /// <inheritdoc />
    public string? LastError
    {
        get
        {
            lock (_gate)
            {
                return _lastError;
            }
        }
    }

    /// <inheritdoc />
    public DateTime StartedUtc => _startedUtc;

    /// <inheritdoc />
    public Task<CancellationToken> WaitForLeadershipAsync(CancellationToken ct)
    {
        Task<CancellationToken> waiting;
        lock (_gate)
        {
            // A term that is being ended is never handed out: work that ended with it would be started again at once,
            // end at once and ask again, a loop that never waits.
            if (_term is { } current && !current.IsCancellationRequested)
            {
                return Task.FromResult(current.Token);
            }

            waiting = _nextTerm.Task;
        }
        return waiting.WaitAsync(ct);
    }

    /// <inheritdoc />
    public Task RunAsLeaderAsync(Func<CancellationToken, Task> work, bool once, CancellationToken ct) =>
        JobLeadershipLoop.RunAsync(this, work, once, ct);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Never hold up the host's start: connecting to the database is asynchronous, but be sure of it.
        await Task.Yield();

        using var stoppingRegistration = _lifetime.ApplicationStopping.Register(OnApplicationStopping);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _lifetime.ApplicationStopping);
        var ct = linked.Token;

        _logger.LogInformation("Job lock: competing for '{Resource}'", _resource);

        while (!ct.IsCancellationRequested && !IsStopping)
        {
            IAppLockSession? session = null;
            var keep = false;
            var delay = TimeSpan.Zero;
            try
            {
                session = _sessionFactory();
                MarkAttempt();
                await session.OpenAsync(ct);
                if (await session.TryAcquireAsync(_resource, _options.LockTimeout, ct))
                {
                    keep = await HoldAsync(session, ct);
                }
                else
                {
                    if (SetOutcome(JobLeadershipOutcome.HeldByOther, null))
                    {
                        _logger.LogInformation("Job lock: '{Resource}' is held by another copy; waiting for it to let go", _resource);
                    }

                    delay = _options.BusyRetryDelay;
                }
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                // The application is stopping. Whichever exception the client raised for the cut-short call is not a fault.
            }
            catch (Exception ex)
            {
                SetOutcome(JobLeadershipOutcome.Error, ex.Message);
                delay = _options.ErrorRetryDelay;
                _logger.LogWarning(ex, "Job lock: could not take the lock on '{Resource}'; trying again in {Delay}", _resource, delay);
            }
            finally
            {
                if (keep)
                {
                    _held = session;
                }
                else if (session is not null)
                {
                    await DisposeQuietlyAsync(session);
                }
            }

            if (ct.IsCancellationRequested || IsStopping || !await DelayAsync(delay, ct))
            {
                break;
            }
        }
    }

    /// <summary>True once the host is stopping this service, whether or not <c>ApplicationStopping</c> has fired for it yet.</summary>
    private bool IsStopping
    {
        get
        {
            lock (_gate)
            {
                return _stopping;
            }
        }
    }

    /// <summary>
    /// Runs one term on an acquired lock. Returns true when the session should be kept so that <see cref="StopAsync"/>
    /// can release the lock (the host is stopping), false when it is lost and must be discarded.
    /// </summary>
    private async Task<bool> HoldAsync(IAppLockSession session, CancellationToken ct)
    {
        // Disposed below, except a term the heartbeat lost: its cancel runs in the background and disposes it when its
        // callbacks are done (EndTermInBackground).
        var term = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var heartbeat = HeartbeatAsync(session, term);
        try
        {
            SetOutcome(JobLeadershipOutcome.Acquired, null);
            if (IsStopping)
            {
                // The lock came in while the service was being stopped: no hook and no worker may start now. The
                // session is kept below so that StopAsync gives the lock back.
                _logger.LogInformation("Job lock: acquired '{Resource}' while stopping; no term starts", _resource);
            }
            else
            {
                _logger.LogInformation("Job lock: acquired '{Resource}'; running the term-start hooks", _resource);
                await RunHooksAsync(term.Token);
                if (TryStartTerm(term))
                {
                    _logger.LogInformation("Job lock: this copy now runs the background jobs");
                }
            }
        }
        catch (OperationCanceledException) when (term.IsCancellationRequested)
        {
            // Lost, or stopping, while the hooks ran.
        }
        catch
        {
            // Unexpected. End the term so the heartbeat stops, wait for it, and let the error reach the loop.
            CancelQuietly(term);
            if (!await heartbeat)
            {
                term.Dispose();
            }

            EndTerm();
            throw;
        }

        // The heartbeat runs until the term ends: true when it found the lock gone, false when the host is stopping.
        bool lost;
        try
        {
            lost = await heartbeat;
        }
        finally
        {
            EndTerm();
        }

        if (lost)
        {
            _logger.LogWarning("Job lock: the term on '{Resource}' ended because the lock was lost", _resource);
        }
        else
        {
            term.Dispose();
        }

        return !lost && (ct.IsCancellationRequested || IsStopping);
    }

    private async Task<bool> HeartbeatAsync(IAppLockSession session, CancellationTokenSource term)
    {
        var token = term.Token;
        string? lostReason = null;
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(_options.HeartbeatInterval, _time, token);
                if (await session.HoldsAsync(_resource, token))
                {
                    continue;
                }

                lostReason = "the database no longer reports the lock as held by this session";
                break;
            }
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            // Cancelled from outside (the host is stopping). A query cut short by it is not a lost lock, whichever
            // exception the client raises for it.
            return false;
        }
        catch (Exception ex)
        {
            // Fail closed: a session that cannot answer is treated as one that lost the lock.
            _logger.LogWarning(ex, "Job lock: the heartbeat on '{Resource}' failed", _resource);
            lostReason = ex.Message;
        }

        if (lostReason is null)
        {
            return false;
        }

        MarkLost(lostReason);
        EndTermInBackground(term);
        return true;
    }

    private async Task RunHooksAsync(CancellationToken ct)
    {
        foreach (var hook in _services.GetServices<IJobTermHook>())
        {
            try
            {
                await hook.OnTermStartAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job lock: the term-start hook {Hook} failed; the term continues", hook.GetType().Name);
            }
        }
    }

    private bool TryStartTerm(CancellationTokenSource term)
    {
        TaskCompletionSource<CancellationToken> waiters;
        lock (_gate)
        {
            if (_stopping || term.IsCancellationRequested)
            {
                return false;
            }

            _term = term;
            _isHolder = true;
            _holderSinceUtc = Now();
            waiters = _nextTerm;
            _nextTerm = NewWaiter();
        }
        waiters.TrySetResult(term.Token);
        return true;
    }

    /// <summary>Clears <see cref="IsHolder"/> and cancels the term's token. Safe to call again.</summary>
    private void EndTerm()
    {
        CancellationTokenSource? term;
        lock (_gate)
        {
            term = _term;
            _term = null;
            _isHolder = false;
            _holderSinceUtc = null;
        }

        if (term is not null)
        {
            CancelQuietly(term);
        }
    }

    /// <summary>
    /// Ends a term that is lost: clears <see cref="IsHolder"/> and the term first, so its token is handed to nobody again,
    /// then cancels the token without waiting for its callbacks. A callback on that token can block (a SQL command's cancel
    /// over a failing connection), and the loop must still end the term and ask for the lock again. On 2026-10-29 the copy
    /// cancelled first and waited: the heartbeat never came back from the cancel, the term was never cleared, and every
    /// stopped worker's loop was handed its cancelled token again and again, at full CPU, until a restart.
    /// </summary>
    private void EndTermInBackground(CancellationTokenSource term)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_term, term))
            {
                _term = null;
                _isHolder = false;
                _holderSinceUtc = null;
            }
        }

        _ = CancelThenDisposeAsync(term);
    }

    /// <summary>
    /// The token reads as cancelled before this returns (<see cref="CancellationTokenSource.CancelAsync"/>), and its callbacks
    /// run on the thread pool. The source is disposed once they are done.
    /// </summary>
    private async Task CancelThenDisposeAsync(CancellationTokenSource term)
    {
        try
        {
            await term.CancelAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Job lock: a callback registered on the term's token threw while it was cancelled");
        }
        finally
        {
            term.Dispose();
        }
    }

    /// <summary>Cancels a token source. A callback that throws is logged: it must not stop the term from ending.</summary>
    private void CancelQuietly(CancellationTokenSource source)
    {
        try
        {
            source.Cancel();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Job lock: a callback registered on the term's token threw while it was cancelled");
        }
    }

    private void OnApplicationStopping()
    {
        lock (_gate)
        {
            _stopping = true;
        }

        EndTerm();
        _logger.LogInformation("Job lock: the application is stopping, so background jobs are winding down; the lock is released after they have stopped");
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Normally ApplicationStopping has done this already.
        lock (_gate)
        {
            _stopping = true;
        }

        EndTerm();
        try
        {
            await base.StopAsync(cancellationToken);
        }
        finally
        {
            await ReleaseAsync(cancellationToken);
        }
    }

    private async Task ReleaseAsync(CancellationToken cancellationToken)
    {
        var session = Interlocked.Exchange(ref _held, null);
        if (session is null)
        {
            return;
        }

        try
        {
            // If the loop is somehow still running, its heartbeat may be using the connection; closing it frees the lock too.
            if (ExecuteTask is null || ExecuteTask.IsCompleted)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(_options.ReleaseTimeout);
                await session.ReleaseAsync(_resource, timeout.Token);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Job lock: releasing '{Resource}' failed; closing the connection frees it", _resource);
        }
        finally
        {
            await DisposeQuietlyAsync(session);
        }

        SetOutcome(JobLeadershipOutcome.Released, null);
        _logger.LogInformation("Job lock: released '{Resource}'", _resource);
    }

    private async Task<bool> DelayAsync(TimeSpan delay, CancellationToken ct)
    {
        if (delay <= TimeSpan.Zero)
        {
            return true;
        }

        try
        {
            await Task.Delay(delay, _time, ct);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return false;
        }
    }

    private async Task DisposeQuietlyAsync(IAppLockSession session)
    {
        try
        {
            await session.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Job lock: closing the lock connection failed");
        }
    }

    private void MarkAttempt()
    {
        lock (_gate)
        {
            _lastAttemptUtc = Now();
        }
    }

    private void MarkLost(string reason)
    {
        lock (_gate)
        {
            _lastOutcome = JobLeadershipOutcome.Lost;
            _lastError = reason;
        }
    }

    /// <summary>Records the outcome and error. True when the outcome is a change from the last one.</summary>
    private bool SetOutcome(JobLeadershipOutcome outcome, string? error)
    {
        lock (_gate)
        {
            var changed = _lastOutcome != outcome;
            _lastOutcome = outcome;
            _lastError = error;
            return changed;
        }
    }

    private DateTime Now() => _time.GetUtcNow().UtcDateTime;

    private static TaskCompletionSource<CancellationToken> NewWaiter() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
