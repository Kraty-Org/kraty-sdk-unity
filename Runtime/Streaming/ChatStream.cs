using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Kraty
{
    /// <summary>The SSE <c>event:</c> names the chat stream sends.</summary>
    public static class ChatStreamEventKind
    {
        public const string Ready = "ready";
        public const string Message = "message";
        public const string MessageEdited = "message_edited";
        public const string MessageDeleted = "message_deleted";
        public const string Typing = "typing";
        public const string Read = "read";
        public const string ConversationAdded = "conversation_added";
        public const string ConversationUpdated = "conversation_updated";
        public const string ConversationRemoved = "conversation_removed";
        public const string Restriction = "restriction";
        public const string RestrictionLifted = "restriction_lifted";
        public const string Close = "close";
    }

    /// <summary>
    /// One raw event off the chat stream: <see cref="Kind"/> is the SSE
    /// <c>event:</c> line, <see cref="Data"/> the parsed JSON. The typed
    /// callbacks on <see cref="ChatStream"/> cover every known kind; this
    /// form also carries kinds a newer backend adds.
    /// </summary>
    public sealed class ChatStreamEvent
    {
        public string Kind { get; }
        public Dictionary<string, JToken> Data { get; }

        public ChatStreamEvent(string kind, Dictionary<string, JToken> data)
        {
            Kind = kind;
            Data = data;
        }
    }

    /// <summary>A public channel the stream follows: the conversation of the player's <see cref="Room"/> in it.</summary>
    public sealed class ChatStreamChannel
    {
        [JsonProperty("key")] public string Key { get; set; } = string.Empty;
        [JsonProperty("conversationId")] public string ConversationId { get; set; } = string.Empty;
        /// <summary>The player's room in the channel (0-based); each room is its own conversation.</summary>
        [JsonProperty("room")] public int Room { get; set; }
    }

    /// <summary><c>ready</c>: every listener is wired; nothing sent after this is missed.</summary>
    public sealed class ChatStreamReady
    {
        [JsonProperty("externalPlayerId")] public string ExternalPlayerId { get; set; } = string.Empty;
        /// <summary>The inbox conversations this stream follows.</summary>
        [JsonProperty("conversationIds")] public List<string> ConversationIds { get; set; } = new();
        /// <summary>The channels named in <c>channels</c> that resolved, with their conversation ids.</summary>
        [JsonProperty("channels")] public List<ChatStreamChannel> Channels { get; set; } = new();
        /// <summary>
        /// Every followed conversation's latest seq at connect time (inbox and
        /// channel rooms). The self-healing stream seeds its catch-up from it.
        /// Null from older servers.
        /// </summary>
        [JsonProperty("lastSeqs")] public Dictionary<string, long>? LastSeqs { get; set; }
    }

    /// <summary><c>message_deleted</c>: drop or grey out the message.</summary>
    public sealed class ChatMessageDeletedEvent
    {
        [JsonProperty("conversationId")] public string ConversationId { get; set; } = string.Empty;
        [JsonProperty("messageId")] public string MessageId { get; set; } = string.Empty;
        [JsonProperty("seq")] public long Seq { get; set; }
    }

    /// <summary><c>typing</c>: another player is typing (never your own).</summary>
    public sealed class ChatTypingEvent
    {
        [JsonProperty("conversationId")] public string ConversationId { get; set; } = string.Empty;
        [JsonProperty("player")] public ChatPlayerRef Player { get; set; } = new();
    }

    /// <summary><c>read</c>: another member read up to <see cref="Seq"/>.</summary>
    public sealed class ChatReadEvent
    {
        [JsonProperty("conversationId")] public string ConversationId { get; set; } = string.Empty;
        [JsonProperty("externalPlayerId")] public string ExternalPlayerId { get; set; } = string.Empty;
        [JsonProperty("seq")] public long Seq { get; set; }
    }

    /// <summary><c>conversation_added</c>: the player joined a conversation; the stream now follows it.</summary>
    public sealed class ChatConversationAddedEvent
    {
        [JsonProperty("conversationId")] public string ConversationId { get; set; } = string.Empty;
        [JsonProperty("kind")] public string Kind { get; set; } = string.Empty;
    }

    /// <summary><c>conversation_updated</c>: title / metadata / member count changed.</summary>
    public sealed class ChatConversationUpdatedEvent
    {
        [JsonProperty("conversationId")] public string ConversationId { get; set; } = string.Empty;
        [JsonProperty("title")] public string? Title { get; set; }
        [JsonProperty("metadata")] public Dictionary<string, JToken> Metadata { get; set; } = new();
        [JsonProperty("memberCount")] public int MemberCount { get; set; }
    }

    /// <summary><c>conversation_removed</c>: the player left or was removed; the stream stops following it.</summary>
    public sealed class ChatConversationRemovedEvent
    {
        [JsonProperty("conversationId")] public string ConversationId { get; set; } = string.Empty;
        [JsonProperty("reason")] public string Reason { get; set; } = string.Empty;
    }

    /// <summary><c>restriction</c>: a warning, mute or chat ban was put on the player.</summary>
    public sealed class ChatRestrictionEvent
    {
        [JsonProperty("restrictionId")] public string RestrictionId { get; set; } = string.Empty;
        /// <summary><c>warning</c>, <c>chat_mute</c> or <c>chat_ban</c>.</summary>
        [JsonProperty("kind")] public string Kind { get; set; } = string.Empty;
        [JsonProperty("reason")] public string? Reason { get; set; }
        [JsonProperty("expiresAt")] public string? ExpiresAt { get; set; }
    }

    /// <summary><c>restriction_lifted</c>: a restriction was lifted early.</summary>
    public sealed class ChatRestrictionLiftedEvent
    {
        [JsonProperty("restrictionId")] public string RestrictionId { get; set; } = string.Empty;
        [JsonProperty("kind")] public string Kind { get; set; } = string.Empty;
    }

    /// <summary>
    /// <c>close</c>: the server is closing the stream (<c>shutdown</c>,
    /// <c>rebalance</c>, or <c>slow_consumer</c>: this client stopped reading).
    /// Only surfaced with <see cref="StreamOptions.AutoReconnect"/> off; then
    /// treat it like a drop: reopen after <see cref="RetryAfterMs"/> and catch up.
    /// </summary>
    public sealed class ChatCloseEvent
    {
        /// <summary><c>shutdown</c>, <c>rebalance</c> or <c>slow_consumer</c> (empty from older servers).</summary>
        [JsonProperty("reason")] public string Reason { get; set; } = string.Empty;
        /// <summary>How long to wait before reopening, in ms (0 when the server sent none).</summary>
        [JsonProperty("retryAfterMs")] public long RetryAfterMs { get; set; }
    }

    /// <summary>
    /// Handle to the player's live chat stream: one SSE connection carrying
    /// every conversation in their inbox plus the public channels it was
    /// opened with. Hook the typed callbacks (best inside the
    /// <c>configure</c> argument of <see cref="ChatClient.LiveAsync"/>, so
    /// nothing fires before they're set) and call <see cref="CancelAsync"/>
    /// to stop.
    ///
    /// <para>
    /// Callbacks fire on the HTTP background thread. In Unity, marshal to
    /// the main thread before touching <c>UnityEngine</c> APIs.
    /// </para>
    ///
    /// <para>
    /// The stream reconnects on its own (<see cref="StreamOptions.AutoReconnect"/>,
    /// on by default): after a server <c>close</c>, a drop, a busy server or
    /// 40 s of silence it waits, reopens, and once the new connection is
    /// <c>ready</c> catches up every followed conversation
    /// (<c>Chat.MessagesAsync(id, after: lastSeq)</c>; quiet ones from
    /// <see cref="ChatStreamReady.LastSeqs"/>, ones added meanwhile from the
    /// start), delivering the missed
    /// messages through <see cref="OnMessage"/> before any live event, never
    /// twice. <see cref="OnConnection"/> reports <c>Reconnecting</c> /
    /// <c>Connected</c>; <see cref="OnClose"/> and transport errors don't fire.
    /// With <c>AutoReconnect = false</c> the stream is one connection: on
    /// <see cref="OnClose"/> or <see cref="OnError"/>, reopen after a backoff
    /// and catch up yourself.
    /// </para>
    /// </summary>
    public sealed class ChatStream : IDisposable
    {
        private static readonly JsonSerializer PlainJson = JsonSerializer.Create();
        private readonly CancellationTokenSource _cts;
        private readonly HttpResponseMessage? _response;
        private Task _readLoop = Task.CompletedTask;
        private int _disposed;
        // Auto-reconnect only: the catch-up fetch and the last seq known per
        // conversation (dedupe + where each catch-up starts), guarded by
        // _lastSeq. _ready is the latest connection's `ready`; _seeded turns
        // true once a `ready` seeded the cursors from `lastSeqs` (after that,
        // a followed conversation without a cursor is new).
        private readonly Func<string, long, CancellationToken, Task<ChatMessagePage>>? _fetchAfter;
        private readonly Dictionary<string, long> _lastSeq = new();
        private ChatStreamReady? _ready;
        private bool _seeded;

        /// <summary>Every event, raw, including kinds this SDK version doesn't model.</summary>
        public Action<ChatStreamEvent>? OnEvent { get; set; }
        public Action<ChatStreamReady>? OnReady { get; set; }
        /// <summary>A new message, text already resolved for this player.</summary>
        public Action<ChatMessage>? OnMessage { get; set; }
        /// <summary>A message was edited: the full updated message (<see cref="ChatMessage.EditedAt"/> set); replace it by <c>MessageId</c>.</summary>
        public Action<ChatMessage>? OnMessageEdited { get; set; }
        public Action<ChatMessageDeletedEvent>? OnMessageDeleted { get; set; }
        public Action<ChatTypingEvent>? OnTyping { get; set; }
        public Action<ChatReadEvent>? OnRead { get; set; }
        public Action<ChatConversationAddedEvent>? OnConversationAdded { get; set; }
        public Action<ChatConversationUpdatedEvent>? OnConversationUpdated { get; set; }
        public Action<ChatConversationRemovedEvent>? OnConversationRemoved { get; set; }
        public Action<ChatRestrictionEvent>? OnRestriction { get; set; }
        public Action<ChatRestrictionLiftedEvent>? OnRestrictionLifted { get; set; }
        /// <summary>
        /// Only with <see cref="StreamOptions.AutoReconnect"/> off: the server is
        /// closing the stream (<c>shutdown</c>, <c>rebalance</c>, <c>slow_consumer</c>);
        /// reopen it after <see cref="ChatCloseEvent.RetryAfterMs"/> and catch up.
        /// </summary>
        public Action<ChatCloseEvent>? OnClose { get; set; }
        /// <summary>
        /// Parse failures and exceptions thrown by your callbacks. With
        /// auto-reconnect, also the error that stopped the stream for good (a
        /// refused reopen: 401, 403 <c>chat_disabled</c>, 404, …); drops are
        /// handled for you. Without it, transport failures too.
        /// </summary>
        public Action<Exception>? OnError { get; set; }
        /// <summary>
        /// Auto-reconnect status: <c>Connected</c> after every successful
        /// connect (after <c>ready</c> and the catch-up, the first connect
        /// included), <c>Reconnecting</c> (with reason, delay, attempt) each
        /// time the connection is lost. Not an error; use it for a
        /// "reconnecting…" badge.
        /// </summary>
        public Action<StreamConnectionEvent>? OnConnection { get; set; }

        /// <summary>The <c>ready</c> payload of the current connection once it arrived (null before).</summary>
        public ChatStreamReady? Ready { get; private set; }

        /// <summary>True when this stream reconnects on its own (<see cref="StreamOptions.AutoReconnect"/>).</summary>
        public bool AutoReconnect => _fetchAfter != null;

        internal ChatStream(HttpResponseMessage response, CancellationTokenSource cts)
        {
            _response = response;
            _cts = cts;
        }

        internal ChatStream(CancellationTokenSource cts, Func<string, long, CancellationToken, Task<ChatMessagePage>> fetchAfter)
        {
            _cts = cts;
            _fetchAfter = fetchAfter;
        }

        internal void Start(Func<ChatStream, Task> startReadLoop)
        {
            // Runs on the threadpool until the server closes the stream OR
            // the consumer cancels.
            _readLoop = Task.Run(() => startReadLoop(this));
        }

        /// <summary>Cancels the subscription + closes the HTTP socket. Idempotent.</summary>
        public async Task CancelAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            try { _cts.Cancel(); } catch { /* swallow */ }
            try { await _readLoop.ConfigureAwait(false); } catch { /* swallow */ }
            try { _response?.Dispose(); } catch { /* swallow */ }
            try { _cts.Dispose(); } catch { /* swallow */ }
        }

        public void Dispose()
        {
            _ = CancelAsync();
        }

        internal void Dispatch(string kind, Dictionary<string, JToken> data)
        {
            if (AutoReconnect && !Track(kind, data)) return;
            try
            {
                OnEvent?.Invoke(new ChatStreamEvent(kind, data));
                switch (kind)
                {
                    case ChatStreamEventKind.Ready:
                        var ready = As<ChatStreamReady>(data);
                        Ready = ready;
                        OnReady?.Invoke(ready);
                        break;
                    case ChatStreamEventKind.Message:
                        OnMessage?.Invoke(As<ChatMessage>(data));
                        break;
                    case ChatStreamEventKind.MessageEdited:
                        OnMessageEdited?.Invoke(As<ChatMessage>(data));
                        break;
                    case ChatStreamEventKind.MessageDeleted:
                        OnMessageDeleted?.Invoke(As<ChatMessageDeletedEvent>(data));
                        break;
                    case ChatStreamEventKind.Typing:
                        OnTyping?.Invoke(As<ChatTypingEvent>(data));
                        break;
                    case ChatStreamEventKind.Read:
                        OnRead?.Invoke(As<ChatReadEvent>(data));
                        break;
                    case ChatStreamEventKind.ConversationAdded:
                        OnConversationAdded?.Invoke(As<ChatConversationAddedEvent>(data));
                        break;
                    case ChatStreamEventKind.ConversationUpdated:
                        OnConversationUpdated?.Invoke(As<ChatConversationUpdatedEvent>(data));
                        break;
                    case ChatStreamEventKind.ConversationRemoved:
                        OnConversationRemoved?.Invoke(As<ChatConversationRemovedEvent>(data));
                        break;
                    case ChatStreamEventKind.Restriction:
                        OnRestriction?.Invoke(As<ChatRestrictionEvent>(data));
                        break;
                    case ChatStreamEventKind.RestrictionLifted:
                        OnRestrictionLifted?.Invoke(As<ChatRestrictionLiftedEvent>(data));
                        break;
                    case ChatStreamEventKind.Close:
                        OnClose?.Invoke(As<ChatCloseEvent>(data));
                        break;
                }
            }
            catch (Exception err)
            {
                EmitError(err);
            }
        }

        internal void EmitError(Exception err)
        {
            try { OnError?.Invoke(err); } catch { /* swallow */ }
        }

        internal void EmitConnection(StreamConnectionEvent ev)
        {
            try { OnConnection?.Invoke(ev); }
            catch (Exception cbErr) { EmitError(cbErr); }
        }

        // Auto mode: records the last seq per conversation; false = a message
        // already delivered (seq <= last), drop it.
        private bool Track(string kind, Dictionary<string, JToken> data)
        {
            if (kind == ChatStreamEventKind.Ready)
            {
                ChatStreamReady? ready;
                try { ready = As<ChatStreamReady>(data); }
                catch { ready = null; }
                lock (_lastSeq) _ready = ready;
                return true;
            }
            var conversationId = Str(data, "conversationId");
            if (conversationId == null) return true;
            switch (kind)
            {
                case ChatStreamEventKind.Message:
                case ChatStreamEventKind.MessageDeleted:
                    var seq = Long(data, "seq");
                    if (seq == null) return true;
                    lock (_lastSeq)
                    {
                        var known = _lastSeq.TryGetValue(conversationId, out var last);
                        if (kind == ChatStreamEventKind.Message && known && seq.Value <= last) return false;
                        if (!known || seq.Value > last) _lastSeq[conversationId] = seq.Value;
                    }
                    return true;
                case ChatStreamEventKind.ConversationAdded:
                    // New to the player: its whole history counts as missed on a reconnect.
                    lock (_lastSeq)
                    {
                        if (!_lastSeq.ContainsKey(conversationId)) _lastSeq[conversationId] = 0;
                    }
                    return true;
                case ChatStreamEventKind.ConversationRemoved:
                    lock (_lastSeq) _lastSeq.Remove(conversationId);
                    return true;
                default:
                    return true;
            }
        }

        /// <summary>
        /// After every <c>ready</c>: the first one carrying <c>lastSeqs</c>
        /// seeds a cursor for every followed conversation. After a reconnect's,
        /// page every tracked conversation forward from its last seq (a
        /// followed one without a cursor appeared during the gap: from 0) and
        /// deliver the missed messages as normal <c>message</c> events. A 4xx
        /// on one conversation drops it from the catch-up; anything else
        /// throws, which the loop treats as a drop.
        /// </summary>
        internal async Task CatchUpAsync(bool isReconnect, CancellationToken ct)
        {
            if (_fetchAfter == null) return;
            List<KeyValuePair<string, long>> todo;
            Dictionary<string, long>? heads;
            lock (_lastSeq)
            {
                var ready = _ready;
                heads = ready?.LastSeqs;
                var seededBefore = _seeded;
                if (heads != null && !_seeded)
                {
                    // Every followed conversation's seq at connect time: quiet ones get a cursor too.
                    foreach (var kv in heads)
                    {
                        if (!_lastSeq.ContainsKey(kv.Key)) _lastSeq[kv.Key] = kv.Value;
                    }
                    _seeded = true;
                }
                if (!isReconnect) return;
                if (ready != null && seededBefore)
                {
                    // Followed now but no cursor: it appeared during the gap, so its whole history is missed.
                    var followed = (ready.ConversationIds ?? new List<string>())
                        .Concat((ready.Channels ?? new List<ChatStreamChannel>()).Select(c => c.ConversationId))
                        .Concat(heads?.Keys ?? Enumerable.Empty<string>());
                    foreach (var id in followed)
                    {
                        if (!string.IsNullOrEmpty(id) && !_lastSeq.ContainsKey(id)) _lastSeq[id] = 0;
                    }
                }
                todo = _lastSeq.ToList();
            }
            foreach (var entry in todo)
            {
                var after = entry.Value;
                // The server says nothing is newer than what we have: skip the request.
                if (heads != null && heads.TryGetValue(entry.Key, out var head) && head <= after) continue;
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    ChatMessagePage page;
                    try
                    {
                        page = await _fetchAfter(entry.Key, after, ct).ConfigureAwait(false);
                    }
                    catch (KratyApiError err) when (!SseReconnectLoop.IsRetryable(err))
                    {
                        lock (_lastSeq) _lastSeq.Remove(entry.Key);
                        break;
                    }
                    foreach (var m in page.Messages)
                    {
                        var dict = new Dictionary<string, JToken>();
                        foreach (var prop in JObject.FromObject(m, PlainJson).Properties()) dict[prop.Name] = prop.Value;
                        Dispatch(ChatStreamEventKind.Message, dict);
                    }
                    // An empty page that says HasMore (every message hidden from
                    // this player) stops here with the cursor unchanged: the
                    // next reconnect retries, and this never spins.
                    if (!page.HasMore || page.Messages.Count == 0) break;
                    after = page.Messages[page.Messages.Count - 1].Seq;
                }
            }
        }

        private static string? Str(Dictionary<string, JToken> data, string key) =>
            data.TryGetValue(key, out var v) && v.Type == JTokenType.String ? (string?)v : null;

        private static long? Long(Dictionary<string, JToken> data, string key) =>
            data.TryGetValue(key, out var v) && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float) ? (long)v : (long?)null;

        private static T As<T>(Dictionary<string, JToken> data) where T : new()
        {
            var obj = new JObject();
            foreach (var kv in data) obj[kv.Key] = kv.Value.DeepClone();
            return obj.ToObject<T>() ?? new T();
        }
    }

    /// <summary>
    /// Opens the player's chat SSE stream: one connection
    /// (<see cref="StreamOptions.AutoReconnect"/> off) or the self-healing
    /// stream run by <see cref="SseReconnectLoop"/>. Transport + parsing live
    /// in <see cref="SseReader"/>.
    /// </summary>
    internal static class ChatStreamFactory
    {
        public const string FilterHeader = "x-kraty-chat-filter";

        public static async Task<ChatStream> OpenAsync(
            HttpClient http,
            string baseUrl,
            string externalPlayerId,
            string authHeader,
            string? playerSecret,
            IEnumerable<string>? channels,
            string? filter,
            Action<ChatStream>? configure,
            CancellationToken cancellationToken,
            StreamOptions? options = null,
            Func<string, long, CancellationToken, Task<ChatMessagePage>>? fetchAfter = null
        )
        {
            var query = new List<string>();
            var keys = channels?.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => Uri.EscapeDataString(c.Trim())).ToList();
            if (keys != null && keys.Count > 0) query.Add("channels=" + string.Join(",", keys));
            if (filter != null) query.Add("filter=" + filter);
            var url = $"{baseUrl}/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/chat/stream"
                + (query.Count > 0 ? "?" + string.Join("&", query) : string.Empty);
            var headers = filter != null ? new Dictionary<string, string> { [FilterHeader] = filter } : null;
            options ??= new StreamOptions();

            if (!options.AutoReconnect || fetchAfter == null)
            {
                var response = await SseReader.OpenAsync(
                    http, url, authHeader, playerSecret, "chat stream", cancellationToken, headers
                ).ConfigureAwait(false);

                var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var stream = new ChatStream(response, cts);
                try
                {
                    configure?.Invoke(stream);
                }
                catch
                {
                    response.Dispose();
                    cts.Dispose();
                    throw;
                }
                stream.Start(self =>
                    SseReader.PumpAsync(response, cts, self.Dispatch, self.EmitError, rawDates: true));
                return stream;
            }

            // Self-healing: callbacks first (so even the first `ready` /
            // `Reconnecting` lands), then the first connect. A refusal throws
            // as before; a transient failure starts out reconnecting.
            var autoCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var auto = new ChatStream(autoCts, fetchAfter);
            HttpResponseMessage? first = null;
            Exception? firstError = null;
            try
            {
                configure?.Invoke(auto);
            }
            catch
            {
                autoCts.Dispose();
                throw;
            }
            try
            {
                first = await SseReader.OpenAsync(
                    http, url, authHeader, playerSecret, "chat stream", cancellationToken, headers
                ).ConfigureAwait(false);
            }
            catch (Exception err) when (SseReconnectLoop.IsRetryable(err) && !cancellationToken.IsCancellationRequested)
            {
                firstError = err;
            }
            catch
            {
                autoCts.Dispose();
                throw;
            }
            var loop = new SseReconnectLoop
            {
                Open = token => SseReader.OpenAsync(http, url, authHeader, playerSecret, "chat stream", token, headers),
                Deliver = auto.Dispatch,
                AfterReady = auto.CatchUpAsync,
                Status = auto.EmitConnection,
                Error = auto.EmitError,
                RawDates = true,
                Options = options,
            };
            auto.Start(_ => loop.RunAsync(first, firstError, autoCts.Token));
            return auto;
        }
    }
}
