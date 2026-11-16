using System;
using System.Collections.Generic;

namespace Kraty
{
    /// <summary>
    /// Sealed-ish set of error codes the backend returns. Mirrors
    /// <c>api/errors.ts</c> on the backend. Kept as constants (rather
    /// than an enum) so the SDK survives the platform adding a new
    /// code without a forced release; consumers compare on strings,
    /// with the constants below as the canonical names.
    /// </summary>
    public static class KratyErrorCode
    {
        // ── core ─────────────────────────────────────────────────────
        public const string Unauthenticated = "unauthenticated";
        public const string SessionInvalid = "session_invalid";
        public const string Forbidden = "forbidden";
        public const string NotFound = "not_found";
        public const string ValidationFailed = "validation_failed";
        public const string Conflict = "conflict";
        public const string RateLimited = "rate_limited";
        public const string InternalError = "internal_error";
        public const string TenantMismatch = "tenant_mismatch";
        public const string IdempotencyConflict = "idempotency_conflict";

        // ── per-player auth ──────────────────────────────────────────
        public const string PlayerSecretInvalid = "player_secret_invalid";
        public const string PlayerAlreadyRegistered = "player_already_registered";

        // ── events / attempts ────────────────────────────────────────
        public const string EventDisabled = "event_disabled";
        public const string NoActiveWindow = "no_active_window";
        public const string NoLeaderboard = "no_leaderboard";
        public const string MaxAttemptsReached = "max_attempts_reached";
        public const string MaxDailyAttemptsReached = "max_daily_attempts_reached";
        public const string AttemptFinished = "attempt_finished";
        public const string InvalidMetric = "invalid_metric";
        public const string UnlockConditionFailed = "unlock_condition_failed";
        public const string InvalidUnlockCondition = "invalid_unlock_condition";

        // ── entry requirements / cost ────────────────────────────────
        public const string EntryRequirementFailed = "entry_requirement_failed";
        public const string InvalidEntryRequirement = "invalid_entry_requirement";
        public const string InsufficientEntryCost = "insufficient_entry_cost";

        // ── matchmaking ──────────────────────────────────────────────
        public const string LobbyForming = "lobby_forming";

        // ── inventory ────────────────────────────────────────────────
        public const string InventoryNotPermissive = "inventory_not_permissive";

        // ── chat anti-spam ───────────────────────────────────────────
        public const string ChatSlowMode = "chat_slow_mode";
        public const string ChatDuplicateMessage = "chat_duplicate_message";
        public const string ChatRateLimited = "chat_rate_limited";
        public const string ChatMuted = "chat_muted";
    }

    /// <summary>
    /// Backend error envelope: every non-2xx response (and the special
    /// 202 lobby-forming response) carries this shape.
    /// </summary>
    public sealed class KratyErrorPayload
    {
        public string Code { get; }
        public string Message { get; }
        public IReadOnlyDictionary<string, object?>? Details { get; }

        public KratyErrorPayload(string code, string message, IReadOnlyDictionary<string, object?>? details = null)
        {
            Code = code;
            Message = message;
            Details = details;
        }
    }

    /// <summary>
    /// Thrown for every non-2xx response (and the 202 lobby-forming
    /// case). <see cref="Status"/> is the HTTP status, <see cref="Code"/>
    /// + <c>Message</c> come from the backend's
    /// <c>{ error: { code, message, details? } }</c> envelope.
    ///
    /// Use the typed <c>Is...</c> properties to switch on the common
    /// SDK-relevant codes: they're cheaper to read than a chain of
    /// string comparisons and immune to typos.
    /// </summary>
    public sealed class KratyApiError : Exception
    {
        public int Status { get; }
        public string Code { get; }
        public IReadOnlyDictionary<string, object?>? Details { get; }

        public KratyApiError(int status, string code, string message, IReadOnlyDictionary<string, object?>? details = null)
            : base($"[{status}] {code}: {message}")
        {
            Status = status;
            Code = code;
            Details = details;
        }

        /// <summary>
        /// The response's <c>Retry-After</c> header, when it had one (set by
        /// the SDK's transport; null on errors built by hand). The chat outbox
        /// and the stream reconnect honour it on a 503.
        /// </summary>
        internal TimeSpan? RetryAfterHeader { get; set; }

