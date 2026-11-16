using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Kraty
{
    /// <summary>
    /// Resource client for the <c>/sdk/v1/players/.../events/...</c>
    /// surface: list, start, progress.
    /// </summary>
    public sealed class EventsClient
    {
        private readonly KratyClient _client;

        public EventsClient(KratyClient client) => _client = client;

        /// <summary>
        /// GET <c>/sdk/v1/players/:externalId/events</c>: events whose
        /// current window the active player can start now. Pass
        /// <paramref name="as"/> to address a different player
        /// (server-side tooling only).
        /// </summary>
        public async Task<List<EventListing>> ListForPlayerAsync(
            string? @as = null,
            CancellationToken ct = default)
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<List<EventListing>>>(
                HttpMethod.Get,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/events",
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new List<EventListing>();
        }

        /// <summary>
        /// POST <c>/sdk/v1/players/:p/events/:e/start</c>: start an
        /// attempt for the active player. Atomically debits any
        /// configured entry cost; on insufficient resources throws
        /// <see cref="KratyApiError"/> with
        /// <c>IsInsufficientEntryCost == true</c> (the debit is
        /// rolled back, so partial spends never persist).
        ///
        /// <para>
        /// If matchmaking is still forming, throws with
        /// <c>IsLobbyForming == true</c>; poll the lobby endpoint and
        /// retry start.
        /// </para>
        /// </summary>
        public async Task<StartAttemptResponse> StartAsync(
            string eventKey,
            IDictionary<string, object?>? playerContext = null,
            string? @as = null,
            CancellationToken ct = default
        )
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var body = new Dictionary<string, object?>();
            if (playerContext != null) body["playerContext"] = playerContext;
            var env = await _client.RequestAsync<DataEnvelope<StartAttemptResponse>>(
                HttpMethod.Post,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/events/{Uri.EscapeDataString(eventKey)}/start",
                body: body,
                cancellationToken: ct
            ).ConfigureAwait(false);
            var data = env.Data ?? new StartAttemptResponse();
            // Track the session/event board for finalization catch-up (docs/05b);
            // fire-and-forget so it never adds latency to start.
            if (!string.IsNullOrEmpty(data.LeaderboardId))
            {
                _ = _client.TrackMembershipAsync(MembershipRef.EventLeaderboard(data.LeaderboardId, eventKey));
            }
            return data;
        }

        /// <summary>
        /// POST <c>/sdk/v1/players/:p/events/:e/attempts/:a/progress</c>
        /// pushes a metric update for the active player. Mode
        /// <c>"set"</c> writes the value, <c>"increment"</c> adds.
        /// Completing the metric target ends the attempt and fires
        /// the reward pipeline server-side.
        /// </summary>
        public async Task<ProgressResult> ProgressAsync(
            string eventKey,
            string attemptId,
            ProgressInput input,
            string? @as = null,
            CancellationToken ct = default
        )
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<ProgressResult>>(
                HttpMethod.Post,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/events/{Uri.EscapeDataString(eventKey)}/attempts/{Uri.EscapeDataString(attemptId)}/progress",
                body: input,
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new ProgressResult();
        }

        /// <summary>
        /// POST <c>/sdk/v1/players/:p/events/:e/attempts/:a/finish</c>: end
        /// an in-progress attempt now, locking in its current score. This is
        /// the player-driven "I'm done" / "cash out my run" action, needed
        /// for score-attack events (no completion target) where the player
        /// decides when the run ends. <c>Outcome</c> is "completed"
        /// (score-attack end / target met, so completion rewards roll) or
        /// "expired" (a target event ended early). Pull rewards afterwards
        /// with <c>Grants.CollectAllAsync</c>.
        /// </summary>
        public async Task<FinishAttemptResult> FinishAsync(
            string eventKey,
            string attemptId,
            string? @as = null,
            CancellationToken ct = default
        )
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<FinishAttemptResult>>(
                HttpMethod.Post,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/events/{Uri.EscapeDataString(eventKey)}/attempts/{Uri.EscapeDataString(attemptId)}/finish",
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new FinishAttemptResult();
        }

        // ── Co-op team events (docs/24) ───────────────────────────────

        private static string EventPath(string externalPlayerId, string eventKey, string rest) =>
            $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/events/{Uri.EscapeDataString(eventKey)}{rest}";

        /// <summary>
        /// GET <c>/sdk/v1/players/:p/event-teams/:id/members</c>: your team's
        /// members by contribution, with <c>IsSelf</c> on your row. Only a
        /// member may read it. Page with <paramref name="cursor"/>.
        /// </summary>
        public async Task<EventTeamMembersPage> TeamMembersAsync(
            string eventTeamId,
            string? cursor = null,
            int? limit = null,
            string? @as = null,
            CancellationToken ct = default
        )
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var qs = new List<string>();
            if (!string.IsNullOrEmpty(cursor)) qs.Add($"cursor={Uri.EscapeDataString(cursor!)}");
            if (limit.HasValue) qs.Add($"limit={limit.Value}");
            var suffix = qs.Count > 0 ? "?" + string.Join("&", qs) : string.Empty;
            var env = await _client.RequestAsync<DataEnvelope<EventTeamMembersPage>>(
                HttpMethod.Get,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/event-teams/{Uri.EscapeDataString(eventTeamId)}/members{suffix}",
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new EventTeamMembersPage();
        }

        /// <summary>POST <c>…/events/:e/teams/match</c>: your auto-matched team (<c>auto_match</c> events). Idempotent.</summary>
        public async Task<TeamMatchResult> MatchTeamAsync(string eventKey, string? @as = null, CancellationToken ct = default)
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<TeamMatchResult>>(
                HttpMethod.Post, EventPath(externalPlayerId, eventKey, "/teams/match"), body: new { }, cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new TeamMatchResult();
        }

        /// <summary>
        /// POST <c>…/partners/invites</c>: invite a partner by external id (or
        /// pass <paramref name="friendCode"/> instead). Their pending invite to you
        /// is accepted instead (<c>Status = accepted</c>).
        /// </summary>
        public async Task<PartnerInvite> InvitePartnerAsync(
            string eventKey,
            string? externalPlayerId = null,
            string? friendCode = null,
            string? @as = null,
            CancellationToken ct = default
        )
        {
            if ((externalPlayerId == null) == (friendCode == null))
                throw new ArgumentException("pass exactly one of externalPlayerId or friendCode");
            var me = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            object body = externalPlayerId != null ? new { externalPlayerId } : new { friendCode };
            var env = await _client.RequestAsync<DataEnvelope<PartnerInvite>>(
                HttpMethod.Post, EventPath(me, eventKey, "/partners/invites"), body: body, cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new PartnerInvite();
        }

        /// <summary>GET <c>…/partners/invites</c>: pending invites to you (<c>in</c>, default) or from you (<c>out</c>).</summary>
        public async Task<List<PartnerInvite>> PartnerInvitesAsync(
            string eventKey,
            string direction = "in",
            string? @as = null,
            CancellationToken ct = default
        )
        {
            var me = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<List<PartnerInvite>>>(
                HttpMethod.Get,
                EventPath(me, eventKey, $"/partners/invites?direction={Uri.EscapeDataString(direction)}"),
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new List<PartnerInvite>();
        }

        /// <summary>Accept an invite sent to you: the pair's bar goes live.</summary>
        public Task<PartnerInvite> AcceptInviteAsync(string eventKey, string eventTeamId, string? @as = null, CancellationToken ct = default) =>
            AnswerInviteAsync(eventKey, eventTeamId, "accept", @as, ct);

        /// <summary>Decline an invite sent to you (both slots free up).</summary>
        public Task<PartnerInvite> DeclineInviteAsync(string eventKey, string eventTeamId, string? @as = null, CancellationToken ct = default) =>
            AnswerInviteAsync(eventKey, eventTeamId, "decline", @as, ct);

        /// <summary>Withdraw an invite you sent (both slots free up).</summary>
        public Task<PartnerInvite> CancelInviteAsync(string eventKey, string eventTeamId, string? @as = null, CancellationToken ct = default) =>
            AnswerInviteAsync(eventKey, eventTeamId, "cancel", @as, ct);

        private async Task<PartnerInvite> AnswerInviteAsync(string eventKey, string eventTeamId, string action, string? @as, CancellationToken ct)
        {
            var me = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<PartnerInvite>>(
                HttpMethod.Post,
                EventPath(me, eventKey, $"/partners/invites/{Uri.EscapeDataString(eventTeamId)}/{action}"),
                body: new { },
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new PartnerInvite();
        }

        /// <summary>POST <c>…/partners/random</c>: pair with someone waiting, or open a bar that waits (solo after the event's fallback).</summary>
        public async Task<RandomPartnerResult> RandomPartnerAsync(string eventKey, string? @as = null, CancellationToken ct = default)
        {
            var me = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<RandomPartnerResult>>(
                HttpMethod.Post, EventPath(me, eventKey, "/partners/random"), body: new { }, cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new RandomPartnerResult();
        }

        /// <summary>GET <c>…/partners/suggestions</c>: friends first, then recently active players with a free slot.</summary>
        public async Task<List<PartnerSuggestion>> PartnerSuggestionsAsync(
            string eventKey,
            int? limit = null,
            string? @as = null,
            CancellationToken ct = default
        )
        {
            var me = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var qs = limit.HasValue ? $"?limit={limit.Value}" : string.Empty;
            var env = await _client.RequestAsync<DataEnvelope<List<PartnerSuggestion>>>(
                HttpMethod.Get, EventPath(me, eventKey, $"/partners/suggestions{qs}"), cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new List<PartnerSuggestion>();
        }
    }

    /// <summary>
    /// Resource client for <c>/sdk/v1/leaderboards/:key</c>: the
    /// dashboard-configured cross-event leaderboards your studio defines
    /// (e.g. <c>"weekly_global"</c>, <c>"weekly_region"</c>). This is the
    /// surface most game UI wants. For the auto-created per-event-window
    /// board addressed by UUID, see <see cref="EventLeaderboardsClient"/>.
    /// </summary>
    public sealed class LeaderboardsClient
    {
        private readonly KratyClient _client;
        public LeaderboardsClient(KratyClient client) => _client = client;

        /// <summary>
        /// GET <c>/sdk/v1/leaderboards</c>: the game's dashboard-configured
        /// boards (archived ones left out), so the client can discover keys
        /// instead of hard-coding them.
        /// </summary>
        /// <param name="ct">Cancellation.</param>
        public async Task<List<LeaderboardSummary>> ListAsync(CancellationToken ct = default)
        {
            var env = await _client.RequestAsync<DataEnvelope<List<LeaderboardSummary>>>(
                HttpMethod.Get, "/sdk/v1/leaderboards", cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new List<LeaderboardSummary>();
        }

        /// <summary>
        /// GET <c>/sdk/v1/leaderboards/:key</c>: snapshot read of a
        /// configurable, cross-event leaderboard.
        ///
        /// <para>
        /// <c>opts.Segment</c> is required only for <c>context</c>
        /// segmentation. For <c>progression</c>-segmented boards leave
        /// it null and the server derives the caller's division.
        /// Unsegmented boards ignore it.
        /// </para>
        /// </summary>
        /// <param name="key">The board's game-scoped key from the dashboard.</param>
        /// <param name="opts">Optional limit, segment, period, includeSelf.</param>
        /// <param name="ct">Cancellation.</param>
        public async Task<Leaderboard> ReadAsync(
            string key,
            LeaderboardReadOptions? opts = null,
            CancellationToken ct = default
        )
        {
            opts ??= new LeaderboardReadOptions();
            var qs = new List<string>();
            if (opts.Limit.HasValue) qs.Add($"limit={opts.Limit.Value}");
            var progressionKeys = opts.Progression == null ? string.Empty : string.Join(",", opts.Progression);
            if (progressionKeys.Length > 0) qs.Add($"progression={Uri.EscapeDataString(progressionKeys)}");
            if (!string.IsNullOrEmpty(opts.Segment)) qs.Add($"segment={Uri.EscapeDataString(opts.Segment!)}");
            if (!string.IsNullOrEmpty(opts.Period)) qs.Add($"period={Uri.EscapeDataString(opts.Period!)}");
            if (opts.IncludeSelf)
            {
                // Lazily resolve the active player when the caller didn't pass an
                // explicit ExternalId; same contract as every player-scoped method.
                var externalId = !string.IsNullOrEmpty(opts.ExternalId)
                    ? opts.ExternalId!
                    : (await _client.EnsureIdentityAsync(ct).ConfigureAwait(false)).ExternalPlayerId;
                qs.Add("includeSelf=true");
                qs.Add($"externalId={Uri.EscapeDataString(externalId)}");
            }
            var path = qs.Count == 0
                ? $"/sdk/v1/leaderboards/{Uri.EscapeDataString(key)}"
                : $"/sdk/v1/leaderboards/{Uri.EscapeDataString(key)}?{string.Join("&", qs)}";

            var env = await _client.RequestAsync<DataEnvelope<Leaderboard>>(
                HttpMethod.Get, path, cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new Leaderboard();
        }

        /// <summary>
        /// POST <c>/sdk/v1/players/:p/leaderboards/:key/score</c>:
        /// submit a score for the active player directly to a
        /// dashboard-configured board, outside an event attempt.
        /// Returns the player's new score + rank.
        ///
        /// <para>
        /// <c>opts.Segment</c> is required only for <c>context</c>
        /// segmentation (pass the bucket value). For
        /// <c>progression</c>-segmented boards leave it null and the
        /// server derives the player's division; unsegmented boards
        /// ignore it. Auto-stamped idempotency key unless you provide
        /// one via <c>opts.IdempotencyKey</c>.
        /// </para>
        ///
        /// <para>
        /// Throws <see cref="KratyApiError"/>:
        /// <c>client_scoring_disabled</c> (403) when the board is
        /// server-only, <c>score_not_supported</c> (400) on a
        /// progression-ranked board, <c>not_found</c> (404),
        /// <c>validation_failed</c> (400). Set <c>opts.ExternalId</c>
        /// to address a different player (server-side tooling only).
        /// </para>
        /// </summary>
        public async Task<LeaderboardScoreResult> SubmitScoreAsync(
            string key,
            double value,
            LeaderboardSubmitOptions? opts = null,
            CancellationToken ct = default
        )
        {
            opts ??= new LeaderboardSubmitOptions();
            var externalPlayerId = await _client.ResolvePlayerIdAsync(opts.ExternalId, ct).ConfigureAwait(false);
            var body = new Dictionary<string, object?> { ["value"] = value };
            if (!string.IsNullOrEmpty(opts.Segment)) body["segment"] = opts.Segment;
            if (!string.IsNullOrEmpty(opts.IdempotencyKey)) body["idempotencyKey"] = opts.IdempotencyKey;
            var env = await _client.RequestAsync<DataEnvelope<LeaderboardScoreResult>>(
                HttpMethod.Post,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/leaderboards/{Uri.EscapeDataString(key)}/score",
                body: body,
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new LeaderboardScoreResult();
        }

        /// <summary>
        /// POST <c>/sdk/v1/players/:p/leaderboards/:key/join</c>: add the
        /// active player to a configurable board at score 0 WITHOUT
        /// submitting a score (they just "appear"), and return the current
        /// standings for their segment. Idempotent; never resets an
        /// existing score.
        ///
        /// <para>
        /// <c>opts.Segment</c> is the bucket value for <c>context</c>-segmented
        /// boards; derived server-side for <c>progression</c>-segmented
        /// boards. Set <c>opts.ExternalId</c> to address a different player
        /// (server-side tooling only).
        /// </para>
        /// </summary>
        /// <param name="key">The board's game-scoped key from the dashboard.</param>
        /// <param name="opts">Optional segment, limit, externalId.</param>
        /// <param name="ct">Cancellation.</param>
        public async Task<Leaderboard> JoinAsync(
            string key,
            LeaderboardJoinOptions? opts = null,
            CancellationToken ct = default
        )
        {
            opts ??= new LeaderboardJoinOptions();
            var externalPlayerId = await _client.ResolvePlayerIdAsync(opts.ExternalId, ct).ConfigureAwait(false);
            var qs = new List<string>();
            if (opts.Limit.HasValue) qs.Add($"limit={opts.Limit.Value}");
            var body = new Dictionary<string, object?>();
            if (!string.IsNullOrEmpty(opts.Segment)) body["segment"] = opts.Segment;
            var basePath = $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/leaderboards/{Uri.EscapeDataString(key)}/join";
            var path = qs.Count == 0 ? basePath : $"{basePath}?{string.Join("&", qs)}";
            var env = await _client.RequestAsync<DataEnvelope<Leaderboard>>(
                HttpMethod.Post, path, body: body, cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new Leaderboard();
        }

        /// <summary>
        /// GET <c>/sdk/v1/leaderboards/:key/standings</c>: flexible
        /// multi-segment read. Returns one block per segment
        /// (<c>opts.Scope</c> picks which), each flagging the caller
        /// (<c>isSelf</c> on entries, <c>selfRank</c>, <c>participated</c>).
        /// Works live (<c>opts.Period == "current"</c>) or for a past period.
        ///
        /// <para>
        /// Use this over <see cref="ReadAsync"/> when you want "my division",
        /// "every division I'm in" (<c>scope: "mine"</c>), or the whole
        /// ladder (<c>scope: "all"</c>). <c>self_segment</c>/<c>mine</c>
        /// resolve the caller from <c>opts.ExternalId</c> (or the SDK's
        /// active identity).
        /// </para>
        /// </summary>
        /// <param name="key">The board's game-scoped key from the dashboard.</param>
        /// <param name="opts">Optional scope, segment, period, externalId, limit, maxSegments.</param>
        /// <param name="ct">Cancellation.</param>
        public async Task<BoardStandings> StandingsAsync(
            string key,
            StandingsReadOptions? opts = null,
            CancellationToken ct = default
        )
        {
            opts ??= new StandingsReadOptions();
            var scope = string.IsNullOrEmpty(opts.Scope) ? "all" : opts.Scope!;
            var qs = new List<string> { $"scope={Uri.EscapeDataString(scope)}" };
            if (!string.IsNullOrEmpty(opts.Segment)) qs.Add($"segment={Uri.EscapeDataString(opts.Segment!)}");
            if (!string.IsNullOrEmpty(opts.Period)) qs.Add($"period={Uri.EscapeDataString(opts.Period!)}");
            if (opts.Limit.HasValue) qs.Add($"limit={opts.Limit.Value}");
            var progressionKeys = opts.Progression == null ? string.Empty : string.Join(",", opts.Progression);
            if (progressionKeys.Length > 0) qs.Add($"progression={Uri.EscapeDataString(progressionKeys)}");
            if (opts.MaxSegments.HasValue) qs.Add($"maxSegments={opts.MaxSegments.Value}");
            // self_segment / mine need a caller; auto-resolve the active
            // identity when the caller didn't pass one explicitly.
            var externalId = opts.ExternalId;
            if (string.IsNullOrEmpty(externalId) && (scope == "self_segment" || scope == "mine"))
            {
                externalId = (await _client.EnsureIdentityAsync(ct).ConfigureAwait(false)).ExternalPlayerId;
            }
            if (!string.IsNullOrEmpty(externalId)) qs.Add($"externalId={Uri.EscapeDataString(externalId!)}");
            var path = $"/sdk/v1/leaderboards/{Uri.EscapeDataString(key)}/standings?{string.Join("&", qs)}";
            var env = await _client.RequestAsync<DataEnvelope<BoardStandings>>(
                HttpMethod.Get, path, cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new BoardStandings();
        }

        /// <summary>
        /// GET <c>/sdk/v1/leaderboards/:key/periods</c>: list the
        /// finalized snapshot periods available for this leaderboard,
        /// newest first. Pair with
        /// <see cref="ReadAsync"/> + <see cref="LeaderboardReadOptions.Period"/>
        /// to render historical snapshots ("last week's top 10").
        /// </summary>
        /// <param name="key">The board's game-scoped key from the dashboard.</param>
        /// <param name="limit">1–52, default 12 server-side.</param>
        /// <param name="ct">Cancellation.</param>
        public async Task<LeaderboardPeriods> ListPeriodsAsync(
            string key,
            int? limit = null,
            CancellationToken ct = default
        )
        {
            var path = limit.HasValue
                ? $"/sdk/v1/leaderboards/{Uri.EscapeDataString(key)}/periods?limit={limit.Value}"
                : $"/sdk/v1/leaderboards/{Uri.EscapeDataString(key)}/periods";
            var env = await _client.RequestAsync<DataEnvelope<LeaderboardPeriods>>(
                HttpMethod.Get, path, cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new LeaderboardPeriods();
        }
    }

    /// <summary>
    /// Resource client for <c>/sdk/v1/event-leaderboards/:id</c>: the
    /// auto-generated per-event-window leaderboard, addressed by the
    /// UUID <c>Events.StartAsync(...)</c> returns in
    /// <c>attempt.LeaderboardId</c>. Includes Server-Sent-Events live
    /// streaming. For the dashboard-configured cross-event boards
    /// addressed by key, see <see cref="LeaderboardsClient"/>.
    /// </summary>
    public sealed class EventLeaderboardsClient
    {
        private readonly KratyClient _client;
        public EventLeaderboardsClient(KratyClient client) => _client = client;
        /// <summary>The underlying client; lets the live subscription route
        /// `finalized` events into the finalization registry.</summary>
        internal KratyClient Client => _client;

        /// <summary>
        /// GET <c>/sdk/v1/event-leaderboards/:id</c>: snapshot read of
        /// one event window's leaderboard. The id is the UUID
        /// <c>Events.StartAsync(...)</c> returns as
        /// <c>start.Attempt.LeaderboardId</c>.
        /// </summary>
        public async Task<EventLeaderboard> ReadAsync(
            string leaderboardId,
            EventLeaderboardReadOptions? opts = null,
            CancellationToken ct = default
        )
        {
            opts ??= new EventLeaderboardReadOptions();
            var qs = new List<string>();
            if (opts.Limit.HasValue) qs.Add($"limit={opts.Limit.Value}");
            var progressionKeys = opts.Progression == null ? string.Empty : string.Join(",", opts.Progression);
            if (progressionKeys.Length > 0) qs.Add($"progression={Uri.EscapeDataString(progressionKeys)}");
            if (opts.IncludeSelf)
            {
                var externalId = !string.IsNullOrEmpty(opts.ExternalId)
                    ? opts.ExternalId!
                    : (await _client.EnsureIdentityAsync(ct).ConfigureAwait(false)).ExternalPlayerId;
                qs.Add("includeSelf=true");
                qs.Add($"externalId={Uri.EscapeDataString(externalId)}");
            }
            var path = qs.Count == 0
                ? $"/sdk/v1/event-leaderboards/{Uri.EscapeDataString(leaderboardId)}"
                : $"/sdk/v1/event-leaderboards/{Uri.EscapeDataString(leaderboardId)}?{string.Join("&", qs)}";

            var env = await _client.RequestAsync<DataEnvelope<EventLeaderboard>>(
                HttpMethod.Get, path, cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new EventLeaderboard();
        }

        /// <summary>
        /// POST <c>/sdk/v1/players/:p/event-leaderboards/:id/join</c>: add
        /// the active player to a per-event-window board at score 0 WITHOUT
        /// starting a scoring attempt, and return the current board.
        /// Idempotent. Throws <see cref="KratyApiError"/> with code
        /// <c>conflict</c> (409) once the window has finalized. Set
        /// <c>opts.ExternalId</c> to address a different player (server-side
        /// tooling only).
        /// </summary>
        /// <param name="leaderboardId">The event leaderboard UUID.</param>
        /// <param name="opts">Optional limit, externalId.</param>
        /// <param name="ct">Cancellation.</param>
        public async Task<EventLeaderboard> JoinAsync(
            string leaderboardId,
            EventLeaderboardJoinOptions? opts = null,
            CancellationToken ct = default
        )
        {
            opts ??= new EventLeaderboardJoinOptions();
            var externalPlayerId = await _client.ResolvePlayerIdAsync(opts.ExternalId, ct).ConfigureAwait(false);
            var qs = new List<string>();
            if (opts.Limit.HasValue) qs.Add($"limit={opts.Limit.Value}");
            var basePath = $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/event-leaderboards/{Uri.EscapeDataString(leaderboardId)}/join";
            var path = qs.Count == 0 ? basePath : $"{basePath}?{string.Join("&", qs)}";
            var env = await _client.RequestAsync<DataEnvelope<EventLeaderboard>>(
                HttpMethod.Post, path, body: new Dictionary<string, object?>(), cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new EventLeaderboard();
        }

        /// <summary>
        /// GET <c>/sdk/v1/event-leaderboards/:id/stream</c>: opens a
        /// Server-Sent Events subscription that pushes score updates
        /// in real time. Returns a <see cref="LeaderboardStream"/> handle
        /// the caller drives via its <c>OnEvent</c> / <c>OnError</c>
        /// callbacks + <see cref="LeaderboardStream.CancelAsync"/>.
        ///
        /// <para>
        /// Event kinds: <c>ready</c>, <c>score_update</c>, <c>finalized</c>,
        /// <c>closed</c>. Reconnects on its own after a server <c>close</c>, a
        /// drop, a busy server or 40 s of silence (see
        /// <see cref="StreamOptions"/>; <c>OnConnection</c> reports it), and
        /// stops for good after <c>closed</c>. Pass
        /// <c>new StreamOptions { AutoReconnect = false }</c> for the old
        /// single connection (handle <c>OnError</c> and re-call
        /// <see cref="LiveAsync"/> yourself).
        /// </para>
        ///
        /// <para>
        /// Low-level; prefer <see cref="Subscribe"/> for game UIs.
        /// </para>
        /// </summary>
        public Task<LeaderboardStream> LiveAsync(string leaderboardId, CancellationToken ct = default, StreamOptions? options = null)
            => OpenLiveAsync(leaderboardId, ct, options, null);

        internal Task<LeaderboardStream> OpenLiveAsync(
            string leaderboardId, CancellationToken ct, StreamOptions? options, Action<LeaderboardStream>? configure)
        {
            return LeaderboardStreamFactory.OpenAsync(
                _client.HttpForStreaming,
                _client.BaseUrlForStreaming,
                leaderboardId,
                _client.AuthHeaderForStreaming,
                _client.PlayerSecretForStreaming,
                ct,
                options,
                configure
            );
        }

        /// <summary>
        /// High-level live event-leaderboard subscription. Composes the
        /// <see cref="LiveAsync"/> SSE stream with a periodic background
        /// <see cref="ReadAsync"/> poll that nudges the server's lazy bot
        /// evaluator and dedupes the resulting deltas against the SSE
        /// feed. Idle UIs need the poll so bots tick on schedule even
        /// without player action; the SSE side carries the resulting
        /// score updates with low latency.
        ///
        /// <para>
        /// Callbacks fire on the HTTP background thread; marshal to
        /// Unity's main thread before touching <c>UnityEngine</c> APIs.
        /// Returns a handle whose <c>CancelAsync</c> / <c>Dispose</c>
        /// tear down both transports.
        /// </para>
        /// </summary>
        /// <param name="leaderboardId">The event leaderboard UUID to subscribe to.</param>
        /// <param name="onEvent">Fires for every event from SSE or poll. Deduped on score.</param>
        /// <param name="opts">Optional cadence + error callback.</param>
        public LiveLeaderboardSubscription Subscribe(
            string leaderboardId,
            Action<LeaderboardStreamEvent> onEvent,
            SubscribeOptions? opts = null
        )
        {
            return LiveLeaderboardSubscription.Open(
                this,
                leaderboardId,
                onEvent,
                opts ?? new SubscribeOptions()
            );
        }
    }

    /// <summary>
    /// Options for <see cref="EventLeaderboardsClient.Subscribe"/>.
    /// </summary>
    public sealed class SubscribeOptions
    {
        /// <summary>Background read cadence. Default 15000ms. Set to 0 to disable polling (SSE-only).</summary>
        public int PollIntervalMs { get; set; } = 15000;
        /// <summary>Optional; receives transport / parse errors. SSE errors are non-fatal; the poll keeps running.</summary>
        public Action<Exception>? OnError { get; set; }
        /// <summary>Optional; the SSE stream's reconnect status (<c>Connected</c> / <c>Reconnecting</c>).</summary>
        public Action<StreamConnectionEvent>? OnConnection { get; set; }
        /// <summary>Optional SSE stream options (auto-reconnect is on by default).</summary>
        public StreamOptions? Stream { get; set; }
    }

    /// <summary>
    /// Handle to a live event-leaderboard subscription opened via
    /// <see cref="EventLeaderboardsClient.Subscribe"/>. Tears down both
    /// the SSE stream and the background poll when cancelled / disposed.
    /// </summary>
    public sealed class LiveLeaderboardSubscription : IDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private readonly EventLeaderboardsClient _leaderboards;
        private readonly string _leaderboardId;
        private readonly Action<LeaderboardStreamEvent> _onEvent;
        private readonly Action<Exception> _onError;
        private readonly Action<StreamConnectionEvent>? _onConnection;
        private readonly StreamOptions? _streamOptions;
        private readonly int _pollIntervalMs;
        private readonly Dictionary<string, double> _lastSurfacedScore = new();
        private LeaderboardStream? _sseStream;
        private Task? _pollLoop;
        private Task? _sseLoop;
        private int _closed;

        private LiveLeaderboardSubscription(
            EventLeaderboardsClient leaderboards,
            string leaderboardId,
            Action<LeaderboardStreamEvent> onEvent,
            SubscribeOptions opts
        )
        {
            _leaderboards = leaderboards;
            _leaderboardId = leaderboardId;
            _onEvent = onEvent;
            _onError = opts.OnError ?? (_ => { });
            _onConnection = opts.OnConnection;
            _streamOptions = opts.Stream;
            _pollIntervalMs = opts.PollIntervalMs;
        }

        internal static LiveLeaderboardSubscription Open(
            EventLeaderboardsClient leaderboards,
            string leaderboardId,
            Action<LeaderboardStreamEvent> onEvent,
            SubscribeOptions opts
        )
        {
            var sub = new LiveLeaderboardSubscription(leaderboards, leaderboardId, onEvent, opts);
            sub._sseLoop = Task.Run(sub.RunSseLoopAsync);
            if (opts.PollIntervalMs > 0)
            {
                sub._pollLoop = Task.Run(sub.RunPollLoopAsync);
            }
            return sub;
        }

        private void Surface(LeaderboardStreamEvent ev)
        {
            if (Volatile.Read(ref _closed) == 1) return;
            // A live `finalized` event also updates the membership registry (not
            // just the callback) through the same single writer as catch-up;
            // see docs/05b. Fire-and-forget; the user still gets `ev` below.
            if (ev.Kind == "finalized")
            {
                _ = _leaderboards.Client.RouteFinalizedAsync(_leaderboardId, ev.Data);
            }
            // Dedup score_update by (participantId, score). Other event
            // kinds (ready, closed, parse-error) always pass through.
            if (ev.Kind == "score_update" && ev.Data != null)
            {
                if (ev.Data.TryGetValue("participantId", out var pidTok) &&
                    ev.Data.TryGetValue("score", out var scoreTok))
                {
                    var pid = (string?)pidTok;
                    if (pid != null && scoreTok.Type != Newtonsoft.Json.Linq.JTokenType.Null)
                    {
                        var score = (double)scoreTok;
                        lock (_lastSurfacedScore)
                        {
                            if (_lastSurfacedScore.TryGetValue(pid, out var prior) && prior == score)
                                return;
                            _lastSurfacedScore[pid] = score;
                        }
                    }
                }
            }
            try { _onEvent(ev); }
            catch (Exception cbErr) { try { _onError(cbErr); } catch { /* swallow */ } }
        }

        private async Task RunSseLoopAsync()
        {
            try
            {
                // Callbacks wired before the stream starts reading.
                _sseStream = await _leaderboards.OpenLiveAsync(_leaderboardId, _cts.Token, _streamOptions, s =>
                {
                    s.OnEvent = Surface;
                    s.OnError = err => {
                        if (Volatile.Read(ref _closed) == 0) { try { _onError(err); } catch { /* swallow */ } }
                    };
                    s.OnConnection = ev => {
                        if (Volatile.Read(ref _closed) == 0) _onConnection?.Invoke(ev);
                    };
                }).ConfigureAwait(false);
            }
            catch (Exception err)
            {
                if (Volatile.Read(ref _closed) == 0) { try { _onError(err); } catch { /* swallow */ } }
                return;
            }
        }

        private async Task RunPollLoopAsync()
        {
            // Fire one read immediately so the first frame of the UI
            // lands with current scores instead of waiting a full interval.
            await PollOnceAsync().ConfigureAwait(false);
            while (!_cts.IsCancellationRequested)
            {
                try { await Task.Delay(_pollIntervalMs, _cts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                await PollOnceAsync().ConfigureAwait(false);
            }
        }

        private async Task PollOnceAsync()
        {
            if (Volatile.Read(ref _closed) == 1) return;
            try
            {
                var lb = await _leaderboards.ReadAsync(_leaderboardId, null, _cts.Token).ConfigureAwait(false);
                foreach (var entry in lb.Entries)
                {
                    Surface(new LeaderboardStreamEvent("score_update", new Dictionary<string, Newtonsoft.Json.Linq.JToken>
                    {
                        ["leaderboardId"] = lb.LeaderboardId,
                        ["participantId"] = entry.ParticipantId,
                        ["score"] = entry.Score,
                        ["rank"] = entry.Rank,
                    }));
                }
            }
            catch (Exception err)
            {
                if (Volatile.Read(ref _closed) == 0) { try { _onError(err); } catch { /* swallow */ } }
            }
        }

        public async Task CancelAsync()
        {
            if (Interlocked.Exchange(ref _closed, 1) == 1) return;
            try { _cts.Cancel(); } catch { /* swallow */ }
            if (_sseStream != null)
            {
                try { await _sseStream.CancelAsync().ConfigureAwait(false); } catch { /* swallow */ }
            }
            try { if (_pollLoop != null) await _pollLoop.ConfigureAwait(false); } catch { /* swallow */ }
            try { if (_sseLoop != null) await _sseLoop.ConfigureAwait(false); } catch { /* swallow */ }
            try { _cts.Dispose(); } catch { /* swallow */ }
        }

        public void Dispose()
        {
            _ = CancelAsync();
        }
    }

    /// <summary>
    /// Resource client for <c>/sdk/v1/players/:p/{pending-grants,grants,crates}</c>.
    /// </summary>
    public sealed class GrantsClient
    {
        private readonly KratyClient _client;
        public GrantsClient(KratyClient client) => _client = client;

        /// <summary>
        /// GET <c>/sdk/v1/players/:p/pending-grants</c>: empty list
        /// for unknown players (not a 404).
        /// </summary>
        public async Task<List<Grant>> ListPendingAsync(
            int? limit = null,
            string? @as = null,
            CancellationToken ct = default
        )
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var path = limit.HasValue
                ? $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/pending-grants?limit={limit.Value}"
                : $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/pending-grants";
            var env = await _client.RequestAsync<DataEnvelope<List<Grant>>>(
                HttpMethod.Get, path, cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new List<Grant>();
        }

        /// <summary>
        /// POST <c>/sdk/v1/players/:p/grants</c>: grant items / currency / a
        /// crate to the ACTIVE player (self-only — the player secret means you
        /// can only grant to yourself). Requires the game's inventory
        /// management to be <c>permissive</c>, else the server returns 403
        /// <c>inventory_not_permissive</c>. The grant auto-deposits into the
        /// player's holdings.
        /// </summary>
        public async Task<Grant> GrantAsync(
            GrantSelfInput input,
            string? @as = null,
            CancellationToken ct = default)
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<Grant>>(
                HttpMethod.Post,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/grants",
                body: input,
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new Grant();
        }

        /// <summary>
        /// POST <c>/sdk/v1/players/:p/grants/:g/claim</c> for the
        /// active player. Idempotent; claiming an already-claimed
        /// grant returns the same row.
        /// </summary>
        public async Task<Grant> ClaimAsync(
            string grantId,
            string? @as = null,
            CancellationToken ct = default)
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<Grant>>(
                HttpMethod.Post,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/grants/{Uri.EscapeDataString(grantId)}/claim",
                body: new Dictionary<string, object?>(),
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new Grant();
        }

        /// <summary>
        /// POST <c>/sdk/v1/players/:p/crates/:g/open</c> for the
        /// active player. Idempotent on the crate id; replays
        /// return the previously-rolled contents grant.
        /// </summary>
        public async Task<OpenCrateResponse> OpenAsync(
            string grantId,
            string? @as = null,
            CancellationToken ct = default)
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<OpenCrateResponse>>(
                HttpMethod.Post,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/crates/{Uri.EscapeDataString(grantId)}/open",
                body: new Dictionary<string, object?>(),
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new OpenCrateResponse();
        }

        /// <summary>
        /// Burn down the pending-grants queue in one call: list
        /// everything pending, open every crate, claim every reward,
        /// return a summary. Built for the "round complete"
        /// reward-collection moment most games have.
        ///
        /// <para>
        /// Errors per-grant are caught and surfaced in
        /// <see cref="CollectAllResult.Failures"/>; one bad grant
        /// doesn't abort the whole sweep. The remaining grants still
        /// get processed. Callers can inspect failures + retry the
        /// individual ones later.
        /// </para>
        ///
        /// <para>
        /// Order is server-determined (whatever
        /// <see cref="ListPendingAsync"/> returns). Crates open BEFORE
        /// rewards are claimed; the rolled-contents grant a crate
        /// produces lands in the NEXT <see cref="ListPendingAsync"/>,
        /// so a recursive call would catch it; we deliberately don't
        /// recurse to keep the call bounded. Re-invoke after the first
        /// call if you want to drain crate-opened contents too.
        /// </para>
        /// </summary>
        public async Task<CollectAllResult> CollectAllAsync(
            string? @as = null,
            CancellationToken ct = default
        )
        {
            var pending = await ListPendingAsync(@as: @as, ct: ct).ConfigureAwait(false);
            var opened = new List<OpenCrateResponse>();
            var claimed = new List<Grant>();
            var failures = new List<CollectAllFailure>();
            foreach (var g in pending)
            {
                try
                {
                    if (g.Kind == "crate")
                    {
                        opened.Add(await OpenAsync(g.Id, @as: @as, ct: ct).ConfigureAwait(false));
                    }
                    else
                    {
                        claimed.Add(await ClaimAsync(g.Id, @as: @as, ct: ct).ConfigureAwait(false));
                    }
                }
                catch (Exception err)
                {
                    failures.Add(new CollectAllFailure(g, err));
                }
            }
            return new CollectAllResult(pending.Count, opened, claimed, failures);
        }
    }

    /// <summary>
    /// One pending grant that <see cref="GrantsClient.CollectAllAsync"/>
    /// couldn't process. The other grants in the same sweep still went
    /// through; inspect <see cref="Error"/> and retry the individual
    /// operation.
    /// </summary>
    public sealed class CollectAllFailure
    {
        public Grant Grant { get; }
        public Exception Error { get; }
        public CollectAllFailure(Grant grant, Exception error)
        {
            Grant = grant;
            Error = error;
        }
    }

    /// <summary>
    /// Aggregate result of <see cref="GrantsClient.CollectAllAsync"/>.
    /// <see cref="Processed"/> is the total pending count at the time
    /// of the call; <see cref="Opened"/> + <see cref="Claimed"/> +
    /// <see cref="Failures"/> sum to that.
    /// </summary>
    public sealed class CollectAllResult
    {
        public int Processed { get; }
        public IReadOnlyList<OpenCrateResponse> Opened { get; }
        public IReadOnlyList<Grant> Claimed { get; }
        public IReadOnlyList<CollectAllFailure> Failures { get; }

        public CollectAllResult(
            int processed,
            IReadOnlyList<OpenCrateResponse> opened,
            IReadOnlyList<Grant> claimed,
            IReadOnlyList<CollectAllFailure> failures
        )
        {
            Processed = processed;
            Opened = opened;
            Claimed = claimed;
            Failures = failures;
        }

        public bool HasFailures => Failures.Count > 0;
    }

    /// <summary>
    /// Resource client for <c>/sdk/v1/players/:p/inventory(/...)</c>.
    /// Only surfaces meaningful data for games whose
    /// <c>settings.inventoryManagement</c> is <c>'platform'</c>; under
    /// studio-managed mode the lists come back empty (the studio's own
    /// backend holds the canonical state). The SDK doesn't expose
    /// grant / admin-credit endpoints; those are server-API only.
    /// </summary>
    /// <summary>
    /// Real-money purchase reporting.
    ///
    /// <para>
    /// Deliberately WRITE-ONLY. There is no client-side read of a
    /// player's spend total: it is a monetization signal worth keeping
    /// out of a shipped binary, and games that gate content on spend
    /// don't need it — the backend evaluates a <c>spend_at_least</c>
    /// unlock condition server-side and the event simply arrives locked
    /// or unlocked.
    /// </para>
    /// </summary>
    public sealed class PurchasesClient
    {
        private readonly KratyClient _client;
        public PurchasesClient(KratyClient client) => _client = client;

        /// <summary>
        /// POST <c>/sdk/v1/players/:p/purchases</c>: record a real-money
        /// purchase for the active player.
        ///
        /// <para>
        /// Call it from your Unity IAP <c>ProcessPurchase</c> callback.
        /// Safe to call repeatedly for one
        /// <see cref="TrackPurchaseInput.TransactionId"/> — restores,
        /// retries after a timeout, and app relaunches all collapse onto
        /// a single ledger row, and
        /// <see cref="TrackPurchaseResult.Deduplicated"/> tells you which
        /// happened. That means you can call it unconditionally on every
        /// restore without tracking what you already reported.
        /// </para>
        ///
        /// <example>
        /// <code>
        /// var product = args.purchasedProduct;
        /// var result = await kraty.TrackPurchaseAsync(new TrackPurchaseInput
        /// {
        ///     // Google Play: the receipt's orderId, the id your server reports too.
        ///     TransactionId = StoreTransactions.FromUnityIap("google_play", product.transactionID, product.receipt),
        ///     Store         = "google_play",
        ///     ProductId     = args.purchasedProduct.definition.id,
        ///     ProductType   = "consumable",
        ///     AmountMinor   = 2990,   // R$29.90 — minor units, as charged
        ///     Currency      = "BRL",
        ///     Receipt       = args.purchasedProduct.receipt,
        ///     OfferInstanceId = offer?.InstanceId, // when bought from kraty.Offers.ListAsync()
        /// });
        /// // result.NormalizedAmountMinor == 520  ($5.20)
        /// // result.Offer?.StudioFulfils == true → deliver result.Offer.Contents yourself
        /// </code>
        /// </example>
        ///
        /// <para>
        /// Throws <see cref="KratyApiError"/> with code
        /// <c>unknown_currency</c> if Kraty holds no FX rate for the
        /// currency — a configuration gap rather than a player problem,
        /// so surface it in QA rather than retrying.
        /// </para>
        /// </summary>
        /// <param name="input">The purchase to record.</param>
        /// <param name="as">Address a different player (server-side tooling only).</param>
        /// <param name="ct">Cancellation token.</param>
        public async Task<TrackPurchaseResult> TrackAsync(
            TrackPurchaseInput input,
            string? @as = null,
            CancellationToken ct = default
        )
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<TrackPurchaseResult>>(
                HttpMethod.Post,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/purchases",
                body: input,
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new TrackPurchaseResult();
        }
    }

    public sealed class InventoryClient
    {
        private readonly KratyClient _client;
        public InventoryClient(KratyClient client) => _client = client;

        /// <summary>
        /// GET <c>/sdk/v1/players/:p/inventory</c>: every item the
        /// player currently holds (quantity > 0). Ordering is not
        /// guaranteed; sort client-side if you need it.
        /// </summary>
        public async Task<List<PlayerItemHolding>> ListAsync(
            string? @as = null,
            CancellationToken ct = default)
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<InventoryListEnvelope>>(
                HttpMethod.Get,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/inventory",
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data?.Items ?? new List<PlayerItemHolding>();
        }

        /// <summary>
        /// POST <c>/sdk/v1/players/:p/inventory/:itemKey/consume</c>:
        /// atomic decrement on the active player's inventory.
        /// Auto-stamped idempotency key unless you provide one.
        /// Throws <see cref="KratyApiError"/> with code <c>conflict</c>
        /// if the player doesn't have enough of the item.
        /// </summary>
        public async Task<ConsumeItemResult> ConsumeAsync(
            string itemKey,
            ConsumeItemInput input,
            string? @as = null,
            CancellationToken ct = default
        )
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<ConsumeItemResult>>(
                HttpMethod.Post,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/inventory/{Uri.EscapeDataString(itemKey)}/consume",
                body: input,
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new ConsumeItemResult();
        }

        /// <summary>
        /// GET <c>/sdk/v1/players/:p/inventory/stream</c>: live subscription
        /// to everything that touches this player's items, wallet, and
        /// grants — whoever caused it. A reward granted from the dashboard,
        /// a currency credited by the studio's backend, an event payout, and
        /// the client's own consume all arrive here.
        ///
        /// <para>
        /// Returns the raw handle; hook <c>OnEvent</c> / <c>OnError</c> and
        /// call <c>CancelAsync</c> when done. See <see cref="WatchAsync"/>
        /// for the callback form.
        /// </para>
        ///
        /// <para>
        /// Callbacks fire on the HTTP background thread; marshal to Unity's
        /// main thread before touching <c>UnityEngine</c> APIs. Reconnects on
        /// its own after a server <c>close</c>, a drop, a busy server or 40 s
        /// of silence (see <see cref="StreamOptions"/>; <c>OnConnection</c>
        /// reports it). Changes made while it was reconnecting are not
        /// replayed: re-read on <c>Connected</c>. Treat each event as
        /// "something changed, re-read this"; the REST reads stay
        /// authoritative.
        /// </para>
        /// </summary>
        /// <param name="as">Address a different player (server-side tooling only).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <param name="options">Stream options; <c>AutoReconnect = false</c> for a single connection.</param>
        public Task<InventoryStream> LiveAsync(
            string? @as = null,
            CancellationToken ct = default,
            StreamOptions? options = null)
            => OpenLiveAsync(@as, ct, options, null);

        private async Task<InventoryStream> OpenLiveAsync(
            string? @as, CancellationToken ct, StreamOptions? options, Action<InventoryStream>? configure)
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            return await InventoryStreamFactory.OpenAsync(
                _client.HttpForStreaming,
                _client.BaseUrlForStreaming,
                externalPlayerId,
                _client.AuthHeaderForStreaming,
                _client.PlayerSecretForStreaming,
                ct,
                options,
                configure
            ).ConfigureAwait(false);
        }

        /// <summary>
        /// Callback form of <see cref="LiveAsync"/>: the one-liner most
        /// games want.
        /// <code>
        /// var stream = await kraty.Inventory.WatchAsync(
        ///     ev => dispatcher.Enqueue(() => {
        ///         if (ev.Kind == "inventory_changed") RefreshBackpack();
        ///         if (ev.Kind == "grant_created") ShowRewardPopup(ev.GrantId);
        ///     }),
        ///     ignoreOwnWrites: true);
        /// // later
        /// await stream.CancelAsync();
        /// </code>
        /// </summary>
        /// <param name="handler">Invoked per event, on a background thread.</param>
        /// <param name="ignoreOwnWrites">
        /// Skip events this client caused (<c>origin: client</c>), so a
        /// consume doesn't bounce back as a refresh.
        /// </param>
        /// <param name="onError">
        /// Callback errors, and the error that ended the subscription for good
        /// (a refused reopen). Drops are retried for you; with
        /// <c>AutoReconnect = false</c>, transport failures too (the
        /// subscription is done by then).
        /// </param>
        /// <param name="as">Address a different player (server-side tooling only).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <param name="options">Stream options; <c>AutoReconnect = false</c> for a single connection.</param>
        /// <param name="onConnection">Reconnect status (<c>Connected</c> / <c>Reconnecting</c>); re-read on <c>Connected</c>.</param>
        public async Task<InventoryStream> WatchAsync(
            Action<InventoryStreamEvent> handler,
            bool ignoreOwnWrites = false,
            Action<Exception>? onError = null,
            string? @as = null,
            CancellationToken ct = default,
            StreamOptions? options = null,
            Action<StreamConnectionEvent>? onConnection = null)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            return await OpenLiveAsync(@as, ct, options, stream =>
            {
                stream.OnEvent = ev =>
                {
                    if (ignoreOwnWrites && ev.IsOwnWrite) return;
                    handler(ev);
                };
                if (onError != null) stream.OnError = onError;
                if (onConnection != null) stream.OnConnection = onConnection;
            }).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Resource client for <c>/sdk/v1/players/:p/wallet(/...)</c>.
    /// Mirrors <see cref="InventoryClient"/> for currencies +
    /// progression resources.
    /// </summary>
    public sealed class WalletClient
    {
        private readonly KratyClient _client;
        public WalletClient(KratyClient client) => _client = client;

        /// <summary>
        /// GET <c>/sdk/v1/players/:p/wallet</c>: every economy entry
        /// the player has touched. Returns zero-balance rows alongside
        /// positive ones, so a wallet that's been emptied still
        /// surfaces. Filter client-side if you only want live balances.
        /// </summary>
        public async Task<List<PlayerWalletHolding>> ListAsync(
            string? @as = null,
            CancellationToken ct = default)
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<WalletListEnvelope>>(
                HttpMethod.Get,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/wallet",
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data?.Wallet ?? new List<PlayerWalletHolding>();
        }

        /// <summary>
        /// POST <c>/sdk/v1/players/:p/wallet/:economyKey/debit</c>:
        /// atomic decrement on the active player's wallet. 409 on
        /// insufficient balance. Credit is intentionally not exposed
        /// here; only the studio's backend can mint balance.
        /// </summary>
        public async Task<DebitWalletResult> DebitAsync(
            string economyKey,
            DebitWalletInput input,
            string? @as = null,
            CancellationToken ct = default
        )
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<DebitWalletResult>>(
                HttpMethod.Post,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/wallet/{Uri.EscapeDataString(economyKey)}/debit",
                body: input,
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new DebitWalletResult();
        }

        /// <summary>
        /// POST <c>/sdk/v1/players/:p/wallet/:economyKey/progress</c>: push
        /// progress into a <b>progression</b> resource straight from the game
        /// client — XP from a finished run, distance travelled, stars
        /// collected — with no studio backend in the loop.
        /// <para>
        /// The resource must be <c>kind: progression</c> AND flagged
        /// <b>client-writable</b> in the dashboard; otherwise this fails 403.
        /// Spendable currencies are never client-writable.
        /// </para>
        /// <para>
        /// Anything DERIVED from the resource moves with it in the same
        /// transaction, and each level crossed pays its configured reward, so
        /// <see cref="ProgressWalletResult.Derived"/> can carry a level jump
        /// plus the grant ids to claim — enough to render "Level 5!" and a
        /// reward popup without a second request.
        /// </para>
        /// </summary>
        /// <param name="economyKey">The progression resource to advance.</param>
        /// <param name="input">Signed amount + optional reason / idempotency key.</param>
        /// <param name="as">Address a different player (server-side tooling only).</param>
        /// <param name="ct">Cancellation token.</param>
        public async Task<ProgressWalletResult> ProgressAsync(
            string economyKey,
            ProgressWalletInput input,
            string? @as = null,
            CancellationToken ct = default
        )
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<ProgressWalletResult>>(
                HttpMethod.Post,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/wallet/{Uri.EscapeDataString(economyKey)}/progress",
                body: input,
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new ProgressWalletResult();
        }
    }

    /// <summary>
    /// Resource client for <c>/sdk/v1/players/:p/register</c>: the
    /// zero-trust bootstrap. Most apps never call this directly:
    /// <see cref="KratyClient.EnsureIdentityAsync"/> handles register
    /// lazily on the first player-scoped call. Reach for
    /// <see cref="RegisterAsync"/> only when you want to register a
    /// specific id you control (e.g. tied to your own auth).
    /// </summary>
    public sealed class PlayersClient
    {
        private readonly KratyClient _client;
        public PlayersClient(KratyClient client) => _client = client;

        /// <summary>
        /// POST <c>/sdk/v1/players/:externalId/register</c>: creates
        /// the player row if it doesn't exist + mints a per-player
        /// secret. Throws <see cref="KratyApiError"/> with
        /// <c>IsPlayerAlreadyRegistered == true</c> if the player has
        /// already claimed a secret.
        ///
        /// <para>
        /// Pass <paramref name="force"/>=true to ROTATE an existing
        /// secret. Only honoured by non-<c>live</c> API keys (dev /
        /// test / staging).
        /// </para>
        /// </summary>
        public async Task<PlayerRegistration> RegisterAsync(
            string externalPlayerId,
            bool force = false,
            CancellationToken ct = default
        )
        {
            var path = force
                ? $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/register?force=true"
                : $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/register";
            var env = await _client.RequestAsync<DataEnvelope<PlayerRegistration>>(
                HttpMethod.Post,
                path,
                body: new Dictionary<string, object?>(),
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new PlayerRegistration();
        }

        /// <summary>
        /// PUT <c>/sdk/v1/players/:externalId/identity</c>: set the active
        /// player's OWN display name (+ optional <paramref name="avatar"/>) —
        /// e.g. from a "choose your username" screen. Overrides the identity
        /// auto-generated from the game's pool and surfaces on leaderboards.
        ///
        /// <para>
        /// Authorized by the player's own secret, so a client can only ever
        /// change ITS OWN identity. This trusts the client; moderate names from
        /// your backend (server SDK <c>players.setIdentity</c>) if you need to.
        /// Pass <paramref name="avatar"/>=null to clear it; <paramref name="as"/>
        /// targets another player (server-side tooling only). Throws
        /// <see cref="KratyApiError"/> <c>validation_failed</c> (400) on an
        /// empty or over-long name.
        /// </para>
        /// </summary>
        public async Task<PlayerIdentity?> SetIdentityAsync(
            string name,
            string? avatar = null,
            string? @as = null,
            CancellationToken ct = default
        )
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var body = new Dictionary<string, object?> { ["name"] = name };
            if (avatar != null) body["avatar"] = avatar;
            var env = await _client.RequestAsync<DataEnvelope<SetIdentityResult>>(
                HttpMethod.Put,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/identity",
                body: body,
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data?.DisplayIdentity;
        }

        /// <summary>
        /// GET the player's REAL display identity — what
        /// <see cref="SetIdentityAsync"/> wrote, falling back to the
        /// anonymized pool value when the player never renamed themselves.
        /// Enriched with the server-resolved country so a UI can render a
        /// flag next to the name in one round-trip. Use for self-facing
        /// surfaces where the real handle should show if picked.
        /// </summary>
        public async Task<PlayerIdentity?> GetIdentityAsync(
            string? @as = null,
            CancellationToken ct = default
        )
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<SetIdentityResult>>(
                HttpMethod.Get,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/identity",
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data?.DisplayIdentity;
        }

        /// <summary>
        /// GET the player's ANONYMIZED identity — always the immutable
        /// synthetic-pool value plus country, regardless of any
        /// <see cref="SetIdentityAsync"/> overrides. Use for public
        /// leaderboards or any surface where the real name shouldn't
        /// leak. Pair with <see cref="GetIdentityAsync"/> for a
        /// "real if set, anonymized otherwise" flow.
        /// </summary>
        public async Task<PlayerIdentity?> GetAnonymizedIdentityAsync(
            string? @as = null,
            CancellationToken ct = default
        )
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<SetIdentityResult>>(
                HttpMethod.Get,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/identity",
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data?.AnonymizedIdentity;
        }

        /// <summary>
        /// PUT the calling player's free-form <c>metadata</c> bag wholesale.
        /// Studio-defined keys — anything the platform's own model doesn't
        /// cover (VIP flag, gender, preferred language, cohort). Surfaced
        /// on every leaderboard entry so game code can render metadata-aware
        /// UI without a second lookup. Use <see cref="MergeMetadataAsync"/>
        /// if you only want to update a subset of keys.
        /// </summary>
        public async Task<Dictionary<string, object?>?> SetMetadataAsync(
            Dictionary<string, object?> metadata,
            string? @as = null,
            CancellationToken ct = default
        )
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<PlayerMetadataResult>>(
                HttpMethod.Put,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/metadata",
                body: metadata,
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data?.Metadata;
        }

        /// <summary>
        /// PATCH the calling player's metadata: shallow-merge the given
        /// keys into the existing bag. Keys not in <paramref name="patch"/>
        /// stay untouched.
        /// </summary>
        public async Task<Dictionary<string, object?>?> MergeMetadataAsync(
            Dictionary<string, object?> patch,
            string? @as = null,
            CancellationToken ct = default
        )
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<PlayerMetadataResult>>(
                HttpMethod.Patch,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/metadata",
                body: patch,
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data?.Metadata;
        }
    }

    /// <summary>
    /// Resource client for the player social graph, under
    /// <c>/sdk/v1/players/:externalId/...</c>: friend codes, requests, the
    /// friends list with live presence, username search, and blocking.
    ///
    /// <para>
    /// Every method is authorized by the active player's own secret, so a
    /// client can only ever act on ITS OWN social graph — friends are always
    /// scoped to the same game + environment. Pass <c>as</c> on any method
    /// to address a different player (server-side tooling only).
    /// </para>
    /// </summary>
    public sealed class FriendsClient
    {
        private readonly KratyClient _client;
        public FriendsClient(KratyClient client) => _client = client;

        /// <summary>
        /// GET <c>/sdk/v1/players/:p/friend-code</c>: the caller's short,
        /// shareable friend code (unambiguous alphabet), generated on first
        /// use and stable forever after. Share it out-of-band so a friend can
        /// <see cref="AddAsync"/> you without a username search.
        /// </summary>
        /// <param name="progression">
        /// Economy keys whose balances to attach (e.g. <c>["level"]</c>). Here
        /// that means the caller's OWN balances, so a profile card can render
        /// the share code and the level from one request.
        /// </param>
        /// <param name="as">Address a different player (server-side tooling only).</param>
        /// <param name="ct">Cancellation token.</param>
        public async Task<FriendCode> GetCodeAsync(
            string? @as = null,
            IEnumerable<string>? progression = null,
            CancellationToken ct = default)
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<FriendCode>>(
                HttpMethod.Get,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/friend-code{ProgressionQuery(progression)}",
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new FriendCode();
        }

        /// <summary>
        /// Builds the <c>?progression=level,trophies</c> query for the
        /// social-graph reads. Opt-in and explicit, because "the level" is a
        /// different economy key in every game; nothing extra is read
        /// server-side when omitted. Returns an empty string for no keys.
        /// </summary>
        private static string ProgressionQuery(IEnumerable<string>? keys)
        {
            if (keys == null) return string.Empty;
            var joined = string.Join(",", keys);
            if (joined.Length == 0) return string.Empty;
            return $"?progression={Uri.EscapeDataString(joined)}";
        }

        /// <summary>
        /// POST <c>/sdk/v1/players/:p/presence</c>: refresh the caller's
        /// online status. Call every ~30s while the player is active;
        /// presence expires automatically when heartbeats stop. Optionally
        /// set a free-form <paramref name="status"/> label ("in_match",
        /// "lobby", …) that friends see; leave it null to just refresh
        /// presence without touching the label.
        /// </summary>
        public async Task<PlayerPresence> HeartbeatAsync(
            string? status = null,
            string? @as = null,
            CancellationToken ct = default)
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var body = new Dictionary<string, object?>();
            if (status != null) body["status"] = status;
            var env = await _client.RequestAsync<DataEnvelope<PlayerPresence>>(
                HttpMethod.Post,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/presence",
                body: body,
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new PlayerPresence();
        }

        /// <summary>
        /// GET <c>/sdk/v1/players/:p/friends</c>: the caller's accepted
        /// friends, each enriched with display identity and live presence
        /// (online / last-active / status).
        /// </summary>
        /// <param name="progression">
        /// Economy keys whose balances to attach to each friend (e.g.
        /// <c>["level"]</c>), so a friends list renders "Lv 12 ShadowStrike"
        /// without a request per friend. A resource a friend never earned
        /// reads <c>0</c>.
        /// </param>
        /// <param name="as">Address a different player (server-side tooling only).</param>
        /// <param name="ct">Cancellation token.</param>
        public async Task<List<Friend>> ListAsync(
            string? @as = null,
            IEnumerable<string>? progression = null,
            CancellationToken ct = default)
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<FriendsListEnvelope>>(
                HttpMethod.Get,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/friends{ProgressionQuery(progression)}",
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data?.Friends ?? new List<Friend>();
        }

        /// <summary>
        /// GET <c>/sdk/v1/players/:p/friends/search?q=</c>: find other
        /// players by display name within the same game + environment. Each
        /// hit reports the caller's relationship to that player; blocked
        /// players are omitted.
        /// </summary>
        public async Task<List<FriendSearchResult>> SearchAsync(
            string query,
            int? limit = null,
            string? @as = null,
            IEnumerable<string>? progression = null,
            CancellationToken ct = default)
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var qs = new List<string> { $"q={Uri.EscapeDataString(query ?? string.Empty)}" };
            if (limit.HasValue) qs.Add($"limit={limit.Value}");
            var progressionKeys = progression == null ? string.Empty : string.Join(",", progression);
            if (progressionKeys.Length > 0) qs.Add($"progression={Uri.EscapeDataString(progressionKeys)}");
            var path = $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/friends/search?{string.Join("&", qs)}";
            var env = await _client.RequestAsync<DataEnvelope<FriendSearchEnvelope>>(
                HttpMethod.Get, path, cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data?.Results ?? new List<FriendSearchResult>();
        }

        /// <summary>
        /// GET <c>/sdk/v1/players/:p/friends/suggestions</c>: "people you may
        /// want to add" — recently-active players in the same game +
        /// environment the caller isn't already related to (not a friend, no
        /// pending request either way, not blocked). Results are a
        /// weighted-random sample biased toward the most recently active, so
        /// calling it again surfaces a different set.
        /// </summary>
        /// <param name="limit">How many to return; defaults to 5 server-side (clamped 1–25).</param>
        /// <param name="as">Address a different player (server-side tooling only).</param>
        /// <param name="progression">Economy keys whose balances to attach to each suggestion.</param>
        /// <param name="ct">Cancellation token.</param>
        public async Task<List<Suggestion>> SuggestionsAsync(
            int? limit = null,
            string? @as = null,
            IEnumerable<string>? progression = null,
            CancellationToken ct = default)
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var qs = new List<string>();
            if (limit.HasValue) qs.Add($"limit={limit.Value}");
            var progressionKeys = progression == null ? string.Empty : string.Join(",", progression);
            if (progressionKeys.Length > 0) qs.Add($"progression={Uri.EscapeDataString(progressionKeys)}");
            var suffix = qs.Count == 0 ? string.Empty : $"?{string.Join("&", qs)}";
            var path = $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/friends/suggestions{suffix}";
            var env = await _client.RequestAsync<DataEnvelope<FriendSuggestionsEnvelope>>(
                HttpMethod.Get, path, cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data?.Suggestions ?? new List<Suggestion>();
        }

        /// <summary>
        /// GET <c>/sdk/v1/players/:p/friends/requests</c>: the caller's
        /// pending incoming + outgoing friend requests.
        /// </summary>
        /// <param name="progression">
        /// Economy keys whose balances to attach to each player.
        /// </param>
        /// <param name="as">Address a different player (server-side tooling only).</param>
        /// <param name="ct">Cancellation token.</param>
        public async Task<FriendRequests> ListRequestsAsync(
            string? @as = null,
            IEnumerable<string>? progression = null,
            CancellationToken ct = default)
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<FriendRequests>>(
                HttpMethod.Get,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/friends/requests{ProgressionQuery(progression)}",
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new FriendRequests();
        }

        /// <summary>
        /// POST <c>/sdk/v1/players/:p/friends/requests</c>: add another
        /// player by friend code or external id (set exactly one on
        /// <paramref name="target"/>). Creates a pending request they must
        /// accept — unless they had already requested the caller, in which
        /// case the friendship is accepted immediately
        /// (<c>Status == "accepted"</c>). Throws <see cref="KratyApiError"/>
        /// on <c>player_blocked</c> (403), <c>already_friends</c> (409),
        /// <c>cannot_friend_self</c> (400), or <c>friend_code_invalid</c>
        /// (404).
        /// </summary>
        public async Task<SendFriendRequestResult> AddAsync(
            FriendTarget target,
            string? @as = null,
            CancellationToken ct = default)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<SendFriendRequestResult>>(
                HttpMethod.Post,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/friends/requests",
                body: target,
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new SendFriendRequestResult();
        }

        /// <summary>
        /// POST <c>/sdk/v1/players/:p/friends/requests/:id/accept</c>: accept
        /// an incoming request, returning the newly-confirmed friend.
        /// </summary>
        public async Task<Friend> AcceptAsync(
            string requestId,
            string? @as = null,
            CancellationToken ct = default)
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<FriendEnvelope>>(
                HttpMethod.Post,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/friends/requests/{Uri.EscapeDataString(requestId)}/accept",
                body: new Dictionary<string, object?>(),
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data?.Friend ?? new Friend();
        }

        /// <summary>
        /// POST <c>/sdk/v1/players/:p/friends/requests/:id/decline</c>:
        /// decline an incoming request.
        /// </summary>
        public async Task DeclineAsync(
            string requestId,
            string? @as = null,
            CancellationToken ct = default)
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            await _client.RequestAsync<DataEnvelope<Newtonsoft.Json.Linq.JObject>>(
                HttpMethod.Post,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/friends/requests/{Uri.EscapeDataString(requestId)}/decline",
                body: new Dictionary<string, object?>(),
                cancellationToken: ct
            ).ConfigureAwait(false);
        }

        /// <summary>
        /// DELETE <c>/sdk/v1/players/:p/friends/requests/:id</c>: cancel an
        /// outgoing request.
        /// </summary>
        public async Task CancelRequestAsync(
            string requestId,
            string? @as = null,
            CancellationToken ct = default)
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            await _client.RequestAsync<DataEnvelope<Newtonsoft.Json.Linq.JObject>>(
                HttpMethod.Delete,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/friends/requests/{Uri.EscapeDataString(requestId)}",
                cancellationToken: ct
            ).ConfigureAwait(false);
        }

        /// <summary>
        /// DELETE <c>/sdk/v1/players/:p/friends/:friendExternalId</c>: remove
        /// an existing friend.
        /// </summary>
        public async Task RemoveAsync(
            string friendExternalId,
            string? @as = null,
            CancellationToken ct = default)
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            await _client.RequestAsync<DataEnvelope<Newtonsoft.Json.Linq.JObject>>(
                HttpMethod.Delete,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/friends/{Uri.EscapeDataString(friendExternalId)}",
                cancellationToken: ct
            ).ConfigureAwait(false);
        }

        /// <summary>
        /// GET <c>/sdk/v1/players/:p/blocks</c>: the players the caller has
        /// blocked.
        /// </summary>
        public async Task<List<BlockedPlayer>> ListBlocksAsync(
            string? @as = null,
            CancellationToken ct = default)
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<BlockedListEnvelope>>(
                HttpMethod.Get,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/blocks",
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data?.Blocked ?? new List<BlockedPlayer>();
        }

        /// <summary>
        /// POST <c>/sdk/v1/players/:p/blocks</c>: block a player by friend
        /// code or external id (set exactly one on <paramref name="target"/>).
        /// Tears down any friendship / pending request between the two and
        /// hides each from the other's search + presence.
        /// </summary>
        public async Task<BlockedPlayer> BlockAsync(
            FriendTarget target,
            string? @as = null,
            CancellationToken ct = default)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<BlockEnvelope>>(
                HttpMethod.Post,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/blocks",
                body: target,
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data?.Blocked ?? new BlockedPlayer();
        }

        /// <summary>
        /// DELETE <c>/sdk/v1/players/:p/blocks/:blockedExternalId</c>:
        /// unblock a player.
        /// </summary>
        public async Task UnblockAsync(
            string blockedExternalId,
            string? @as = null,
            CancellationToken ct = default)
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            await _client.RequestAsync<DataEnvelope<Newtonsoft.Json.Linq.JObject>>(
                HttpMethod.Delete,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/blocks/{Uri.EscapeDataString(blockedExternalId)}",
                cancellationToken: ct
            ).ConfigureAwait(false);
        }

        // ── Gifting ──────────────────────────────────────────────────────

        private static string GiftQuery(bool pendingOnly, int? limit)
        {
            var parts = new List<string>();
            if (pendingOnly) parts.Add("status=pending");
            if (limit.HasValue) parts.Add($"limit={limit.Value}");
            return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
        }

        /// <summary>
        /// GET <c>/sdk/v1/players/:p/friends/gifts/catalog</c>: what this
        /// player may gift and how often.
        ///
        /// Returns the studio's allowlist — every catalog row flagged giftable
        /// in the dashboard — plus the game's cooldown / daily-limit config, so
        /// a gift picker can be driven entirely from server config.
        /// <c>type: "currency"</c> entries include progression items like XP.
        /// Throws when the game hasn't enabled gifting (<c>403
        /// gifting_disabled</c>).
        /// </summary>
        public async Task<GiftCatalog> GiftCatalogAsync(
            string? @as = null,
            CancellationToken ct = default)
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<GiftCatalog>>(
                HttpMethod.Get,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/friends/gifts/catalog",
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new GiftCatalog();
        }

        /// <summary>
        /// POST <c>/sdk/v1/players/:p/friends/gifts</c>: send a gift to an
        /// accepted friend.
        ///
        /// The catalog decides what may travel and how much of it; when the
        /// resource is configured to cost the sender, their balance is debited
        /// in the same transaction. The recipient receives a PENDING grant that
        /// stays unopened until they call <see cref="ClaimGiftAsync"/>. Set
        /// <c>IdempotencyKey</c> so a retry returns the original gift instead
        /// of failing the per-friend cooldown.
        /// </summary>
        public async Task<Gift> SendGiftAsync(
            SendGiftInput input,
            string? @as = null,
            CancellationToken ct = default)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<GiftEnvelope>>(
                HttpMethod.Post,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/friends/gifts",
                body: input,
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data?.Gift ?? new Gift();
        }

        /// <summary>
        /// GET <c>/sdk/v1/players/:p/friends/gifts</c>: the player's gift
        /// inbox, newest first. Pass <paramref name="pendingOnly"/> to narrow
        /// it to gifts still waiting to be claimed.
        /// </summary>
        public async Task<List<Gift>> GiftsAsync(
            bool pendingOnly = false,
            int? limit = null,
            string? @as = null,
            CancellationToken ct = default)
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<GiftListEnvelope>>(
                HttpMethod.Get,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/friends/gifts{GiftQuery(pendingOnly, limit)}",
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data?.Gifts ?? new List<Gift>();
        }

        /// <summary>
        /// GET <c>/sdk/v1/players/:p/friends/gifts/sent</c>: gifts this player
        /// has sent, newest first.
        /// </summary>
        public async Task<List<Gift>> SentGiftsAsync(
            bool pendingOnly = false,
            int? limit = null,
            string? @as = null,
            CancellationToken ct = default)
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<GiftListEnvelope>>(
                HttpMethod.Get,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/friends/gifts/sent{GiftQuery(pendingOnly, limit)}",
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data?.Gifts ?? new List<Gift>();
        }

        /// <summary>
        /// POST <c>/sdk/v1/players/:p/friends/gifts/:giftId/claim</c>: claim a
        /// received gift.
        ///
        /// This is where a gift pays out: the delivering grant defers its
        /// deposit to claim time, so an unopened gift is genuinely unopened.
        /// Replays on an already-claimed gift return the same row.
        /// </summary>
        public async Task<Gift> ClaimGiftAsync(
            string giftId,
            string? @as = null,
            CancellationToken ct = default)
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<GiftEnvelope>>(
                HttpMethod.Post,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/friends/gifts/{Uri.EscapeDataString(giftId)}/claim",
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data?.Gift ?? new Gift();
        }
    }

    /// <summary>
    /// Resource client for <c>/sdk/v1/catalog</c>: single-shot read
    /// of every item + currency configured for the calling game.
    /// Studios call this once at boot and cache locally; pairs with
    /// <see cref="EventsClient.ListForPlayerAsync"/> (which inlines
    /// reward-bundle previews) so a UI can render names, icons,
    /// rarities, and reward previews without keeping a parallel
    /// catalog in the client codebase.
    /// </summary>
    public sealed class CatalogClient
    {
        private readonly KratyClient _client;
        public CatalogClient(KratyClient client) => _client = client;

        /// <summary>
        /// GET <c>/sdk/v1/catalog</c>: items + currencies for the
        /// calling game. Game is derived from the API key.
        /// </summary>
        public async Task<Catalog> ReadAsync(CancellationToken ct = default)
        {
            var env = await _client.RequestAsync<DataEnvelope<Catalog>>(
                HttpMethod.Get,
                "/sdk/v1/catalog",
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new Catalog();
        }
    }

    /// <summary>
    /// Resource client for <c>/sdk/v1/players/:p/daily-login</c>: the
    /// game's daily login calendar. Read-only; the server pays each day's
    /// reward on the player's first session of that UTC day (a call on
    /// first contact or after 30 minutes of silence opens one), as a grant
    /// with <c>SourceKind == "daily_login"</c>.
    /// </summary>
    public sealed class DailyLoginClient
    {
        private readonly KratyClient _client;
        public DailyLoginClient(KratyClient client) => _client = client;

        /// <summary>
        /// GET <c>/sdk/v1/players/:p/daily-login</c>. Returns <c>null</c>
        /// when the game has no calendar or it is switched off for this
        /// key's environment. When this request is the one that opens a
        /// session, today is already paid in the result; a player still in a
        /// session from before midnight UTC is paid on their next one.
        /// </summary>
        public async Task<DailyLogin?> ReadAsync(
            string? @as = null,
            CancellationToken ct = default)
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<DailyLogin>>(
                HttpMethod.Get,
                $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/daily-login",
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data;
        }
    }

    /// <summary>
    /// Resource client for <c>/sdk/v1/players/:p/offers</c> (docs/26):
    /// real-money offers such as starter packs, bundles and chains.
    ///
    /// <para>
    /// The flow: <see cref="ListAsync"/> what to show;
    /// <see cref="MarkShownAsync"/> when an offer is actually on screen (that
    /// starts a "from first shown" timer, so don't call it for offers you only
    /// prefetched); sell <c>offer.Current.Skus[yourStore]</c>; then report the
    /// store transaction with <c>kraty.TrackPurchaseAsync(new TrackPurchaseInput
    /// { …, OfferInstanceId = offer.InstanceId })</c>. When the result's
    /// <c>Offer.StudioFulfils</c> is true (the default), deliver
    /// <c>Offer.Contents</c> yourself, as you fulfil any IAP.
    /// </para>
    /// </summary>
    public sealed class OffersClient
    {
        private readonly KratyClient _client;
        public OffersClient(KratyClient client) => _client = client;

        private async Task<string> BaseAsync(string? @as, CancellationToken ct)
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            return $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/offers";
        }

        /// <summary>
        /// GET <c>/sdk/v1/players/:p/offers</c>: the offers to show now, highest
        /// priority first (<paramref name="limit"/> ≤ 20, default 10).
        /// <paramref name="level"/> is the player's current level, for offers
        /// that trigger on reaching one. Listing is not an impression.
        /// </summary>
        public async Task<List<Offer>> ListAsync(
            int? limit = null,
            int? level = null,
            string? @as = null,
            CancellationToken ct = default)
        {
            var b = await BaseAsync(@as, ct).ConfigureAwait(false);
            var parts = new List<string>();
            if (limit.HasValue) parts.Add($"limit={limit.Value}");
            if (level.HasValue) parts.Add($"level={level.Value}");
            var qs = parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
            var env = await _client.RequestAsync<DataEnvelope<List<Offer>>>(
                HttpMethod.Get, b + qs, cancellationToken: ct).ConfigureAwait(false);
            return env.Data ?? new List<Offer>();
        }

        /// <summary>
        /// POST <c>…/offers/:instanceId/impression</c>: the offer is on screen
        /// now. Idempotent (the SDK generates the key when you don't pass one).
        /// <c>Capped</c> is true when the offer's impression cap refused it.
        /// </summary>
        public async Task<OfferImpressionResult> MarkShownAsync(
            string instanceId,
            string? idempotencyKey = null,
            string? @as = null,
            CancellationToken ct = default)
        {
            var b = await BaseAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<OfferImpressionResult>>(
                HttpMethod.Post,
                $"{b}/{Uri.EscapeDataString(instanceId)}/impression",
                body: KeyBody(idempotencyKey),
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new OfferImpressionResult();
        }

        /// <summary>
        /// POST <c>…/offers/:instanceId/dismiss</c>: the player closed the offer
        /// without buying. Answers the offer after the dismiss.
        /// </summary>
        public async Task<Offer> DismissAsync(
            string instanceId,
            string? idempotencyKey = null,
            string? @as = null,
            CancellationToken ct = default)
        {
            var b = await BaseAsync(@as, ct).ConfigureAwait(false);
            var env = await _client.RequestAsync<DataEnvelope<Offer>>(
                HttpMethod.Post,
                $"{b}/{Uri.EscapeDataString(instanceId)}/dismiss",
                body: KeyBody(idempotencyKey),
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new Offer();
        }

        /// <summary>
        /// POST <c>…/offers/triggers</c>: report a fail, a depleted resource or a
        /// level (<see cref="OfferTriggerInput.FailEvent"/>,
        /// <see cref="OfferTriggerInput.ResourceDepleted"/>,
        /// <see cref="OfferTriggerInput.LevelReached"/>) so the offers listening
        /// to it arm. Answers the newly armed offers that can be shown now
        /// (empty when none did; they also show up on the next
        /// <see cref="ListAsync"/>). <paramref name="playerContext"/> feeds offer
        /// eligibility. Idempotent (the SDK generates the key when you don't
        /// pass one). Win-back offers need no call: they arm when a session
        /// opens after a break.
        /// </summary>
        public async Task<OfferTriggerResult> TriggerAsync(
            OfferTriggerInput input,
            Dictionary<string, object?>? playerContext = null,
            string? idempotencyKey = null,
            string? @as = null,
            CancellationToken ct = default)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var b = await BaseAsync(@as, ct).ConfigureAwait(false);
            var body = KeyBody(idempotencyKey);
            body["trigger"] = input;
            if (playerContext != null) body["playerContext"] = playerContext;
            var env = await _client.RequestAsync<DataEnvelope<OfferTriggerResult>>(
                HttpMethod.Post, $"{b}/triggers", body: body, cancellationToken: ct).ConfigureAwait(false);
            return env.Data ?? new OfferTriggerResult();
        }

        /// <summary>
        /// POST <c>…/offers/:instanceId/claim</c>: claim the current free tier
        /// of a chain offer (<c>offer.Current.IsFree</c>). Pass the
        /// <paramref name="tier"/> the player tapped (<c>offer.Tier</c>). No
        /// store, never revenue. Answers the claim and the offer after it (the
        /// next tier, or <c>completed</c>). When <c>Claim.StudioFulfils</c> is
        /// true (the default), deliver <c>Claim.Contents</c> yourself.
        /// Deliver each claim from one path only (this response or the
        /// <c>offer.tier_claimed</c> webhook), and de-duplicate on
        /// <c>Claim.ClaimId</c>.
        /// Idempotent (the SDK generates the key when you don't pass one). A
        /// refusal throws <see cref="KratyApiError"/> with
        /// <c>Code == "conflict"</c>; read its reason (<c>tier_changed</c>,
        /// <c>tier_not_free</c>, <c>offer_closed</c>) and the offer now with
        /// <see cref="TryReadClaimRefusal"/>.
        /// </summary>
        public async Task<OfferClaimResult> ClaimAsync(
            string instanceId,
            int tier,
            string? idempotencyKey = null,
            string? @as = null,
            CancellationToken ct = default)
        {
            var b = await BaseAsync(@as, ct).ConfigureAwait(false);
            var body = KeyBody(idempotencyKey);
            body["tier"] = tier;
            var env = await _client.RequestAsync<DataEnvelope<OfferClaimResult>>(
                HttpMethod.Post,
                $"{b}/{Uri.EscapeDataString(instanceId)}/claim",
                body: body,
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new OfferClaimResult();
        }

        /// <summary>
        /// Reads a refused <see cref="ClaimAsync"/> (409 <c>conflict</c>):
        /// <paramref name="reason"/> is <c>tier_changed</c>,
        /// <c>tier_not_free</c> or <c>offer_closed</c>, and
        /// <paramref name="offer"/> the offer as it is now (re-render it).
        /// False for any other error.
        /// </summary>
        public static bool TryReadClaimRefusal(KratyApiError error, out string reason, out Offer? offer)
        {
            reason = string.Empty;
            offer = null;
            if (error == null || error.Code != "conflict" || error.Details == null) return false;
            if (!error.Details.TryGetValue("raw", out var raw) || raw is not string json) return false;
            try
            {
                var details = Newtonsoft.Json.Linq.JObject.Parse(json);
                var r = (string?)details["reason"];
                if (string.IsNullOrEmpty(r)) return false;
                reason = r!;
                offer = details["offer"]?.Type == Newtonsoft.Json.Linq.JTokenType.Object
                    ? details["offer"]!.ToObject<Offer>()
                    : null;
                return true;
            }
            catch (Newtonsoft.Json.JsonException)
            {
                return false;
            }
        }

        // The client stamps a generated key into a dictionary body that has none.
        private static Dictionary<string, object?> KeyBody(string? idempotencyKey)
        {
            var body = new Dictionary<string, object?>();
            if (!string.IsNullOrEmpty(idempotencyKey)) body["idempotencyKey"] = idempotencyKey;
            return body;
        }
    }

    /// <summary>
    /// The <c>transactionId</c> to report for a store transaction (docs/22,
    /// docs/26 § 5.0). Client and server must report the SAME id for one
    /// transaction, or the purchase is recorded twice:
    /// <list type="bullet">
    /// <item>App Store: the StoreKit 2 <c>transactionId</c>, never <c>originalTransactionId</c>;</item>
    /// <item>Google Play: the <c>orderId</c> (<c>GPA.…</c>), falling back to the
    /// <c>purchaseToken</c> only for test purchases that have no <c>orderId</c>;</item>
    /// <item>other stores: their transaction id, unchanged.</item>
    /// </list>
    /// </summary>
    public static class StoreTransactions
    {
        /// <summary>
        /// Pick the canonical id from the store's fields. Throws
        /// <see cref="ArgumentException"/> when the store's id is missing.
        /// </summary>
        public static string CanonicalId(
            string store,
            string? transactionId = null,
            string? orderId = null,
            string? purchaseToken = null)
        {
            string? id = store == "google_play"
                ? (!string.IsNullOrEmpty(orderId) ? orderId : (!string.IsNullOrEmpty(purchaseToken) ? purchaseToken : null))
                : (!string.IsNullOrEmpty(transactionId) ? transactionId : null);
            if (id == null)
            {
                throw new ArgumentException(store == "google_play"
                    ? "CanonicalId: google_play needs orderId (or purchaseToken for test purchases)"
                    : $"CanonicalId: {store} needs transactionId");
            }
            return id;
        }

        /// <summary>
        /// The canonical id for a Unity IAP <c>Product</c>: pass its
        /// <c>transactionID</c> and <c>receipt</c>. On Google Play Unity's
        /// <c>transactionID</c> is the purchase token, so this reads the
        /// <c>orderId</c> from the receipt JSON (<c>Payload</c> → <c>json</c>)
        /// and falls back to the token when there is none. Other stores
        /// return <paramref name="transactionId"/> unchanged.
        /// </summary>
        public static string FromUnityIap(string store, string transactionId, string? receipt)
        {
            if (store != "google_play") return CanonicalId(store, transactionId: transactionId);
            return CanonicalId(store, orderId: GoogleOrderId(receipt), purchaseToken: transactionId);
        }

        private static string? GoogleOrderId(string? receipt)
        {
            if (string.IsNullOrEmpty(receipt)) return null;
            try
            {
                // Unity wraps Google's purchase JSON twice: { Payload: "{ json: \"{ orderId }\" }" }.
                var outer = Newtonsoft.Json.Linq.JObject.Parse(receipt!);
                var payload = outer["Payload"]?.Type == Newtonsoft.Json.Linq.JTokenType.String
                    ? Newtonsoft.Json.Linq.JObject.Parse((string)outer["Payload"]!)
                    : outer["Payload"] as Newtonsoft.Json.Linq.JObject;
                var json = payload?["json"];
                var order = json?.Type == Newtonsoft.Json.Linq.JTokenType.String
                    ? Newtonsoft.Json.Linq.JObject.Parse((string)json!)
                    : json as Newtonsoft.Json.Linq.JObject;
                var orderId = order?["orderId"]?.Type == Newtonsoft.Json.Linq.JTokenType.String
                    ? (string?)order!["orderId"]
                    : null;
                return string.IsNullOrEmpty(orderId) ? null : orderId;
            }
            catch (Newtonsoft.Json.JsonException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Resource client for guilds (docs/25): <c>/sdk/v1/players/:p/guild*</c>
    /// and <c>/guilds*</c>. One guild per player, up to 50 members, leader /
    /// co-leader / member roles. Every call acts as the active player (or
    /// <c>@as</c>). Build screens from <see cref="ConfigAsync"/>: it carries
    /// the unlock gate, cost, role permissions, help and score settings.
    /// </summary>
    public sealed class GuildsClient
    {
        private readonly KratyClient _client;
        public GuildsClient(KratyClient client) => _client = client;

        private async Task<string> BaseAsync(string? @as, CancellationToken ct)
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            return $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}";
        }

        private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
        {
            var env = await _client.RequestAsync<DataEnvelope<T>>(method, path, body: body, cancellationToken: ct)
                .ConfigureAwait(false);
            return env.Data;
        }

        private static string Query(params (string Key, string? Value)[] pairs)
        {
            var parts = new List<string>();
            foreach (var (key, value) in pairs)
            {
                if (string.IsNullOrEmpty(value)) continue;
                parts.Add($"{key}={Uri.EscapeDataString(value)}");
            }
            return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
        }

        /// <summary>GET <c>/guilds/config</c>: the game's guild rules for this player.</summary>
        public async Task<GuildClientConfig> ConfigAsync(string? @as = null, CancellationToken ct = default)
        {
            var b = await BaseAsync(@as, ct).ConfigureAwait(false);
            return await SendAsync<GuildClientConfig>(HttpMethod.Get, $"{b}/guilds/config", null, ct).ConfigureAwait(false)
                ?? new GuildClientConfig();
        }

        /// <summary>GET <c>/guild</c>: your guild, role, permissions and week; null when guildless.</summary>
        public async Task<MyGuild?> MineAsync(string? @as = null, CancellationToken ct = default)
        {
            var b = await BaseAsync(@as, ct).ConfigureAwait(false);
            return await SendAsync<MyGuild>(HttpMethod.Get, $"{b}/guild", null, ct).ConfigureAwait(false);
        }

        /// <summary>GET <c>/guilds/:guildId</c>: any guild of this game with its roster.</summary>
        public async Task<GuildDetail> GetAsync(string guildId, string? @as = null, CancellationToken ct = default)
        {
            var b = await BaseAsync(@as, ct).ConfigureAwait(false);
            return await SendAsync<GuildDetail>(HttpMethod.Get, $"{b}/guilds/{Uri.EscapeDataString(guildId)}", null, ct)
                .ConfigureAwait(false) ?? new GuildDetail();
        }

        /// <summary>GET <c>/guilds?q=</c>: name search, prefix matches first (one page; see <c>SearchPageAsync</c>).</summary>
        public async Task<List<GuildSummary>> SearchAsync(
            string query, string? country = null, int? limit = null, string? @as = null, CancellationToken ct = default)
        {
            var page = await SearchPageAsync(query, country, limit, null, @as, ct).ConfigureAwait(false);
            return page.Guilds;
        }

        /// <summary>
        /// GET <c>/guilds?q=&amp;offset=</c>: one page of name search (<c>limit</c> default 20, max 50)
        /// and the <c>NextOffset</c> for the next page (null at the end).
        /// </summary>
        public async Task<GuildSearchPage> SearchPageAsync(
            string query, string? country = null, int? limit = null, int? offset = null,
            string? @as = null, CancellationToken ct = default)
        {
            var b = await BaseAsync(@as, ct).ConfigureAwait(false);
            var qs = Query(("q", query), ("country", country), ("limit", limit?.ToString()),
                ("offset", offset is > 0 ? offset.ToString() : null));
            var data = await SendAsync<GuildSearchPage>(HttpMethod.Get, $"{b}/guilds{qs}", null, ct)
                .ConfigureAwait(false);
            return data ?? new GuildSearchPage();
        }

        /// <summary>GET <c>/guilds/recommended</c>: guilds with room, friends' guilds first.</summary>
        public async Task<List<RecommendedGuild>> RecommendedAsync(
            string? country = null, int? limit = null, string? @as = null, CancellationToken ct = default)
        {
            var b = await BaseAsync(@as, ct).ConfigureAwait(false);
            var qs = Query(("country", country), ("limit", limit?.ToString()));
            var data = await SendAsync<GuildListPayload<RecommendedGuild>>(HttpMethod.Get, $"{b}/guilds/recommended{qs}", null, ct)
                .ConfigureAwait(false);
            return data?.Guilds ?? new List<RecommendedGuild>();
        }

        /// <summary>POST <c>/guilds</c>: found a guild (gate checked, cost charged atomically).</summary>
        public async Task<MyGuild> CreateAsync(GuildProfileInput input, string? @as = null, CancellationToken ct = default)
        {
            var b = await BaseAsync(@as, ct).ConfigureAwait(false);
            return await SendAsync<MyGuild>(HttpMethod.Post, $"{b}/guilds", input, ct).ConfigureAwait(false) ?? new MyGuild();
        }

        /// <summary>POST <c>/guilds/:guildId/join</c>: join an open guild, or ask to join a request guild.</summary>
        public async Task<GuildJoinResult> JoinAsync(
            string guildId, string? message = null, string? @as = null, CancellationToken ct = default)
        {
            var b = await BaseAsync(@as, ct).ConfigureAwait(false);
            var body = new Dictionary<string, object?>();
            if (message != null) body["message"] = message;
            return await SendAsync<GuildJoinResult>(HttpMethod.Post, $"{b}/guilds/{Uri.EscapeDataString(guildId)}/join", body, ct)
                .ConfigureAwait(false) ?? new GuildJoinResult();
        }

        /// <summary>GET <c>/guild-requests</c>: your pending join requests.</summary>
        public async Task<List<GuildJoinRequest>> MyRequestsAsync(string? @as = null, CancellationToken ct = default)
        {
            var b = await BaseAsync(@as, ct).ConfigureAwait(false);
            var data = await SendAsync<GuildRequestsPayload>(HttpMethod.Get, $"{b}/guild-requests", null, ct).ConfigureAwait(false);
            return data?.Requests ?? new List<GuildJoinRequest>();
        }

        /// <summary>DELETE <c>/guild-requests/:requestId</c>: withdraw a join request.</summary>
        public async Task CancelRequestAsync(string requestId, string? @as = null, CancellationToken ct = default)
        {
            var b = await BaseAsync(@as, ct).ConfigureAwait(false);
            await SendAsync<Newtonsoft.Json.Linq.JObject>(HttpMethod.Delete, $"{b}/guild-requests/{Uri.EscapeDataString(requestId)}", null, ct)
                .ConfigureAwait(false);
        }

        /// <summary>PATCH <c>/guild</c>: edit the profile (needs <c>edit_profile</c>).</summary>
        public async Task<GuildDetail> UpdateProfileAsync(GuildProfileInput patch, string? @as = null, CancellationToken ct = default)
        {
            var b = await BaseAsync(@as, ct).ConfigureAwait(false);
            return await SendAsync<GuildDetail>(new HttpMethod("PATCH"), $"{b}/guild", patch, ct).ConfigureAwait(false)
                ?? new GuildDetail();
        }

        /// <summary>POST <c>/guild/leave</c>: a leaving leader hands over; the last one out disbands.</summary>
        public async Task<GuildLeaveResult> LeaveAsync(string? @as = null, CancellationToken ct = default)
        {
            var b = await BaseAsync(@as, ct).ConfigureAwait(false);
            return await SendAsync<GuildLeaveResult>(HttpMethod.Post, $"{b}/guild/leave", null, ct).ConfigureAwait(false)
                ?? new GuildLeaveResult();
        }

        /// <summary>GET <c>/guild/requests</c>: your guild's pending join requests (needs <c>accept_requests</c>).</summary>
        public async Task<List<GuildJoinRequest>> RequestsAsync(string? @as = null, CancellationToken ct = default)
        {
            var b = await BaseAsync(@as, ct).ConfigureAwait(false);
            var data = await SendAsync<GuildRequestsPayload>(HttpMethod.Get, $"{b}/guild/requests", null, ct).ConfigureAwait(false);
            return data?.Requests ?? new List<GuildJoinRequest>();
        }

        /// <summary>POST <c>/guild/requests/:requestId/accept</c>.</summary>
        public async Task<GuildDetail> AcceptRequestAsync(string requestId, string? @as = null, CancellationToken ct = default)
        {
            var b = await BaseAsync(@as, ct).ConfigureAwait(false);
            return await SendAsync<GuildDetail>(HttpMethod.Post, $"{b}/guild/requests/{Uri.EscapeDataString(requestId)}/accept", null, ct)
                .ConfigureAwait(false) ?? new GuildDetail();
        }

        /// <summary>POST <c>/guild/requests/:requestId/decline</c>.</summary>
        public async Task DeclineRequestAsync(string requestId, string? @as = null, CancellationToken ct = default)
        {
            var b = await BaseAsync(@as, ct).ConfigureAwait(false);
            await SendAsync<Newtonsoft.Json.Linq.JObject>(HttpMethod.Post, $"{b}/guild/requests/{Uri.EscapeDataString(requestId)}/decline", null, ct)
                .ConfigureAwait(false);
        }

        /// <summary>POST <c>/guild/members/:id/kick</c>: they can't rejoin this guild for the configured block.</summary>
        public Task<GuildDetail> KickAsync(string memberExternalId, string? @as = null, CancellationToken ct = default)
            => MemberActionAsync("kick", memberExternalId, @as, ct);

        /// <summary>POST <c>/guild/members/:id/promote</c>: member → co-leader.</summary>
        public Task<GuildDetail> PromoteAsync(string memberExternalId, string? @as = null, CancellationToken ct = default)
            => MemberActionAsync("promote", memberExternalId, @as, ct);

        /// <summary>POST <c>/guild/members/:id/demote</c>: co-leader → member.</summary>
        public Task<GuildDetail> DemoteAsync(string memberExternalId, string? @as = null, CancellationToken ct = default)
            => MemberActionAsync("demote", memberExternalId, @as, ct);

        private async Task<GuildDetail> MemberActionAsync(string verb, string memberExternalId, string? @as, CancellationToken ct)
        {
            var b = await BaseAsync(@as, ct).ConfigureAwait(false);
            return await SendAsync<GuildDetail>(HttpMethod.Post, $"{b}/guild/members/{Uri.EscapeDataString(memberExternalId)}/{verb}", null, ct)
                .ConfigureAwait(false) ?? new GuildDetail();
        }

        /// <summary>POST <c>/guild/leader</c>: hand leadership to a member (leader only).</summary>
        public async Task<GuildDetail> TransferLeadershipAsync(string memberExternalId, string? @as = null, CancellationToken ct = default)
        {
            var b = await BaseAsync(@as, ct).ConfigureAwait(false);
            var body = new Dictionary<string, object?> { ["externalPlayerId"] = memberExternalId };
            return await SendAsync<GuildDetail>(HttpMethod.Post, $"{b}/guild/leader", body, ct).ConfigureAwait(false)
                ?? new GuildDetail();
        }

        /// <summary>GET <c>/guild/help</c>: open help requests, yours, and your weekly helps counter.</summary>
        public async Task<GuildHelpOverview> HelpAsync(string? @as = null, CancellationToken ct = default)
        {
            var b = await BaseAsync(@as, ct).ConfigureAwait(false);
            return await SendAsync<GuildHelpOverview>(HttpMethod.Get, $"{b}/guild/help", null, ct).ConfigureAwait(false)
                ?? new GuildHelpOverview();
        }

        /// <summary>POST <c>/guild/help</c>: ask guildmates for a resource on the game's help list.</summary>
        public async Task<GuildHelpRequest> RequestHelpAsync(
            string type, string resourceKey, int amount, string? @as = null, CancellationToken ct = default)
        {
            var b = await BaseAsync(@as, ct).ConfigureAwait(false);
            var body = new Dictionary<string, object?> { ["type"] = type, ["resourceKey"] = resourceKey, ["amount"] = amount };
            return await SendAsync<GuildHelpRequest>(HttpMethod.Post, $"{b}/guild/help", body, ct).ConfigureAwait(false)
                ?? new GuildHelpRequest();
        }

        /// <summary>POST <c>/guild/help/:requestId/donate</c>: delivered to them as a grant; may reward you.</summary>
        public async Task<GuildHelpDonation> DonateAsync(
            string requestId, int? amount = null, string? @as = null, CancellationToken ct = default)
        {
            var b = await BaseAsync(@as, ct).ConfigureAwait(false);
            var body = new Dictionary<string, object?>();
            if (amount.HasValue) body["amount"] = amount.Value;
            return await SendAsync<GuildHelpDonation>(HttpMethod.Post, $"{b}/guild/help/{Uri.EscapeDataString(requestId)}/donate", body, ct)
                .ConfigureAwait(false) ?? new GuildHelpDonation();
        }

        /// <summary>DELETE <c>/guild/help/:requestId</c>: withdraw your own help request.</summary>
        public async Task<GuildHelpRequest> CancelHelpAsync(string requestId, string? @as = null, CancellationToken ct = default)
        {
            var b = await BaseAsync(@as, ct).ConfigureAwait(false);
            return await SendAsync<GuildHelpRequest>(HttpMethod.Delete, $"{b}/guild/help/{Uri.EscapeDataString(requestId)}", null, ct)
                .ConfigureAwait(false) ?? new GuildHelpRequest();
        }
    }

    /// <summary>
    /// Resource client for chat (docs/26): <c>/sdk/v1/players/:p/chat/*</c>.
    /// Direct messages, groups, guild chat and public channels, plus the live
    /// stream. Every call acts as the active player (or <c>@as</c>). Chat is a
    /// paid add-on: when it's off for the game every call throws
    /// <see cref="KratyApiError"/> <c>403 chat_disabled</c>.
    ///
    /// <para>
    /// When <see cref="KratyClientOptions.ChatProfanityFilter"/> is set, every
    /// call (and the stream) sends <c>X-Kraty-Chat-Filter: on|off</c>.
    /// </para>
    /// </summary>
    public sealed class ChatClient
    {
        private static readonly JsonSerializer PlainJson = JsonSerializer.Create();
        private readonly KratyClient _client;
        public ChatClient(KratyClient client) => _client = client;

        // ── Cooldown tracking ───────────────────────────────────────
        // Kind + channel cooldown of every conversation the SDK returned, the
        // per-kind cooldowns from ConfigAsync (server defaults until then) and
        // when the player may next send in each conversation. Guarded by _gate:
        // sends can complete on any thread.
        private readonly object _gate = new object();
        private readonly Dictionary<string, (string Kind, int? ChannelCooldown)> _known = new();
        private readonly Dictionary<string, int> _kindCooldowns = new()
        {
            ["direct"] = 1, ["group"] = 1, ["guild"] = 2, ["channel"] = 15,
        };
        private readonly Dictionary<string, DateTime> _readyAt = new();

        private ChatConversation Observe(ChatConversation c)
        {
            if (!string.IsNullOrEmpty(c.ConversationId))
            {
                lock (_gate) _known[c.ConversationId] = (c.Kind, c.Channel?.CooldownSeconds);
            }
            return c;
        }

        private void HoldUntil(string conversationId, DateTime at)
        {
            lock (_gate)
            {
                if (!_readyAt.TryGetValue(conversationId, out var current) || at > current) _readyAt[conversationId] = at;
            }
        }

        private int CooldownOf(string conversationId)
        {
            lock (_gate)
            {
                if (!_known.TryGetValue(conversationId, out var k)) return 0;
                if (k.ChannelCooldown.HasValue) return k.ChannelCooldown.Value;
                return _kindCooldowns.TryGetValue(k.Kind, out var cd) ? cd : 0;
            }
        }

        private bool IsChannel(string conversationId)
        {
            lock (_gate) return _known.TryGetValue(conversationId, out var k) && k.Kind == "channel";
        }

        private TimeSpan CooldownLeft(string conversationId)
        {
            lock (_gate)
            {
                if (!_readyAt.TryGetValue(conversationId, out var at)) return TimeSpan.Zero;
                var left = at - DateTime.UtcNow;
                return left > TimeSpan.Zero ? left : TimeSpan.Zero;
            }
        }

        /// <summary>
        /// Seconds until the player may send in this conversation again (0
        /// when free): from the conversation's cooldown after a successful
        /// send, and from a <c>chat_slow_mode</c> 429's <c>retryAfterSeconds</c>
        /// (duplicate and rate-limit refusals don't hold the conversation).
        /// Use it to grey out the send button. Knows a conversation's cooldown once the SDK has
        /// returned it (<see cref="ConversationsAsync"/>,
        /// <see cref="ConversationAsync"/>, <see cref="ChannelAsync"/>, …) and
        /// <see cref="ConfigAsync"/>.
        /// </summary>
        public int CooldownRemaining(string conversationId)
        {
            lock (_gate)
            {
                if (!_readyAt.TryGetValue(conversationId, out var at)) return 0;
                var left = (at - DateTime.UtcNow).TotalSeconds;
                if (left <= 0)
                {
                    _readyAt.Remove(conversationId);
                    return 0;
                }
                return (int)Math.Ceiling(left);
            }
        }

        // One try of a send / edit: a single HTTP attempt (the outbox runs
        // the retries, so they never compound with the client's). A 429
        // surfaces at once (never retried: a retried refusal is another spam
        // strike). Only chat_slow_mode feeds CooldownRemaining: a
        // chat_duplicate_message wait is for that text only and
        // chat_rate_limited is per player, so neither holds the conversation.
        private async Task<T?> WriteOnceAsync<T>(
            HttpMethod method, string path, string conversationId, object body, CancellationToken ct)
        {
            try
            {
                var env = await _client.RequestWithHeadersAsync<DataEnvelope<T>>(
                    method, path, body, FilterHeaders(), ct, retryOn429: false, maxAttempts: 1).ConfigureAwait(false);
                return env.Data;
            }
            catch (KratyApiError err) when (err.IsChatSlowMode && err.RetryAfterSeconds.HasValue)
            {
                HoldUntil(conversationId, DateTime.UtcNow.AddSeconds(err.RetryAfterSeconds!.Value));
                throw;
            }
        }

        // ── Outbox ──────────────────────────────────────────────────
        // Sends queue per conversation (FIFO, one in flight; different
        // conversations run in parallel). Each message retries the SAME
        // request (same body, same clientMessageId, same idempotency key) on
        // network errors, timeouts and 5xx until ChatOutboxTimeout; 4xx (429
        // included) surface at once. In memory only.
        private readonly object _outboxGate = new object();
        private readonly Dictionary<string, Task> _outboxTails = new();

        private Task<ChatMessage> EnqueueSend(
            string conversationId, Dictionary<string, object?> body, string clientMessageId,
            string? @as, bool waitForCooldown, CancellationToken ct)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task? previous;
            lock (_outboxGate)
            {
                _outboxTails.TryGetValue(conversationId, out previous);
                _outboxTails[conversationId] = done.Task;
            }
            return RunAsync();

            void Release()
            {
                lock (_outboxGate)
                {
                    if (_outboxTails.TryGetValue(conversationId, out var tail) && tail == done.Task)
                        _outboxTails.Remove(conversationId);
                }
                done.TrySetResult(true);
            }

            async Task<ChatMessage> RunAsync()
            {
                var started = false;
                try
                {
                    // `previous` never faults: it's the earlier message's
                    // completion signal, not its result. Cancelling `ct`
                    // abandons the wait.
                    if (previous != null && !previous.IsCompleted)
                    {
                        var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        using (ct.Register(() => cancelled.TrySetResult(true)))
                        {
                            await Task.WhenAny(previous, cancelled.Task).ConfigureAwait(false);
                        }
                        ct.ThrowIfCancellationRequested();
                    }
                    started = true;

                    // Head of the queue: the cooldown decides, before any request.
                    var held = TimeSpan.Zero;
                    for (var left = CooldownLeft(conversationId); left > TimeSpan.Zero; left = CooldownLeft(conversationId))
                    {
                        if (!waitForCooldown) throw LocalSlowMode(left);
                        var before = clock.Elapsed;
                        await Task.Delay(left, ct).ConfigureAwait(false);
                        held += clock.Elapsed - before;
                    }

                    var path = await ConversationPathAsync(conversationId, @as, ct).ConfigureAwait(false);
                    var message = await DeliverAsync(
                        HttpMethod.Post, $"{path}/messages", conversationId, body, clientMessageId,
                        clock, _client.ChatOutboxTimeout + held,
                        retryCt => EnqueueSend(conversationId, body, clientMessageId, @as, waitForCooldown, retryCt),
                        ct
                    ).ConfigureAwait(false);
                    HoldUntil(conversationId, DateTime.UtcNow.AddSeconds(CooldownOf(conversationId)));
                    return message;
                }
                finally
                {
                    // Abandoned before its turn: the next message must still
                    // wait for the one ahead of this one (one in flight).
                    if (!started && previous != null && !previous.IsCompleted)
                        _ = previous.ContinueWith(_ => Release(), TaskScheduler.Default);
                    else
                        Release();
                }
            }
        }

        // Retries one send / edit on network errors, timeouts and 5xx until
        // `budget` (measured on `clock`) would pass, then throws
        // KratyChatNotSentError. Waits the server's Retry-After /
        // details.retryAfterSeconds, else a jittered backoff capped at 10 s.
        private async Task<ChatMessage> DeliverAsync(
            HttpMethod method, string path, string conversationId, object body, string? clientMessageId,
            System.Diagnostics.Stopwatch clock, TimeSpan budget,
            Func<CancellationToken, Task<ChatMessage>> resend, CancellationToken ct)
        {
            for (var attempt = 1; ; attempt++)
            {
                Exception last;
                TimeSpan? hint = null;
                try
                {
                    return await WriteOnceAsync<ChatMessage>(method, path, conversationId, body, ct).ConfigureAwait(false)
                        ?? new ChatMessage();
                }
                catch (KratyApiError err) when (err.Status >= 500)
                {
                    last = err;
                    hint = err.ServerRetryAfter;
                }
                catch (KratyNetworkError err)
                {
                    last = err;
                }
                var delay = hint ?? SseReconnectLoop.Backoff(attempt, _client.ChatOutboxBackoffBase, _client.ChatOutboxBackoffMax);
                if (clock.Elapsed + delay > budget)
                {
                    throw new KratyChatNotSentError(
                        $"chat {(clientMessageId != null ? "message" : "edit")} not sent after {attempt} attempt(s): {last.Message}",
                        conversationId, clientMessageId, last, resend);
                }
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }

        private static KratyApiError LocalSlowMode(TimeSpan left)
        {
            var seconds = Math.Max(1, (int)Math.Ceiling(left.TotalSeconds));
            return new KratyApiError(
                429,
                KratyErrorCode.ChatSlowMode,
                $"wait {seconds}s before sending here again (refused locally, nothing was sent)",
                new Dictionary<string, object?> { ["raw"] = "{\"retryAfterSeconds\":" + seconds + ",\"local\":true}" });
        }

        private async Task<string> BaseAsync(string? @as, CancellationToken ct)
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            return $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/chat";
        }

        private async Task<string> ConversationPathAsync(string conversationId, string? @as, CancellationToken ct)
        {
            var b = await BaseAsync(@as, ct).ConfigureAwait(false);
            return $"{b}/conversations/{Uri.EscapeDataString(conversationId)}";
        }

        private Dictionary<string, string>? FilterHeaders()
        {
            var filter = _client.ChatFilterValue;
            return filter == null ? null : new Dictionary<string, string> { [ChatStreamFactory.FilterHeader] = filter };
        }

        private async Task<T?> CallAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
        {
            var env = await _client.RequestWithHeadersAsync<DataEnvelope<T>>(method, path, body, FilterHeaders(), ct)
                .ConfigureAwait(false);
            return env.Data;
        }

        // Free-form JSON (payload / metadata) keeps the caller's keys exactly:
        // the SDK's camelCase settings would otherwise rewrite dictionary keys.
        private static JToken? Raw(object? value) =>
            value == null ? null : value as JToken ?? JToken.FromObject(value, PlainJson);

        private static string Query(params (string Key, string? Value)[] pairs)
        {
            var parts = new List<string>();
            foreach (var (key, value) in pairs)
            {
                if (string.IsNullOrEmpty(value)) continue;
                parts.Add($"{key}={Uri.EscapeDataString(value)}");
            }
            return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
        }

        // ── Config, preferences, inbox ──────────────────────────────

        /// <summary>GET <c>/chat/config</c>: kinds, channels, message rules, rate limit, your preferences and moderation standing.</summary>
        public async Task<ChatClientConfig> ConfigAsync(string? @as = null, CancellationToken ct = default)
        {
            var b = await BaseAsync(@as, ct).ConfigureAwait(false);
            var cfg = await CallAsync<ChatClientConfig>(HttpMethod.Get, $"{b}/config", null, ct).ConfigureAwait(false)
                ?? new ChatClientConfig();
            lock (_gate)
            {
                foreach (var kv in cfg.Kinds)
                {
                    if (kv.Value != null && kv.Value.CooldownSeconds > 0) _kindCooldowns[kv.Key] = kv.Value.CooldownSeconds;
                }
            }
            return cfg;
        }

        /// <summary>GET <c>/chat/preferences</c>: the player's saved filter choice and who may DM them.</summary>
        public async Task<ChatPreferences> PreferencesAsync(string? @as = null, CancellationToken ct = default)
        {
            var b = await BaseAsync(@as, ct).ConfigureAwait(false);
            return await CallAsync<ChatPreferences>(HttpMethod.Get, $"{b}/preferences", null, ct).ConfigureAwait(false)
                ?? new ChatPreferences();
        }

        /// <summary>
        /// PUT <c>/chat/preferences</c>: save the player's choices. Only the
        /// properties you assign on <paramref name="update"/> are sent; assign
        /// <c>ProfanityFilter = null</c> to clear back to the kinds' defaults.
        /// </summary>
        public async Task<ChatPreferences> SetPreferencesAsync(
            ChatPreferencesUpdate update, string? @as = null, CancellationToken ct = default)
        {
            if (update == null) throw new ArgumentNullException(nameof(update));
            if (!update.HasProfanityFilter && update.AllowDirectFrom == null)
                throw new ArgumentException("nothing to update: set ProfanityFilter and/or AllowDirectFrom", nameof(update));
            var b = await BaseAsync(@as, ct).ConfigureAwait(false);
            var body = new Dictionary<string, object?>();
            if (update.HasProfanityFilter) body["profanityFilter"] = update.ProfanityFilter;
            if (update.AllowDirectFrom != null) body["allowDirectFrom"] = update.AllowDirectFrom;
            return await CallAsync<ChatPreferences>(HttpMethod.Put, $"{b}/preferences", body, ct).ConfigureAwait(false)
                ?? new ChatPreferences();
        }

        /// <summary>
        /// GET <c>/chat/conversations</c>: the inbox, most recent first
        /// (<paramref name="limit"/> default 30, max 100). Narrow with
        /// <paramref name="kind"/>; page with <c>NextOffset</c>.
        /// </summary>
        public async Task<ChatConversationPage> ConversationsAsync(
            string? kind = null, int? limit = null, int? offset = null,
            string? @as = null, CancellationToken ct = default)
        {
            var b = await BaseAsync(@as, ct).ConfigureAwait(false);
            var qs = Query(("kind", kind), ("limit", limit?.ToString()), ("offset", offset is > 0 ? offset.ToString() : null));
            var page = await CallAsync<ChatConversationPage>(HttpMethod.Get, $"{b}/conversations{qs}", null, ct).ConfigureAwait(false)
                ?? new ChatConversationPage();
            foreach (var c in page.Conversations) Observe(c);
            return page;
        }

        /// <summary>GET <c>/chat/unread</c>: unread totals for a badge (muted conversations don't count).</summary>
        public async Task<ChatUnread> UnreadAsync(string? @as = null, CancellationToken ct = default)
        {
            var b = await BaseAsync(@as, ct).ConfigureAwait(false);
            return await CallAsync<ChatUnread>(HttpMethod.Get, $"{b}/unread", null, ct).ConfigureAwait(false)
                ?? new ChatUnread();
        }

        /// <summary>GET <c>/chat/conversations/:id</c>: one conversation, with <c>Members</c> for groups.</summary>
        public async Task<ChatConversation> ConversationAsync(
            string conversationId, string? @as = null, CancellationToken ct = default)
        {
            var path = await ConversationPathAsync(conversationId, @as, ct).ConfigureAwait(false);
            return Observe(await CallAsync<ChatConversation>(HttpMethod.Get, path, null, ct).ConfigureAwait(false)
                ?? new ChatConversation());
        }

        // ── Opening conversations ───────────────────────────────────

        /// <summary>
        /// POST <c>/chat/direct</c>: open (or find) the direct conversation
        /// with another player, by external id or by
        /// <paramref name="friendCode"/> (pass exactly one).
        /// </summary>
        public async Task<ChatConversation> OpenDirectAsync(
            string? externalPlayerId = null, string? friendCode = null,
            string? @as = null, CancellationToken ct = default)
        {
            if ((externalPlayerId == null) == (friendCode == null))
                throw new ArgumentException("pass exactly one of externalPlayerId or friendCode");
            var b = await BaseAsync(@as, ct).ConfigureAwait(false);
            var body = new Dictionary<string, object?>();
            if (externalPlayerId != null) body["externalPlayerId"] = externalPlayerId;
            else body["friendCode"] = friendCode;
            return Observe(await CallAsync<ChatConversation>(HttpMethod.Post, $"{b}/direct", body, ct).ConfigureAwait(false)
                ?? new ChatConversation());
        }

        /// <summary>POST <c>/chat/groups</c>: create a group you own; returns it with <c>Members</c>.</summary>
        public async Task<ChatConversation> CreateGroupAsync(
            string title, IEnumerable<string>? externalPlayerIds = null, Dictionary<string, object?>? metadata = null,
            string? @as = null, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(title)) throw new ArgumentException("title is required", nameof(title));
            var b = await BaseAsync(@as, ct).ConfigureAwait(false);
            var body = new Dictionary<string, object?>
            {
                ["title"] = title,
                ["externalPlayerIds"] = externalPlayerIds?.ToList() ?? new List<string>(),
            };
            if (metadata != null) body["metadata"] = Raw(metadata);
            return Observe(await CallAsync<ChatConversation>(HttpMethod.Post, $"{b}/groups", body, ct).ConfigureAwait(false)
                ?? new ChatConversation());
        }

        /// <summary>PATCH <c>/chat/conversations/:id</c>: rename a group or replace its metadata (owner / admin).</summary>
        public async Task<ChatConversation> UpdateGroupAsync(
            string conversationId, string? title = null, Dictionary<string, object?>? metadata = null,
            string? @as = null, CancellationToken ct = default)
        {
            if (title == null && metadata == null)
                throw new ArgumentException("nothing to update: pass title and/or metadata");
            var path = await ConversationPathAsync(conversationId, @as, ct).ConfigureAwait(false);
            var body = new Dictionary<string, object?>();
            if (title != null) body["title"] = title;
            if (metadata != null) body["metadata"] = Raw(metadata);
            return Observe(await CallAsync<ChatConversation>(new HttpMethod("PATCH"), path, body, ct).ConfigureAwait(false)
                ?? new ChatConversation());
        }

        /// <summary>POST <c>/chat/conversations/:id/members</c>: add players to a group.</summary>
        public async Task<ChatConversation> AddMembersAsync(
            string conversationId, IEnumerable<string> externalPlayerIds,
            string? @as = null, CancellationToken ct = default)
        {
            if (externalPlayerIds == null) throw new ArgumentNullException(nameof(externalPlayerIds));
            var path = await ConversationPathAsync(conversationId, @as, ct).ConfigureAwait(false);
            var body = new Dictionary<string, object?> { ["externalPlayerIds"] = externalPlayerIds.ToList() };
            return Observe(await CallAsync<ChatConversation>(HttpMethod.Post, $"{path}/members", body, ct).ConfigureAwait(false)
                ?? new ChatConversation());
        }

        /// <summary>DELETE <c>/chat/conversations/:id/members/:externalPlayerId</c>: remove a member from a group.</summary>
        public async Task<ChatMemberRemoved> RemoveMemberAsync(
            string conversationId, string externalPlayerId,
            string? @as = null, CancellationToken ct = default)
        {
            var path = await ConversationPathAsync(conversationId, @as, ct).ConfigureAwait(false);
            return await CallAsync<ChatMemberRemoved>(
                HttpMethod.Delete, $"{path}/members/{Uri.EscapeDataString(externalPlayerId)}", null, ct
            ).ConfigureAwait(false) ?? new ChatMemberRemoved();
        }

        /// <summary>PUT <c>/chat/conversations/:id/members/:externalPlayerId/role</c>: <c>owner</c>, <c>admin</c> or <c>member</c>.</summary>
        public async Task<ChatConversation> SetRoleAsync(
            string conversationId, string externalPlayerId, string role,
            string? @as = null, CancellationToken ct = default)
        {
            var path = await ConversationPathAsync(conversationId, @as, ct).ConfigureAwait(false);
            var body = new Dictionary<string, object?> { ["role"] = role };
            return Observe(await CallAsync<ChatConversation>(
                HttpMethod.Put, $"{path}/members/{Uri.EscapeDataString(externalPlayerId)}/role", body, ct
            ).ConfigureAwait(false) ?? new ChatConversation());
        }

        /// <summary>POST <c>/chat/conversations/:id/leave</c>: leave a group (an owner hands over first).</summary>
        public async Task<ChatLeaveResult> LeaveAsync(
            string conversationId, string? @as = null, CancellationToken ct = default)
        {
            var path = await ConversationPathAsync(conversationId, @as, ct).ConfigureAwait(false);
            return await CallAsync<ChatLeaveResult>(HttpMethod.Post, $"{path}/leave", null, ct).ConfigureAwait(false)
                ?? new ChatLeaveResult();
        }

        /// <summary>GET <c>/chat/guild</c>: your guild's conversation (<c>409 conflict</c> when guildless).</summary>
        public async Task<ChatConversation> GuildAsync(string? @as = null, CancellationToken ct = default)
        {
            var b = await BaseAsync(@as, ct).ConfigureAwait(false);
            return Observe(await CallAsync<ChatConversation>(HttpMethod.Get, $"{b}/guild", null, ct).ConfigureAwait(false)
                ?? new ChatConversation());
        }

        /// <summary>GET <c>/chat/channels</c>: the game's public channels as conversations.</summary>
        public async Task<List<ChatConversation>> ChannelsAsync(string? @as = null, CancellationToken ct = default)
        {
            var b = await BaseAsync(@as, ct).ConfigureAwait(false);
            var data = await CallAsync<ChatChannelsPayload>(HttpMethod.Get, $"{b}/channels", null, ct).ConfigureAwait(false);
            var channels = data?.Channels ?? new List<ChatConversation>();
            foreach (var c in channels) Observe(c);
            return channels;
        }

        /// <summary>GET <c>/chat/channels/:channelKey</c>: one public channel's conversation.</summary>
        public async Task<ChatConversation> ChannelAsync(
            string channelKey, string? @as = null, CancellationToken ct = default)
        {
            var b = await BaseAsync(@as, ct).ConfigureAwait(false);
            return Observe(await CallAsync<ChatConversation>(HttpMethod.Get, $"{b}/channels/{Uri.EscapeDataString(channelKey)}", null, ct)
                .ConfigureAwait(false) ?? new ChatConversation());
        }

        // ── Messages ────────────────────────────────────────────────

        /// <summary>
        /// GET <c>/chat/conversations/:id/messages</c>: a page of messages,
        /// oldest to newest. No cursor = the latest page; <paramref name="before"/>
        /// pages back, <paramref name="after"/> catches up (e.g. after a
        /// stream reconnect). Pass one cursor at most.
        /// </summary>
        public async Task<ChatMessagePage> MessagesAsync(
            string conversationId, long? before = null, long? after = null, int? limit = null,
            string? @as = null, CancellationToken ct = default)
        {
            if (before.HasValue && after.HasValue)
                throw new ArgumentException("pass before or after, not both");
            var path = await ConversationPathAsync(conversationId, @as, ct).ConfigureAwait(false);
            var qs = Query(("before", before?.ToString()), ("after", after?.ToString()), ("limit", limit?.ToString()));
            return await CallAsync<ChatMessagePage>(HttpMethod.Get, $"{path}/messages{qs}", null, ct).ConfigureAwait(false)
                ?? new ChatMessagePage();
        }

        /// <summary>
        /// POST <c>/chat/conversations/:id/messages</c>: send a text message
        /// through the outbox, so a busy server or a network blip never fails it.
        ///
        /// <para>
        /// <paramref name="clientMessageId"/> defaults to a fresh GUID, handed to
        /// <paramref name="onPending"/> at once (synchronously, on the calling
        /// thread, before any request) so the game can show a pending bubble and
        /// match the stream's echo to it. The server dedupes on it: a resend
        /// returns the original message, never a second one.
        /// </para>
        ///
        /// <para>
        /// Sends to one conversation go out one at a time, in call order.
        /// Network errors, timeouts and 5xx (503 while the server scales up) are
        /// retried with the same <c>clientMessageId</c>, waiting the server's
        /// <c>Retry-After</c> or a backoff, for up to
        /// <see cref="KratyClientOptions.ChatOutboxTimeout"/> (120 s); then it
        /// throws <see cref="KratyChatNotSentError"/> (a
        /// <see cref="KratyNetworkError"/>): show "not sent, tap to retry" and
        /// call its <c>RetryAsync()</c>. The outbox lives in memory: a message
        /// still pending when the app quits is not sent.
        /// </para>
        ///
        /// <para>
        /// Cooldown: if <see cref="CooldownRemaining"/> is above 0 when the
        /// message reaches the head of its queue, it fails at once with a local
        /// 429 <c>chat_slow_mode</c> (<see cref="KratyApiError.IsLocal"/>, no
        /// request, no spam strike), unless <paramref name="waitForCooldown"/>
        /// (default <see cref="KratyClientOptions.ChatWaitForCooldown"/>) holds
        /// it until the cooldown ends.
        /// </para>
        ///
        /// <para>
        /// Anti-spam refusals from the server throw at once, never retried: 429
        /// <c>chat_slow_mode</c>, <c>chat_duplicate_message</c> or
        /// <c>chat_rate_limited</c>, with
        /// <see cref="KratyApiError.RetryAfterSeconds"/>. Don't resend before
        /// then: repeated refusals mute the player automatically (403
        /// <c>chat_muted</c>). Other 4xx throw at once too.
        /// </para>
        /// </summary>
        /// <param name="conversationId">The conversation to post in.</param>
        /// <param name="text">The message text.</param>
        /// <param name="replyToId">Optional message this one replies to.</param>
        /// <param name="clientMessageId">Your id for the message (pass the error's <c>ClientMessageId</c> to resend by hand); default a fresh GUID.</param>
        /// <param name="as">Address a different player (server-side tooling only).</param>
        /// <param name="ct">Cancels the send (and any wait in the outbox).</param>
        /// <param name="waitForCooldown">Hold the message until the conversation's cooldown ends instead of failing; null = the client option.</param>
        /// <param name="onPending">Receives the <c>clientMessageId</c> right away.</param>
        public Task<ChatMessage> SendAsync(
            string conversationId, string text, string? replyToId = null, string? clientMessageId = null,
            string? @as = null, CancellationToken ct = default,
            bool? waitForCooldown = null, Action<string>? onPending = null)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var body = new Dictionary<string, object?> { ["type"] = "text", ["text"] = text };
            return PostMessageAsync(conversationId, body, replyToId, clientMessageId, @as, waitForCooldown, onPending, ct);
        }

        /// <summary>
        /// POST <c>/chat/conversations/:id/messages</c> with <c>type: custom</c>:
        /// a game card (<paramref name="customType"/> is one of the config's
        /// <c>Messages.CustomTypes</c>) with an optional caption.
        /// <paramref name="payload"/> may be a <see cref="JToken"/>, a dictionary
        /// or a POCO; its keys are sent exactly as given. Goes through the
        /// outbox exactly like <see cref="SendAsync"/> (queue, retries,
        /// cooldown, <paramref name="onPending"/>, <see cref="KratyChatNotSentError"/>).
        /// </summary>
        public Task<ChatMessage> SendCustomAsync(
            string conversationId, string customType, object? payload, string? text = null,
            string? replyToId = null, string? clientMessageId = null,
            string? @as = null, CancellationToken ct = default,
            bool? waitForCooldown = null, Action<string>? onPending = null)
        {
            if (string.IsNullOrEmpty(customType)) throw new ArgumentException("customType is required", nameof(customType));
            var body = new Dictionary<string, object?> { ["type"] = "custom", ["customType"] = customType };
            if (payload != null) body["payload"] = Raw(payload);
            if (text != null) body["text"] = text;
            return PostMessageAsync(conversationId, body, replyToId, clientMessageId, @as, waitForCooldown, onPending, ct);
        }

        private Task<ChatMessage> PostMessageAsync(
            string conversationId, Dictionary<string, object?> body, string? replyToId, string? clientMessageId,
            string? @as, bool? waitForCooldown, Action<string>? onPending, CancellationToken ct)
        {
            if (replyToId != null) body["replyToId"] = replyToId;
            var id = string.IsNullOrEmpty(clientMessageId) ? Guid.NewGuid().ToString() : clientMessageId!;
            body["clientMessageId"] = id;
            onPending?.Invoke(id);
            return EnqueueSend(conversationId, body, id, @as, waitForCooldown ?? _client.ChatWaitForCooldown, ct);
        }

        /// <summary>
        /// PATCH <c>/chat/conversations/:id/messages/:messageId</c>: edit the
        /// text of your own message within the game's edit window
        /// (<c>Config.Messages.EditWindowSeconds</c>; 0 = editing off). Returns the
        /// updated message with <see cref="ChatMessage.EditedAt"/> set; the
        /// conversation's live streams receive <c>message_edited</c>. Outside the
        /// window, or on someone else's message: <c>403 chat_edit_not_allowed</c>.
        /// Edits count toward the per-minute rate; a 429 throws at once like
        /// <see cref="SendAsync"/>. Network errors, timeouts and 5xx are retried
        /// like a send (no queue, no cooldown check) for up to
        /// <see cref="KratyClientOptions.ChatOutboxTimeout"/>, then it throws
        /// <see cref="KratyChatNotSentError"/> (<c>ClientMessageId</c> null;
        /// <c>RetryAsync()</c> re-runs the edit).
        /// </summary>
        public async Task<ChatMessage> EditMessageAsync(
            string conversationId, string messageId, string text, string? @as = null, CancellationToken ct = default)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var body = new Dictionary<string, object?> { ["text"] = text };
            return await EditCoreAsync(conversationId, messageId, body, @as, ct).ConfigureAwait(false);
        }

        private async Task<ChatMessage> EditCoreAsync(
            string conversationId, string messageId, Dictionary<string, object?> body, string? @as, CancellationToken ct)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var path = await ConversationPathAsync(conversationId, @as, ct).ConfigureAwait(false);
            return await DeliverAsync(
                new HttpMethod("PATCH"), $"{path}/messages/{Uri.EscapeDataString(messageId)}", conversationId, body, null,
                clock, _client.ChatOutboxTimeout,
                retryCt => EditCoreAsync(conversationId, messageId, body, @as, retryCt),
                ct
            ).ConfigureAwait(false);
        }

        /// <summary>DELETE <c>/chat/conversations/:id/messages/:messageId</c>: delete your own message (or any, as a moderator here).</summary>
        public async Task<ChatMessageDeleted> DeleteMessageAsync(
            string conversationId, string messageId, string? @as = null, CancellationToken ct = default)
        {
            var path = await ConversationPathAsync(conversationId, @as, ct).ConfigureAwait(false);
            return await CallAsync<ChatMessageDeleted>(
                HttpMethod.Delete, $"{path}/messages/{Uri.EscapeDataString(messageId)}", null, ct
            ).ConfigureAwait(false) ?? new ChatMessageDeleted();
        }

        /// <summary>POST <c>/chat/conversations/:id/read</c>: mark read up to <paramref name="seq"/> (never moves backwards).</summary>
        public async Task<ChatReadResult> MarkReadAsync(
            string conversationId, long seq, string? @as = null, CancellationToken ct = default)
        {
            var path = await ConversationPathAsync(conversationId, @as, ct).ConfigureAwait(false);
            var body = new Dictionary<string, object?> { ["seq"] = seq };
            return await CallAsync<ChatReadResult>(HttpMethod.Post, $"{path}/read", body, ct).ConfigureAwait(false)
                ?? new ChatReadResult();
        }

        /// <summary>
        /// POST <c>/chat/conversations/:id/typing</c>: show a typing indicator
        /// to the others (call every few seconds while typing). Public channels
        /// have no typing indicators: for a conversation the SDK knows is a
        /// channel, nothing is sent.
        /// </summary>
        public async Task TypingAsync(string conversationId, string? @as = null, CancellationToken ct = default)
        {
            if (IsChannel(conversationId)) return;
            var path = await ConversationPathAsync(conversationId, @as, ct).ConfigureAwait(false);
            await CallAsync<JObject>(HttpMethod.Post, $"{path}/typing", new Dictionary<string, object?>(), ct).ConfigureAwait(false);
        }

        /// <summary>
        /// PUT <c>/chat/conversations/:id/settings</c>: mute notifications
        /// until a time (assign <c>MutedUntil = null</c> to unmute) and/or hide
        /// the conversation from the inbox. Only assigned properties are sent.
        /// </summary>
        public async Task<ChatConversation> SetConversationSettingsAsync(
            string conversationId, ChatConversationSettings settings,
            string? @as = null, CancellationToken ct = default)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (!settings.HasMutedUntil && settings.Hidden == null)
                throw new ArgumentException("nothing to update: set MutedUntil and/or Hidden", nameof(settings));
            var path = await ConversationPathAsync(conversationId, @as, ct).ConfigureAwait(false);
            var body = new Dictionary<string, object?>();
            if (settings.HasMutedUntil)
            {
                body["mutedUntil"] = settings.MutedUntil?.ToUniversalTime()
                    .ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
            }
            if (settings.Hidden != null) body["hidden"] = settings.Hidden.Value;
            return Observe(await CallAsync<ChatConversation>(HttpMethod.Put, $"{path}/settings", body, ct).ConfigureAwait(false)
                ?? new ChatConversation());
        }

        // ── Live stream ─────────────────────────────────────────────

        /// <summary>
        /// GET <c>/chat/stream</c>: one SSE stream with every conversation in
        /// the player's inbox plus the public <paramref name="channels"/> named
        /// (at most 10). Message text arrives resolved for this player, and
        /// messages from blocked players never arrive.
        ///
        /// <para>
        /// Wire callbacks in <paramref name="configure"/>: it runs before the
        /// first connect, so nothing (not even <c>ready</c>) can fire first.
        /// Callbacks run on the HTTP background thread.
        /// </para>
        ///
        /// <para>
        /// The stream heals itself (<see cref="StreamOptions.AutoReconnect"/>,
        /// on by default): after a server <c>close</c> (deploy, rebalance,
        /// <c>slow_consumer</c>) it waits the server's <c>retryAfterMs</c>;
        /// after a drop, a busy server (503) or 40 s without even a ping it
        /// backs off (up to 15 s) and reopens. Once the new connection is
        /// <c>ready</c> it catches up every conversation it delivered messages
        /// for and hands the missed ones to <c>OnMessage</c> before any live
        /// event, never twice. <c>OnConnection</c> reports
        /// <c>Reconnecting</c> / <c>Connected</c>; <c>OnClose</c> and drops
        /// don't reach the game. Only a refused reopen (401, 403
        /// <c>chat_disabled</c>, …) stops it, through <c>OnError</c>. If the
        /// first connect is refused this throws; if it fails transiently the
        /// stream starts out reconnecting. <c>AutoReconnect = false</c> restores
        /// the single connection: reopen on <c>OnClose</c> / <c>OnError</c> and
        /// catch up with <see cref="MessagesAsync"/>'s <c>after</c> yourself.
        /// </para>
        /// <code>
        /// var stream = await kraty.Chat.LiveAsync(new[] { "global" }, s =>
        /// {
        ///     s.OnMessage = m => dispatcher.Enqueue(() => AppendBubble(m));
        ///     s.OnConnection = c => dispatcher.Enqueue(() =>
        ///         reconnectingBadge.SetActive(c.State == StreamConnectionState.Reconnecting));
        /// });
        /// // later
        /// await stream.CancelAsync();
        /// </code>
        /// </summary>
        public async Task<ChatStream> LiveAsync(
            IEnumerable<string>? channels = null, Action<ChatStream>? configure = null,
            string? @as = null, CancellationToken ct = default, StreamOptions? options = null)
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            return await ChatStreamFactory.OpenAsync(
                _client.HttpForStreaming,
                _client.BaseUrlForStreaming,
                externalPlayerId,
                _client.AuthHeaderForStreaming,
                _client.PlayerSecretForStreaming,
                channels,
                _client.ChatFilterValue,
                configure,
                ct,
                options,
                (conversationId, after, token) =>
                    MessagesAsync(conversationId, after: after, @as: externalPlayerId, ct: token)
            ).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Resource client for player safety (docs/26):
    /// <c>/sdk/v1/players/:p/reports</c> and <c>/moderation/*</c>. Works in
    /// every game, with or without the chat add-on, so the game can offer a
    /// report button anywhere (store compliance).
    /// </summary>
    public sealed class ModerationClient
    {
        private readonly KratyClient _client;
        public ModerationClient(KratyClient client) => _client = client;

        private async Task<string> BaseAsync(string? @as, CancellationToken ct)
        {
            var externalPlayerId = await _client.ResolvePlayerIdAsync(@as, ct).ConfigureAwait(false);
            return $"/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}";
        }

        private async Task<T?> CallAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
        {
            var env = await _client.RequestAsync<DataEnvelope<T>>(method, path, body: body, cancellationToken: ct)
                .ConfigureAwait(false);
            return env.Data;
        }

        /// <summary>
        /// POST <c>/reports</c>: report another player. Attach
        /// <c>ConversationId</c> + <c>MessageIds</c> for chat reports so the
        /// reviewer sees the evidence.
        /// </summary>
        public async Task<MyReport> ReportAsync(ReportPlayerInput input, string? @as = null, CancellationToken ct = default)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var b = await BaseAsync(@as, ct).ConfigureAwait(false);
            return await CallAsync<MyReport>(HttpMethod.Post, $"{b}/reports", input, ct).ConfigureAwait(false)
                ?? new MyReport();
        }

        /// <summary>GET <c>/reports</c>: the reports this player filed, newest first (<paramref name="limit"/> default 20, max 100).</summary>
        public async Task<List<MyReport>> ReportsAsync(int? limit = null, string? @as = null, CancellationToken ct = default)
        {
            var b = await BaseAsync(@as, ct).ConfigureAwait(false);
            var qs = limit.HasValue ? $"?limit={limit.Value}" : string.Empty;
            var data = await CallAsync<MyReportsPayload>(HttpMethod.Get, $"{b}/reports{qs}", null, ct).ConfigureAwait(false);
            return data?.Reports ?? new List<MyReport>();
        }

        /// <summary>GET <c>/moderation/status</c>: can the player chat / post, and the restrictions in force.</summary>
        public async Task<ModerationStatus> StatusAsync(string? @as = null, CancellationToken ct = default)
        {
            var b = await BaseAsync(@as, ct).ConfigureAwait(false);
            return await CallAsync<ModerationStatus>(HttpMethod.Get, $"{b}/moderation/status", null, ct).ConfigureAwait(false)
                ?? new ModerationStatus();
        }

        /// <summary>POST <c>/moderation/warnings/:restrictionId/acknowledge</c>: the player saw the warning; it stops showing.</summary>
        public async Task<Restriction> AcknowledgeWarningAsync(
            string restrictionId, string? @as = null, CancellationToken ct = default)
        {
            var b = await BaseAsync(@as, ct).ConfigureAwait(false);
            return await CallAsync<Restriction>(
                HttpMethod.Post, $"{b}/moderation/warnings/{Uri.EscapeDataString(restrictionId)}/acknowledge", null, ct
            ).ConfigureAwait(false) ?? new Restriction();
        }
    }

    /// <summary>
    /// Resource client for <c>/sdk/v1/lobbies/:id</c>. Used after
    /// <c>/start</c> returns <c>lobby_forming</c>: poll this until
    /// <see cref="Lobby.Status"/> transitions out of <c>forming</c>,
    /// then retry start.
    /// </summary>
    public sealed class LobbiesClient
    {
        private readonly KratyClient _client;
        public LobbiesClient(KratyClient client) => _client = client;

        public async Task<Lobby> ReadAsync(string lobbyId, CancellationToken ct = default)
        {
            var env = await _client.RequestAsync<DataEnvelope<Lobby>>(
                HttpMethod.Get,
                $"/sdk/v1/lobbies/{Uri.EscapeDataString(lobbyId)}",
                cancellationToken: ct
            ).ConfigureAwait(false);
            return env.Data ?? new Lobby();
        }
    }

    /// <summary>
    /// Adaptive polling for a player's pending grants. Grows the
    /// interval while the queue is empty; resets to the floor when
    /// grants land. Resolves when the cancellation token fires.
    /// </summary>
    public static class GrantPolling
    {
        public sealed class Options
        {
            public TimeSpan Start { get; set; } = TimeSpan.FromSeconds(2);
            public double Grow { get; set; } = 1.5;
            public TimeSpan Max { get; set; } = TimeSpan.FromSeconds(30);
            /// <summary>Fires for every batch (including empty ones).</summary>
            public Action<List<Grant>>? OnBatch { get; set; }
        }

        public static async Task PollPendingAsync(
            GrantsClient grants,
            Options? opts = null,
            string? @as = null,
            CancellationToken cancellationToken = default
        )
        {
            opts ??= new Options();
            var interval = opts.Start;
            while (!cancellationToken.IsCancellationRequested)
            {
                var batch = await grants.ListPendingAsync(@as: @as, ct: cancellationToken).ConfigureAwait(false);
                opts.OnBatch?.Invoke(batch);
                if (batch.Count > 0)
                {
                    interval = opts.Start;
                }
                else
                {
                    var grown = TimeSpan.FromMilliseconds(interval.TotalMilliseconds * opts.Grow);
                    interval = grown > opts.Max ? opts.Max : grown;
                }
                try
                {
                    await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    public static class LobbyPolling
    {
        public sealed class Options
        {
            public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(1);
            public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);
        }

        /// <summary>
        /// Polls a lobby until it transitions out of <c>forming</c>.
        /// Throws <see cref="TimeoutException"/> if the deadline
        /// elapses, <see cref="OperationCanceledException"/> if the
        /// token is cancelled.
        /// </summary>
        public static async Task<Lobby> UntilActiveAsync(
            LobbiesClient lobbies,
            string lobbyId,
            Options? opts = null,
            CancellationToken cancellationToken = default
        )
        {
            opts ??= new Options();
            var deadline = DateTimeOffset.UtcNow + opts.Timeout;
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var lobby = await lobbies.ReadAsync(lobbyId, ct: cancellationToken).ConfigureAwait(false);
                if (lobby.Status != "forming") return lobby;
                await Task.Delay(opts.Interval, cancellationToken).ConfigureAwait(false);
            }
            throw new TimeoutException($"UntilActiveAsync: lobby '{lobbyId}' did not leave 'forming' within {opts.Timeout}");
        }
    }
}
