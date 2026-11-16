using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Kraty
{
    /// <summary>
    /// Server-Sent Events plumbing shared by every Kraty stream
    /// (leaderboards, inventory, chat): opening the long-lived request, mapping a
    /// non-2xx response onto the SDK's error types, and parsing the
    /// <c>event:</c> / <c>data:</c> line protocol.
    ///
    /// <para>
    /// Each stream keeps its own strongly-typed event class and its own
    /// handle; only the transport lives here, so adding a stream is a
    /// factory plus a DTO rather than another copy of the parser.
    /// </para>
    /// </summary>
    internal static class SseReader
    {
        private static JToken ParseKeepingDates(string raw)
        {
            using var reader = new JsonTextReader(new StringReader(raw)) { DateParseHandling = DateParseHandling.None };
            return JToken.ReadFrom(reader);
        }

        /// <summary>
        /// Opens the stream and returns the live response. Throws
        /// <see cref="KratyNetworkError"/> when the connection fails and
        /// <see cref="KratyApiError"/> for a non-2xx status (the body is
        /// drained and parsed first so the caller sees the real error code).
        /// <paramref name="label"/> only shapes error messages.
        /// </summary>
        public static async Task<HttpResponseMessage> OpenAsync(
            HttpClient http,
            string url,
            string authHeader,
            string? playerSecret,
            string label,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? extraHeaders = null
        )
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("authorization", authHeader);
            req.Headers.TryAddWithoutValidation("accept", "text/event-stream");
            if (!string.IsNullOrEmpty(playerSecret))
            {
                req.Headers.TryAddWithoutValidation("x-player-secret", playerSecret);
            }
            if (extraHeaders != null)
            {
                foreach (var h in extraHeaders) req.Headers.TryAddWithoutValidation(h.Key, h.Value);
            }

            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new KratyNetworkError($"{label} connect failed: {ex.Message}", ex);
            }

            if (!response.IsSuccessStatusCode)
            {
                var bodyText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var retryAfter = KratyClient.ReadRetryAfter(response);
                response.Dispose();
                string? code = null;
                string? message = null;
                Dictionary<string, object?>? details = null;
                try
                {
                    var root = JToken.Parse(bodyText);
                    if (root.Type == JTokenType.Object && root["error"] is JObject err)
                    {
                        code = (string?)err["code"];
                        message = (string?)err["message"];
                        var d = err["details"];
                        if (d != null)
                        {
                            details = new Dictionary<string, object?> { ["raw"] = d.ToString(Formatting.None) };
                        }
                    }
                }
                catch { /* not JSON, fall through */ }
                throw new KratyApiError(
                    (int)response.StatusCode,
                    code ?? $"http_{(int)response.StatusCode}",
                    message ?? bodyText,
                    details
                )
                { RetryAfterHeader = retryAfter };
            }

            return response;
        }

        /// <summary>
        /// Reads the response body until the server closes it or
        /// <paramref name="cts"/> fires, invoking
        /// <paramref name="onEvent"/> once per complete SSE event.
        /// Parse failures and read failures go to <paramref name="onError"/>;
        /// a cancellation is never reported as an error.
        /// <paramref name="rawDates"/> keeps ISO timestamps as JSON strings
        /// (instead of Json.NET's default date tokens) so typed DTOs with
        /// <c>string</c> timestamp fields read them back verbatim.
        /// </summary>
        public static async Task PumpAsync(
            HttpResponseMessage response,
            CancellationTokenSource cts,
            Action<string, Dictionary<string, JToken>> onEvent,
            Action<Exception> onError,
            bool rawDates = false
        )
        {
            var readErr = await ReadAsync(
                response,
                cts.Token,
                (kind, data) =>
                {
                    onEvent(kind, data);
                    return Task.FromResult(true);
                },
                onError,
                rawDates
            ).ConfigureAwait(false);
            if (readErr != null && !cts.Token.IsCancellationRequested) onError(readErr);
        }

        /// <summary>
        /// The parser behind <see cref="PumpAsync"/>. Awaits
        /// <paramref name="onEvent"/> before reading on (so a handler can do
        /// async work, e.g. a chat catch-up, while live events wait in the
        /// socket), and stops when it returns false. <paramref name="onLine"/>
        /// fires for every line read, comments (<c>: ping</c>) included: the
        /// heartbeat watchdog's activity signal. Parse failures go to
        /// <paramref name="onParseError"/>. Returns the read / loop failure
        /// that ended the stream, or null on a clean end of stream, a stop,
        /// or a cancellation.
        /// </summary>
        public static async Task<Exception?> ReadAsync(
            HttpResponseMessage response,
            CancellationToken token,
            Func<string, Dictionary<string, JToken>, Task<bool>> onEvent,
            Action<Exception> onParseError,
            bool rawDates = false,
            Action? onLine = null
        )
        {
            try
            {
                using var bodyStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var reader = new StreamReader(bodyStream, Encoding.UTF8);
                string currentEvent = "message";
                var dataBuffer = new StringBuilder();

                // Returns false when the handler asked to stop.
                async Task<bool> Emit()
                {
                    if (dataBuffer.Length == 0)
                    {
                        currentEvent = "message";
                        return true;
                    }
                    var raw = dataBuffer.ToString();
                    var kind = currentEvent;
                    dataBuffer.Clear();
                    currentEvent = "message";
                    Dictionary<string, JToken> dict;
                    try
                    {
                        var root = rawDates ? ParseKeepingDates(raw) : JToken.Parse(raw);
                        dict = new Dictionary<string, JToken>();
                        if (root is JObject obj)
                        {
                            foreach (var prop in obj.Properties())
                            {
                                dict[prop.Name] = prop.Value;
                            }
                        }
                        else
                        {
                            dict["value"] = root;
                        }
                    }
                    catch (Exception parseErr)
                    {
                        onParseError(parseErr);
                        return true;
                    }
                    return await onEvent(kind, dict).ConfigureAwait(false);
                }

                // ReadLineAsync doesn't take a CT on netstandard2.1, so
                // poll the reader and bail when the token fires by
                // disposing the stream out from under it.
                using var registration = token.Register(() =>
                {
                    try { bodyStream.Dispose(); } catch { /* swallow */ }
                });

                while (!token.IsCancellationRequested)
                {
                    string? line;
                    try
                    {
                        line = await reader.ReadLineAsync().ConfigureAwait(false);
                    }
                    catch (Exception readErr)
                    {
                        return token.IsCancellationRequested ? null : readErr;
                    }
                    onLine?.Invoke();
                    if (line == null)
                    {
                        // Server closed the stream; flush any
                        // pending event, then exit cleanly.
                        await Emit().ConfigureAwait(false);
                        return null;
                    }
                    if (line.Length == 0)
                    {
                        // Blank line terminates an event.
                        if (!await Emit().ConfigureAwait(false)) return null;
                        continue;
                    }
                    if (line[0] == ':')
                    {
                        // Comment / heartbeat (`: ping`): only activity.
                        continue;
                    }
                    var colonIdx = line.IndexOf(':');
                    if (colonIdx < 0) continue;
                    var field = line.Substring(0, colonIdx);
                    var value = line.Substring(colonIdx + 1);
                    // Spec: a single leading space in the value is
                    // optional and should be stripped.
                    if (value.Length > 0 && value[0] == ' ') value = value.Substring(1);
                    switch (field)
                    {
                        case "event":
                            currentEvent = value;
                            break;
                        case "data":
                            if (dataBuffer.Length > 0) dataBuffer.Append('\n');
                            dataBuffer.Append(value);
                            break;
                        // SSE also defines `id` and `retry`, which we
                        // don't use.
                        default:
                            break;
                    }
                }
                return null;
            }
            catch (Exception loopErr)
            {
                return token.IsCancellationRequested ? null : loopErr;
            }
        }
    }

    /// <summary>
    /// Options for the live streams (<c>Chat.LiveAsync</c>,
    /// <c>EventLeaderboards.LiveAsync</c> / <c>Subscribe</c>,
    /// <c>Inventory.LiveAsync</c> / <c>WatchAsync</c>).
    /// </summary>
    public sealed class StreamOptions
    {
        /// <summary>
        /// Reconnect on your behalf (default true). The stream survives server
        /// restarts, rebalances, a busy server and network drops: it waits
        /// (the server's <c>retryAfterMs</c> / <c>Retry-After</c>, else a
        /// jittered backoff up to 15 s), reopens and reports the gap through
        /// <c>OnConnection</c>; drops and the server's <c>close</c> never
        /// reach <c>OnError</c> / <c>OnClose</c>. The chat stream also catches
        /// up on messages missed while away. Only a refusal to reopen (401,
        /// 403 <c>chat_disabled</c>, 404, …) stops it, through <c>OnError</c>.
        /// False restores the single-connection behaviour: the stream ends on
        /// the first drop or <c>close</c>, and reopening is up to you.
        /// </summary>
        public bool AutoReconnect { get; set; } = true;

        /// <summary>
        /// With <see cref="AutoReconnect"/>: when nothing at all (no event, no
        /// <c>: ping</c> comment; the server pings every 15 s) arrives for this
        /// long, the connection is treated as dead and reopened (reason
        /// <c>heartbeat_timeout</c>). Default 40 s; <see cref="TimeSpan.Zero"/>
        /// turns the watchdog off.
        /// </summary>
        public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromSeconds(40);

        // Reconnect backoff (no server hint): base * 2^(n-1) * [0.5, 1], capped.
        // Internal so tests can shrink them.
        internal TimeSpan ReconnectBaseDelay { get; set; } = TimeSpan.FromSeconds(1);
        internal TimeSpan ReconnectMaxDelay { get; set; } = TimeSpan.FromSeconds(15);
    }

    /// <summary>State in a <see cref="StreamConnectionEvent"/>.</summary>
    public enum StreamConnectionState
    {
        /// <summary>Connected and live (after <c>ready</c> and, for chat, the catch-up).</summary>
        Connected,
        /// <summary>The connection was lost; the SDK is waiting to reopen it.</summary>
        Reconnecting,
    }

    /// <summary>Why a stream is reconnecting (<see cref="StreamConnectionEvent.Reason"/>).</summary>
    public static class StreamReconnectReason
    {
        /// <summary>The server is shutting down (deploy / scale-in).</summary>
        public const string Shutdown = "shutdown";
        /// <summary>The server moved this stream to another instance.</summary>
        public const string Rebalance = "rebalance";
        /// <summary>The server cut off a client that stopped reading.</summary>
        public const string SlowConsumer = "slow_consumer";
        /// <summary>Nothing arrived for <see cref="StreamOptions.HeartbeatTimeout"/>.</summary>
        public const string HeartbeatTimeout = "heartbeat_timeout";
        /// <summary>A network / read failure, or a retryable error reopening.</summary>
        public const string Error = "error";
        /// <summary>The server answered 503 (busy, scaling up) when reopening.</summary>
        public const string Busy = "busy";
        /// <summary>The connection ended without a reason.</summary>
        public const string Ended = "ended";
    }

    /// <summary>
    /// Connection status of an auto-reconnecting stream: not an error, just
    /// what to show (e.g. a "reconnecting…" badge). Fires
    /// <see cref="StreamConnectionState.Connected"/> after every successful
    /// connect (the first one included) and
    /// <see cref="StreamConnectionState.Reconnecting"/> each time the SDK
    /// schedules a reopen.
    /// </summary>
    public sealed class StreamConnectionEvent
    {
        public StreamConnectionState State { get; }
        /// <summary>Why it's reconnecting (<see cref="StreamReconnectReason"/>); null on <c>Connected</c>.</summary>
        public string? Reason { get; }
        /// <summary>How long until the next attempt; null on <c>Connected</c>.</summary>
        public TimeSpan? RetryIn { get; }
        /// <summary>The reconnect attempt (1, 2, …; resets once connected); null on <c>Connected</c>.</summary>
        public int? Attempt { get; }

        public StreamConnectionEvent(StreamConnectionState state, string? reason = null, TimeSpan? retryIn = null, int? attempt = null)
        {
            State = state;
            Reason = reason;
            RetryIn = retryIn;
            Attempt = attempt;
        }
    }

    /// <summary>
    /// The reconnect loop shared by every auto-reconnecting stream. Each
    /// stream plugs in how to open, how to deliver an event, and (chat) what
    /// to do once a reopened connection is <c>ready</c>.
    /// </summary>
    internal sealed class SseReconnectLoop
    {
        public Func<CancellationToken, Task<HttpResponseMessage>> Open { get; set; } = _ => throw new InvalidOperationException();
        /// <summary>Hands a live event to the game (never sees <c>close</c>).</summary>
        public Action<string, Dictionary<string, JToken>> Deliver { get; set; } = (_, __) => { };
        /// <summary>After <c>ready</c> is delivered: (isReconnect, ct). Throwing counts as a drop.</summary>
        public Func<bool, CancellationToken, Task>? AfterReady { get; set; }
        /// <summary>An event after which the stream ends for good (delivered first).</summary>
        public Func<string, bool>? IsFinal { get; set; }
        public Action<StreamConnectionEvent> Status { get; set; } = _ => { };
        /// <summary>Parse errors, and the error that stopped the loop (a refused reopen).</summary>
        public Action<Exception> Error { get; set; } = _ => { };
        public bool RawDates { get; set; }
        public StreamOptions Options { get; set; } = new();

        private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
        private static readonly Random Rng = new();

        /// <summary>Network, 5xx (incl. 503 busy), 408 and 429 are worth another try; other 4xx are final.</summary>
        internal static bool IsRetryable(Exception e) =>
            e is KratyNetworkError
            || (e is KratyApiError a && (a.Status >= 500 || a.Status == 408 || a.Status == 429));

        /// <summary>
        /// The first connect of an auto-reconnecting stream: a refusal throws
        /// (as the single-connection API does); a retryable failure comes back
        /// as the error to start the loop with, so the stream begins in
        /// <c>Reconnecting</c> instead of failing.
        /// </summary>
        internal static async Task<(HttpResponseMessage? Response, Exception? Error)> OpenFirstAsync(
            Func<CancellationToken, Task<HttpResponseMessage>> open, CancellationToken ct)
        {
            try
            {
                return (await open(ct).ConfigureAwait(false), null);
            }
            catch (Exception err) when (IsRetryable(err) && !ct.IsCancellationRequested)
            {
                return (null, err);
            }
        }

        /// <summary><paramref name="baseDelay"/> * 2^(attempt-1), capped at <paramref name="max"/>, times a random factor in [0.5, 1].</summary>
        internal static TimeSpan Backoff(int attempt, TimeSpan baseDelay, TimeSpan max)
        {
            var ms = baseDelay.TotalMilliseconds * Math.Pow(2, Math.Max(0, attempt - 1));
            if (ms > max.TotalMilliseconds) ms = max.TotalMilliseconds;
            double factor;
            lock (Rng) factor = 0.5 + Rng.NextDouble() * 0.5;
            return TimeSpan.FromMilliseconds(ms * factor);
        }

        private TimeSpan Backoff(int attempt) => Backoff(attempt, Options.ReconnectBaseDelay, Options.ReconnectMaxDelay);

        private sealed class Outcome
        {
            public bool Stop;
            public string Reason = StreamReconnectReason.Ended;
            public TimeSpan? Delay;
        }

        private sealed class Connection
        {
            public long LastActivityMs;
            public int Busy;
            public bool TimedOut;
            public Outcome? Outcome;
        }

        /// <summary>
        /// Runs until <paramref name="stop"/> fires, a final event arrives, or a
        /// reopen is refused. <paramref name="first"/> is the already-open first
        /// connection, or null when the first attempt failed with the
        /// retryable <paramref name="firstError"/>.
        /// </summary>
        public async Task RunAsync(HttpResponseMessage? first, Exception? firstError, CancellationToken stop)
        {
            var attempt = 0;
            var opened = 0;
            var response = first;
            var pending = firstError;
            while (!stop.IsCancellationRequested)
            {
                Outcome outcome;
                if (response == null)
                {
                    if (pending == null)
                    {
                        try
                        {
                            response = await Open(stop).ConfigureAwait(false);
                        }
                        catch (Exception e)
                        {
                            if (stop.IsCancellationRequested) return;
                            pending = e;
                        }
                    }
                    if (pending != null)
                    {
                        if (!IsRetryable(pending))
                        {
                            Error(pending);
                            return;
                        }
                        outcome = FromFailure(pending);
                        pending = null;
                        if (!await ScheduleAsync(outcome, ++attempt, stop).ConfigureAwait(false)) return;
                        continue;
                    }
                }

                try
                {
                    outcome = await RunConnectionAsync(response!, opened++ > 0, () => attempt = 0, stop).ConfigureAwait(false);
                }
                finally
                {
                    try { response!.Dispose(); } catch { /* swallow */ }
                    response = null;
                }
                if (outcome.Stop || stop.IsCancellationRequested) return;
                if (!await ScheduleAsync(outcome, ++attempt, stop).ConfigureAwait(false)) return;
            }
        }

        // Reports `reconnecting` and waits. False when stopped meanwhile.
        private async Task<bool> ScheduleAsync(Outcome outcome, int attempt, CancellationToken stop)
        {
            var delay = outcome.Delay ?? Backoff(attempt);
            try { Status(new StreamConnectionEvent(StreamConnectionState.Reconnecting, outcome.Reason, delay, attempt)); }
            catch (Exception cbErr) { Error(cbErr); }
            try
            {
                await Task.Delay(delay, stop).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            return !stop.IsCancellationRequested;
        }

        private static Outcome FromFailure(Exception e) => new Outcome
        {
            Reason = e is KratyApiError a && a.Status == 503 ? StreamReconnectReason.Busy : StreamReconnectReason.Error,
            Delay = (e as KratyApiError)?.ServerRetryAfter,
        };

        private static Outcome FromClose(Dictionary<string, JToken> data)
        {
            var reason = data.TryGetValue("reason", out var r) && r.Type == JTokenType.String ? (string?)r : null;
            TimeSpan? delay = null;
            if (data.TryGetValue("retryAfterMs", out var ms) && (ms.Type == JTokenType.Integer || ms.Type == JTokenType.Float))
            {
                delay = TimeSpan.FromMilliseconds(Math.Max(0, (double)ms));
            }
            return new Outcome
            {
                Reason = string.IsNullOrEmpty(reason) ? StreamReconnectReason.Shutdown : reason!,
                Delay = delay,
            };
        }

        private async Task<Outcome> RunConnectionAsync(
            HttpResponseMessage response, bool isReconnect, Action onConnected, CancellationToken stop)
        {
            using var conn = CancellationTokenSource.CreateLinkedTokenSource(stop);
            var state = new Connection { LastActivityMs = Clock.ElapsedMilliseconds };
            var heartbeat = Options.HeartbeatTimeout;
            var watchdog = heartbeat > TimeSpan.Zero ? WatchAsync(state, heartbeat, conn) : Task.CompletedTask;

            async Task<bool> OnEvent(string kind, Dictionary<string, JToken> data)
            {
                if (kind == "close")
                {
                    state.Outcome = FromClose(data);
                    return false;
                }
                Volatile.Write(ref state.Busy, 1);
                try
                {
                    Deliver(kind, data);
                    if (IsFinal != null && IsFinal(kind))
                    {
                        state.Outcome = new Outcome { Stop = true };
                        return false;
                    }
                    if (kind == "ready")
                    {
                        if (AfterReady != null)
                        {
                            try
                            {
                                await AfterReady(isReconnect, conn.Token).ConfigureAwait(false);
                            }
                            catch (Exception e)
                            {
                                if (conn.IsCancellationRequested) return false;
                                if (!IsRetryable(e)) Error(e);
                                state.Outcome = FromFailure(e);
                                return false;
                            }
                        }
                        onConnected();
                        try { Status(new StreamConnectionEvent(StreamConnectionState.Connected)); }
                        catch (Exception cbErr) { Error(cbErr); }
                    }
                    return true;
                }
                finally
                {
                    Interlocked.Exchange(ref state.LastActivityMs, Clock.ElapsedMilliseconds);
                    Volatile.Write(ref state.Busy, 0);
                }
            }

            var readErr = await SseReader.ReadAsync(
                response,
                conn.Token,
                OnEvent,
                Error,
                RawDates,
                () => Interlocked.Exchange(ref state.LastActivityMs, Clock.ElapsedMilliseconds)
            ).ConfigureAwait(false);

            try { conn.Cancel(); } catch { /* swallow */ }
            try { await watchdog.ConfigureAwait(false); } catch { /* swallow */ }

            if (stop.IsCancellationRequested) return new Outcome { Stop = true };
            if (state.Outcome != null) return state.Outcome;
            if (state.TimedOut) return new Outcome { Reason = StreamReconnectReason.HeartbeatTimeout };
            return new Outcome { Reason = readErr != null ? StreamReconnectReason.Error : StreamReconnectReason.Ended };
        }

        // Closes the connection when nothing arrived for `timeout` (time spent
        // inside an event handler, e.g. the chat catch-up, doesn't count).
        private static async Task WatchAsync(Connection state, TimeSpan timeout, CancellationTokenSource conn)
        {
            var limit = (long)timeout.TotalMilliseconds;
            while (!conn.IsCancellationRequested)
            {
                long wait;
                if (Volatile.Read(ref state.Busy) == 1)
                {
                    wait = limit;
                }
                else
                {
                    var idle = Clock.ElapsedMilliseconds - Interlocked.Read(ref state.LastActivityMs);
                    if (idle >= limit)
                    {
                        state.TimedOut = true;
                        try { conn.Cancel(); } catch { /* swallow */ }
                        return;
                    }
                    wait = limit - idle;
                }
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(1, wait)), conn.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }
}