        /// <summary>
        /// How long the server asked us to wait: the <c>Retry-After</c> header,
        /// else <c>details.retryAfterSeconds</c>, else null.
        /// </summary>
        internal TimeSpan? ServerRetryAfter =>
            RetryAfterHeader ?? (RetryAfterSecondsExact is double s ? TimeSpan.FromSeconds(s) : (TimeSpan?)null);

        /// <summary>
        /// Generic code matcher. Useful when matching on a code the
        /// SDK doesn't yet have a typed getter for (e.g. a new code
        /// the backend added before an SDK release).
        /// </summary>
        public bool Is(string code) => Code == code;

        // ── core ─────────────────────────────────────────────────────

        /// <summary>401: <c>Authorization</c> header missing on a protected route.</summary>
        public bool IsUnauthenticated => Code == KratyErrorCode.Unauthenticated;

        /// <summary>401: Bearer token is malformed, revoked, or rejected.</summary>
        public bool IsSessionInvalid => Code == KratyErrorCode.SessionInvalid;

        /// <summary>403: authenticated but lacks the permission for this route.</summary>
        public bool IsForbidden => Code == KratyErrorCode.Forbidden;

        /// <summary>404: referenced resource doesn't exist or is archived.</summary>
        public bool IsNotFound => Code == KratyErrorCode.NotFound;

        /// <summary>400: request body / query failed schema validation. <c>Details</c> carries field-level errors.</summary>
        public bool IsValidationFailed => Code == KratyErrorCode.ValidationFailed;

        /// <summary>409: generic mutation conflict. More specific 409 codes get their own getters; this catches the rest.</summary>
        public bool IsConflict => Code == KratyErrorCode.Conflict;

        /// <summary>429: per-key rate limit exceeded. The SDK auto-retries with backoff before surfacing this.</summary>
        public bool IsRateLimited => Code == KratyErrorCode.RateLimited;

        /// <summary>500: unhandled exception. Surface a generic "something went wrong" to the player.</summary>
        public bool IsInternalError => Code == KratyErrorCode.InternalError;

        /// <summary>403: cross-studio access attempt (RLS rejected). Shouldn't happen via the SDK.</summary>
        public bool IsTenantMismatch => Code == KratyErrorCode.TenantMismatch;

        /// <summary>
        /// 409: same <c>idempotencyKey</c> used with a different
        /// request body within the 24h cache TTL. The SDK auto-stamps
        /// fresh keys per write so you only see this when you've
        /// supplied your own key.
        /// </summary>
        public bool IsIdempotencyConflict => Code == KratyErrorCode.IdempotencyConflict;

        // ── per-player auth ──────────────────────────────────────────

        /// <summary>
        /// 401: <c>X-Player-Secret</c> is missing, malformed, or
        /// doesn't match the stored hash for the player. Triggers
        /// your re-authentication flow (usually wipe the local
        /// secret + re-call <see cref="Kraty.ConnectAsPlayerAsync"/>).
        /// </summary>
        public bool IsPlayerSecretInvalid => Code == KratyErrorCode.PlayerSecretInvalid;

        /// <summary>
        /// 409: the player already has a registered secret.
        /// <see cref="Kraty.ConnectAsPlayerAsync"/> handles this
        /// automatically by retrying with <c>force=true</c> in dev
        /// /test envs; in production keys this surfaces here and
        /// should route to your account-recovery flow.
        /// </summary>
        public bool IsPlayerAlreadyRegistered => Code == KratyErrorCode.PlayerAlreadyRegistered;

        // ── events / attempts ────────────────────────────────────────

        /// <summary>409: the event is configured but disabled.</summary>
        public bool IsEventDisabled => Code == KratyErrorCode.EventDisabled;

        /// <summary>409: the event has no currently-active window. Player is between scheduled windows.</summary>
        public bool IsNoActiveWindow => Code == KratyErrorCode.NoActiveWindow;

        /// <summary>503: server couldn't allocate / find the leaderboard. Usually transient, so retry after backoff.</summary>
        public bool IsNoLeaderboard => Code == KratyErrorCode.NoLeaderboard;

