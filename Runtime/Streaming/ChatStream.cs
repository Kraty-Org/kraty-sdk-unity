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

    /// <summary>A public channel the stream follows.</summary>
    public sealed class ChatStreamChannel
    {
        [JsonProperty("key")] public string Key { get; set; } = string.Empty;
        [JsonProperty("conversationId")] public string ConversationId { get; set; } = string.Empty;
    }

    /// <summary><c>ready</c>: every listener is wired; nothing sent after this is missed.</summary>
    public sealed class ChatStreamReady
    {
        [JsonProperty("externalPlayerId")] public string ExternalPlayerId { get; set; } = string.Empty;
        /// <summary>The inbox conversations this stream follows.</summary>
        [JsonProperty("conversationIds")] public List<string> ConversationIds { get; set; } = new();
        /// <summary>The channels named in <c>channels</c> that resolved, with their conversation ids.</summary>
        [JsonProperty("channels")] public List<ChatStreamChannel> Channels { get; set; } = new();
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

    /// <summary><c>close</c>: the server is draining; reconnect.</summary>
    public sealed class ChatCloseEvent
    {
        [JsonProperty("reason")] public string Reason { get; set; } = string.Empty;
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
    /// The SDK does NOT auto-reconnect. On <see cref="OnClose"/> or
    /// <see cref="OnError"/>, reopen after a backoff and catch each
    /// conversation up with <c>Chat.MessagesAsync(id, after: lastSeqSeen)</c>.
    /// </para>
    /// </summary>
    public sealed class ChatStream : IDisposable
    {
        private readonly CancellationTokenSource _cts;
        private readonly HttpResponseMessage _response;
        private Task _readLoop = Task.CompletedTask;
        private int _disposed;

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
        /// <summary>The server is draining: reopen the stream.</summary>
        public Action<ChatCloseEvent>? OnClose { get; set; }
        /// <summary>Transport / parse failures, and exceptions thrown by your callbacks.</summary>
        public Action<Exception>? OnError { get; set; }

        /// <summary>The <c>ready</c> payload once it arrived (null before).</summary>
        public ChatStreamReady? Ready { get; private set; }

        internal ChatStream(HttpResponseMessage response, CancellationTokenSource cts)
        {
            _response = response;
            _cts = cts;
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
            try { _response.Dispose(); } catch { /* swallow */ }
            try { _cts.Dispose(); } catch { /* swallow */ }
        }

        public void Dispose()
        {
            _ = CancelAsync();
        }

        internal void Dispatch(string kind, Dictionary<string, JToken> data)
        {
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

        private static T As<T>(Dictionary<string, JToken> data) where T : new()
        {
            var obj = new JObject();
            foreach (var kv in data) obj[kv.Key] = kv.Value.DeepClone();
            return obj.ToObject<T>() ?? new T();
        }
    }

    /// <summary>
    /// Opens the player's chat SSE stream. Does NOT auto-reconnect.
    /// Transport + parsing live in <see cref="SseReader"/>.
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
            CancellationToken cancellationToken
        )
        {
            var query = new List<string>();
            var keys = channels?.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => Uri.EscapeDataString(c.Trim())).ToList();
            if (keys != null && keys.Count > 0) query.Add("channels=" + string.Join(",", keys));
            if (filter != null) query.Add("filter=" + filter);
            var url = $"{baseUrl}/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/chat/stream"
                + (query.Count > 0 ? "?" + string.Join("&", query) : string.Empty);
            var headers = filter != null ? new Dictionary<string, string> { [FilterHeader] = filter } : null;

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
    }
}