        /// <summary>429: player burned all attempts for the current event window.</summary>
        public bool IsMaxAttemptsReached => Code == KratyErrorCode.MaxAttemptsReached;

        /// <summary>429: per-day attempt cap reached. Player should wait until midnight in the event's timezone.</summary>
        public bool IsMaxDailyAttemptsReached => Code == KratyErrorCode.MaxDailyAttemptsReached;

        /// <summary>409: reported progress on an attempt that's already <c>completed</c> / <c>expired</c>. Refresh state.</summary>
        public bool IsAttemptFinished => Code == KratyErrorCode.AttemptFinished;

        /// <summary>400: <c>progress</c> referenced a metric key the event doesn't declare. SDK / game-config bug.</summary>
        public bool IsInvalidMetric => Code == KratyErrorCode.InvalidMetric;

        /// <summary>403: player can't see the event yet (visibility gate failed).</summary>
        public bool IsUnlockConditionFailed => Code == KratyErrorCode.UnlockConditionFailed;

        /// <summary>500: event config has a malformed unlock condition tree. Operator should fix in the portal.</summary>
        public bool IsInvalidUnlockCondition => Code == KratyErrorCode.InvalidUnlockCondition;

        // ── entry requirements / cost ────────────────────────────────

        /// <summary>
        /// 403: player attempted an event whose entry requirement
        /// failed (e.g. "must own item X"). Show a locked-event
        /// dialog or surface what's missing from the message.
        /// </summary>
        public bool IsEntryRequirementFailed => Code == KratyErrorCode.EntryRequirementFailed;

        /// <summary>500: event config has a malformed entry requirement.</summary>
        public bool IsInvalidEntryRequirement => Code == KratyErrorCode.InvalidEntryRequirement;

        /// <summary>
        /// 402: paid event the player can't afford. <c>Message</c>
        /// names the shortfall resource ("not enough cash to enter;
        /// need 50"). Surface a "buy more X" prompt or a free-event
        /// fallback. The server's atomic debit was rolled back, so
        /// partial debits never persist.
        /// </summary>
        public bool IsInsufficientEntryCost => Code == KratyErrorCode.InsufficientEntryCost;

        // ── matchmaking ──────────────────────────────────────────────

        /// <summary>
        /// 202: lobby-matched event whose lobby isn't yet at capacity.
        /// Not a hard failure: poll the lobby endpoint (using
        /// <c>Details["lobbyId"]</c>) and retry <c>events.start</c>
        /// once it transitions out of <c>forming</c>.
        /// </summary>
        public bool IsLobbyForming => Code == KratyErrorCode.LobbyForming;

        /// <summary>
        /// 403: a client-side <c>GrantAsync</c> on a game whose inventory
        /// management isn't <c>permissive</c>. Enable it in the game's
        /// Settings, or grant from your backend with a
        /// <c>server_integration</c> key.
        /// </summary>
        public bool IsInventoryNotPermissive => Code == KratyErrorCode.InventoryNotPermissive;

        // ── chat anti-spam ───────────────────────────────────────────
        // Chat sends and edits are never auto-retried on 429: a retried
        // refusal is another spam strike, and enough strikes mute the
        // player automatically.

        /// <summary>429: sent again before the conversation's cooldown passed.</summary>
        public bool IsChatSlowMode => Code == KratyErrorCode.ChatSlowMode;

        /// <summary>429: the same text again in this conversation within the duplicate window.</summary>
        public bool IsChatDuplicateMessage => Code == KratyErrorCode.ChatDuplicateMessage;

        /// <summary>429: over the game's messages-per-minute.</summary>
        public bool IsChatRateLimited => Code == KratyErrorCode.ChatRateLimited;

        /// <summary>
        /// 403: the player is muted. Repeated spam refusals mute
        /// automatically; an <c>OnRestriction</c> event arrives on the chat
        /// stream too.
        /// </summary>
        public bool IsChatMuted => Code == KratyErrorCode.ChatMuted;

        /// <summary>
        /// Seconds to wait before trying again (<c>details.retryAfterSeconds</c>,
        /// on the chat 429s), or null when the error carries none. Don't
        /// resend sooner.
        /// </summary>
        public int? RetryAfterSeconds =>
            RetryAfterSecondsExact is double s ? (int)Math.Ceiling(s) : (int?)null;

        private double? RetryAfterSecondsExact
        {
            get
            {
                if (Details == null || !Details.TryGetValue("raw", out var raw) || !(raw is string json)) return null;
                try
                {
                    var v = Newtonsoft.Json.Linq.JObject.Parse(json)["retryAfterSeconds"];
                    if (v == null || (v.Type != Newtonsoft.Json.Linq.JTokenType.Integer && v.Type != Newtonsoft.Json.Linq.JTokenType.Float)) return null;
                    return Math.Max(0, (double)v);
                }
                catch (Newtonsoft.Json.JsonException)
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// True when the SDK refused the call itself, without a request: a
        /// chat send while the conversation's cooldown is still running
        /// (<c>details.local: true</c>). Not a spam strike.
        /// </summary>
        public bool IsLocal
        {
            get
            {
                if (Details == null || !Details.TryGetValue("raw", out var raw) || !(raw is string json)) return false;
                try
                {
                    var v = Newtonsoft.Json.Linq.JObject.Parse(json)["local"];
                    return v != null && v.Type == Newtonsoft.Json.Linq.JTokenType.Boolean && (bool)v;
                }
                catch (Newtonsoft.Json.JsonException)
                {
                    return false;
                }
            }
        }
    }

    /// <summary>
    /// Network / HTTP-layer failure that didn't produce an HTTP
    /// response (DNS, socket reset, timeout, etc.). The SDK
    /// auto-retries network errors with backoff before surfacing
    /// this; by the time you see one, the retry budget is exhausted.
    /// <see cref="KratyChatNotSentError"/> derives from it, so an existing
    /// <c>catch (KratyNetworkError)</c> also catches a chat message the
    /// outbox gave up on.
    /// </summary>
    public class KratyNetworkError : Exception
    {
        public Exception? OriginalCause { get; }

        public KratyNetworkError(string message, Exception? originalCause = null)
            : base(message, originalCause)
        {
            OriginalCause = originalCause;
        }
    }

    /// <summary>
    /// A chat message (or edit) the outbox could not deliver within
    /// <see cref="KratyClientOptions.ChatOutboxTimeout"/>: the server stayed
    /// busy (5xx) or the network stayed down the whole time. Nothing was
    /// refused, so it is safe to try again: show the message as "not sent,
    /// tap to retry" and call <see cref="RetryAsync"/> (or send again with
    /// the same <see cref="ClientMessageId"/>). The server dedupes by
    /// <c>clientMessageId</c>, so a retry never posts twice, even if an
    /// earlier attempt did reach it.
    /// </summary>
    public sealed class KratyChatNotSentError : KratyNetworkError
    {
        private readonly Func<System.Threading.CancellationToken, System.Threading.Tasks.Task<ChatMessage>> _resend;

        /// <summary>The conversation the message was for.</summary>
        public string ConversationId { get; }

        /// <summary>The id the message was sent with; null for an edit.</summary>
        public string? ClientMessageId { get; }

        /// <summary>The last failure seen: a <see cref="KratyNetworkError"/> or a 5xx <see cref="KratyApiError"/>.</summary>
        public Exception LastError { get; }

        internal KratyChatNotSentError(
            string message,
            string conversationId,
            string? clientMessageId,
            Exception lastError,
            Func<System.Threading.CancellationToken, System.Threading.Tasks.Task<ChatMessage>> resend)
            : base(message, lastError)
        {
            ConversationId = conversationId;
            ClientMessageId = clientMessageId;
            LastError = lastError;
            _resend = resend;
        }

        /// <summary>
        /// Sends it again through the outbox: same body, same
        /// <see cref="ClientMessageId"/>, a fresh timeout budget. For an edit,
        /// re-runs the edit. Resolves with the message.
        /// </summary>
        public System.Threading.Tasks.Task<ChatMessage> RetryAsync(System.Threading.CancellationToken ct = default) => _resend(ct);
    }
}
