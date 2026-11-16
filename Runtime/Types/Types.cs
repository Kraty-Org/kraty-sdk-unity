using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Kraty
{
    /// <summary>
    /// Public response types for the <c>/sdk/v1</c> surface, mirroring
    /// the OpenAPI spec at <c>apps/backend/openapi.json</c>. Plain DTOs;
    /// Newtonsoft.Json deserializes into them via the
    /// <see cref="JsonPropertyAttribute"/> hints.
    ///
    /// LocalizedString-ish fields (<c>name</c>, <c>description</c>) and
    /// free-form metadata bags are typed as <c>JToken</c> /
    /// <c>Dictionary&lt;string, JToken&gt;</c> so the consumer can
    /// inspect the shape without the SDK locking it down prematurely.
    /// </summary>

    public sealed class Attempt
    {
        [JsonProperty("id")] public string Id { get; set; } = string.Empty;
        [JsonProperty("eventId")] public string EventId { get; set; } = string.Empty;
        [JsonProperty("eventWindowId")] public string EventWindowId { get; set; } = string.Empty;
        [JsonProperty("leaderboardId")] public string LeaderboardId { get; set; } = string.Empty;
        [JsonProperty("playerId")] public string PlayerId { get; set; } = string.Empty;
        [JsonProperty("startedAt")] public string StartedAt { get; set; } = string.Empty;
        [JsonProperty("endsAt")] public string EndsAt { get; set; } = string.Empty;
        [JsonProperty("completedAt")] public string? CompletedAt { get; set; }
        [JsonProperty("metrics")] public Dictionary<string, double> Metrics { get; set; } = new();
        [JsonProperty("metricsRaw")] public Dictionary<string, double> MetricsRaw { get; set; } = new();
        [JsonProperty("score")] public double Score { get; set; }
        /// <summary>One of <c>in_progress</c>, <c>completed</c>, <c>expired</c>, <c>force_completed</c>.</summary>
        [JsonProperty("status")] public string Status { get; set; } = string.Empty;
    }

    public sealed class StartAttemptResponse
    {
        [JsonProperty("attempt")] public Attempt Attempt { get; set; } = new();
        [JsonProperty("leaderboardId")] public string LeaderboardId { get; set; } = string.Empty;
        [JsonProperty("windowEndsAt")] public string WindowEndsAt { get; set; } = string.Empty;
        /// <summary>Team events only: the player's team seats in this window. Null otherwise.</summary>
        [JsonProperty("teams")] public List<TeamSeatView>? Teams { get; set; }
    }

    public sealed class ProgressResult
    {
        [JsonProperty("attempt")] public Attempt Attempt { get; set; } = new();
        /// <summary>Team events only: the player's team seats after this write. Null otherwise.</summary>
        [JsonProperty("teams")] public List<TeamSeatView>? Teams { get; set; }

        /// <summary>
        /// Milestones whose threshold was crossed by THIS progress
        /// call. Always present (empty list when nothing fired) and never
        /// null, so callers can iterate without a null check. Each
        /// entry's <c>Grants</c> array carries the concrete payout
        /// rows in the same shape <c>GET /grants/pending</c> returns.
        /// </summary>
        [JsonProperty("milestonesFired")] public List<MilestoneFired> MilestonesFired { get; set; } = new();
    }

    /// <summary>
    /// Result of <c>Events.FinishAsync</c>: the finalized attempt plus how
    /// it resolved: "completed" (a score-attack end, or a target that was
    /// already met → completion rewards) or "expired" (a target event ended
    /// before its target, participation only).
    /// </summary>
    public sealed class FinishAttemptResult
    {
        [JsonProperty("attempt")] public Attempt Attempt { get; set; } = new();

        [JsonProperty("outcome")] public string Outcome { get; set; } = "completed";
        /// <summary>Team events only: the player's team seats. Null otherwise.</summary>
        [JsonProperty("teams")] public List<TeamSeatView>? Teams { get; set; }
    }

    /// <summary>
    /// A single milestone that fired during a <see cref="EventsClient.ProgressAsync"/>
    /// call. The <c>Key</c> is the designer-assigned id (use it to look
    /// up icon/copy/animation in your asset bundle). <c>Grants</c> is the
    /// concrete reward rows the engine wrote; render those directly or
    /// hand them to your existing grant-handling code.
    /// </summary>
    public sealed class MilestoneFired
    {
        [JsonProperty("key")] public string Key { get; set; } = string.Empty;
        [JsonProperty("grants")] public List<Grant> Grants { get; set; } = new();
    }

    /// <summary>
    /// One slot in an event's <c>entryCost.currencies</c> list, paid
    /// from the player's wallet on attempt start.
    /// </summary>
    public sealed class EntryCostCurrency
    {
        [JsonProperty("key")] public string Key { get; set; } = string.Empty;
        [JsonProperty("amount")] public int Amount { get; set; }
    }

    /// <summary>
    /// One slot in an event's <c>entryCost.items</c> list, consumed
    /// from inventory on attempt start.
    /// </summary>
    public sealed class EntryCostItem
    {
        [JsonProperty("key")] public string Key { get; set; } = string.Empty;
        [JsonProperty("quantity")] public int Quantity { get; set; }
    }

    /// <summary>
    /// Transactional cost paid on <see cref="EventsClient.StartAsync"/>.
    /// Server atomically debits + creates the attempt in a single tx, so
    /// partial debits never persist. Missing entry triggers a
    /// <see cref="KratyApiError"/> with code
    /// <c>insufficient_entry_cost</c>.
    /// </summary>
    public sealed class EntryCost
    {
        [JsonProperty("currencies")] public List<EntryCostCurrency> Currencies { get; set; } = new();
        [JsonProperty("items")] public List<EntryCostItem> Items { get; set; } = new();

        [JsonIgnore]
        public bool IsEmpty => (Currencies?.Count ?? 0) == 0 && (Items?.Count ?? 0) == 0;
    }

    /// <summary>
    /// One slot inside a reward bundle or milestone reward payload.
    /// Carries one of three shapes discriminated by <see cref="Type"/>:
    /// <c>currency</c>, <c>item</c>, or <c>crate</c>. Optional fields
    /// match whichever shape applies.
    /// </summary>
    public sealed class RewardEntryPreview
    {
        /// <summary>One of <c>currency</c> / <c>item</c> / <c>crate</c>.</summary>
        [JsonProperty("type")] public string Type { get; set; } = string.Empty;
        /// <summary>Set when <see cref="Type"/> is <c>currency</c>.</summary>
        [JsonProperty("currencyKey")] public string? CurrencyKey { get; set; }
        /// <summary>Set when <see cref="Type"/> is <c>currency</c>.</summary>
        [JsonProperty("amount")] public double? Amount { get; set; }
        /// <summary>Set when <see cref="Type"/> is <c>item</c>.</summary>
        [JsonProperty("itemKey")] public string? ItemKey { get; set; }
        /// <summary>Set when <see cref="Type"/> is <c>crate</c>.</summary>
        [JsonProperty("crateItemKey")] public string? CrateItemKey { get; set; }
        /// <summary>Set when <see cref="Type"/> is <c>item</c> or <c>crate</c>.</summary>
        [JsonProperty("quantity")] public int? Quantity { get; set; }
        /// <summary>Optional per-item parameters (used by <c>item</c> entries).</summary>
        [JsonProperty("parameters")] public Dictionary<string, JToken>? Parameters { get; set; }
    }

    /// <summary>
    /// Inline preview of a reward bundle's contents. Surfaced on
    /// <see cref="EventListing.RewardPolicy"/> so the studio can
    /// render "you'll win X" without resolving bundle IDs against a
    /// separate endpoint.
    /// </summary>
    public sealed class RewardBundlePreview
    {
        [JsonProperty("key")] public string Key { get; set; } = string.Empty;
        /// <summary>LocalizedString.</summary>
        [JsonProperty("name")] public JToken? Name { get; set; }
        [JsonProperty("entries")] public List<RewardEntryPreview> Entries { get; set; } = new();
    }

    /// <summary>
    /// One milestone reward that fires when the player crosses
    /// <see cref="Threshold"/> on <see cref="MetricKey"/> during a
    /// single attempt. Use this to render "next milestone: 5 rabbits
    /// → 200 cash + 5 bullets" in your UI.
    /// </summary>
    public sealed class MilestoneRewardPreview
    {
        [JsonProperty("key")] public string Key { get; set; } = string.Empty;
        [JsonProperty("metricKey")] public string MetricKey { get; set; } = string.Empty;
        [JsonProperty("threshold")] public double Threshold { get; set; }
        [JsonProperty("entries")] public List<RewardEntryPreview> Entries { get; set; } = new();
    }

    /// <summary>One tier in a <c>rank_scaled</c> reward policy.</summary>
    public sealed class RewardPolicyTier
    {
        [JsonProperty("fromRank")] public int FromRank { get; set; }
        [JsonProperty("toRank")] public int ToRank { get; set; }
        [JsonProperty("bundle")] public RewardBundlePreview? Bundle { get; set; }
    }

    /// <summary>
    /// Reward policy summary with inline bundle previews. Mirrors the
    /// four sealed policy types the backend supports:
    /// <list type="bullet">
    ///   <item><description><c>none</c>: event has no rewards.</description></item>
    ///   <item><description><c>fixed_bundle</c>: everyone who completes gets <see cref="Bundle"/>.</description></item>
    ///   <item><description><c>rank_scaled</c>: bundle picked by leaderboard rank; see <see cref="Tiers"/>.</description></item>
    ///   <item><description><c>shared_pool</c>: currency pool split among winners; see <see cref="Pool"/> and <see cref="CurrencyKey"/>.</description></item>
    /// </list>
    /// </summary>
    public sealed class RewardPolicySummary
    {
        [JsonProperty("type")] public string Type { get; set; } = "none";
        /// <summary>Set when <see cref="Type"/> is <c>fixed_bundle</c>.</summary>
        [JsonProperty("bundle")] public RewardBundlePreview? Bundle { get; set; }
        /// <summary>Set when <see cref="Type"/> is <c>rank_scaled</c>.</summary>
        [JsonProperty("tiers")] public List<RewardPolicyTier>? Tiers { get; set; }
        /// <summary>Set when <see cref="Type"/> is <c>shared_pool</c>.</summary>
        [JsonProperty("pool")] public double? Pool { get; set; }
        /// <summary>Set when <see cref="Type"/> is <c>shared_pool</c>.</summary>
        [JsonProperty("currencyKey")] public string? CurrencyKey { get; set; }
    }

    /// <summary>
    /// How much of the player's attempt allowance on this event window is
    /// spent, for an entry counter ("Play — 1/2 today").
    /// <para>
    /// The <c>Used*</c> counts include a live attempt: an entry is spent
    /// when it starts, not when it finishes. A counter is only populated
    /// when the matching cap is set — an uncapped event reports <c>0</c>
    /// used and a <c>null</c> maximum. <see cref="Remaining"/> is the
    /// tightest configured cap, <c>null</c> when nothing is capped.
    /// </para>
    /// </summary>
    public sealed class AttemptQuota
    {
        /// <summary>Attempts allowed for the whole window; null = unlimited.</summary>
        [JsonProperty("maxPerWindow")] public int? MaxPerWindow { get; set; }
        [JsonProperty("usedInWindow")] public int UsedInWindow { get; set; }
        /// <summary>Attempts allowed per local calendar day; null = unlimited.</summary>
        [JsonProperty("maxPerDay")] public int? MaxPerDay { get; set; }
        [JsonProperty("usedToday")] public int UsedToday { get; set; }
        /// <summary>What the player can still start now; null = unlimited.</summary>
        [JsonProperty("remaining")] public int? Remaining { get; set; }

        /// <summary>
        /// True when a cap is configured and fully spent, so the next
        /// <c>StartAsync</c> would be refused. Drives a greyed-out Play button.
        /// </summary>
        [JsonIgnore]
        public bool IsExhausted => Remaining == 0;
    }

    public sealed class EventListing
    {
        [JsonProperty("eventKey")] public string EventKey { get; set; } = string.Empty;
        /// <summary>
        /// LocalizedString (string or object); see backend docs/02 § English-only v1.
        /// Kept as <see cref="JToken"/> so the consumer can
        /// inspect the shape without the SDK locking it down prematurely.
        /// </summary>
        [JsonProperty("name")] public JToken? Name { get; set; }
        [JsonProperty("windowId")] public string WindowId { get; set; } = string.Empty;
        [JsonProperty("startsAt")] public string StartsAt { get; set; } = string.Empty;
        [JsonProperty("endsAt")] public string EndsAt { get; set; } = string.Empty;
        [JsonProperty("leaderboardId")] public string? LeaderboardId { get; set; }
        [JsonProperty("currentAttemptId")] public string? CurrentAttemptId { get; set; }

        /// <summary>
        /// <c>single_metric</c> / <c>multi_metric</c> (and any future
        /// event-type registry entries). Lets the SDK consumer pick
        /// the right UI without a hard-coded per-event catalog.
        /// </summary>
        [JsonProperty("type")] public string Type { get; set; } = "single_metric";

        /// <summary>
        /// <c>global</c> / <c>global_segmented</c> / <c>grouped</c> /
        /// <c>lobby_matched</c>. Combined with <see cref="LeaderboardId"/>
        /// tells the consumer whether to expect <c>lobby_forming</c>
        /// on <see cref="EventsClient.StartAsync"/>.
        /// </summary>
        [JsonProperty("leaderboardMode")] public string LeaderboardMode { get; set; } = "global";

        /// <summary>
        /// Free-form metric definitions from the event row (key,
        /// target, cap, scoreWeight, …); same shape the server stores.
        /// </summary>
        [JsonProperty("metrics")] public List<Dictionary<string, JToken>> Metrics { get; set; } = new();

        /// <summary>Player-condition tree; null when there's no join gate.</summary>
        [JsonProperty("entryRequirement")] public Dictionary<string, JToken>? EntryRequirement { get; set; }

        /// <summary>
        /// Cost paid on attempt start. <c>null</c> (and an empty
        /// <see cref="EntryCost"/>) both mean "free to enter".
        /// </summary>
        [JsonProperty("entryCost")] public EntryCost? EntryCost { get; set; }

        /// <summary>
        /// Studio-defined free-form blob. Event-level metadata merged
        /// with the active window's metadata (window keys win). Use
        /// for UI hints: banner image keys, theme colors,
        /// special-event copy.
        /// </summary>
        [JsonProperty("metadata")] public Dictionary<string, JToken> Metadata { get; set; } = new();

        /// <summary>Mid-attempt milestone rewards. Empty list when none configured.</summary>
        [JsonProperty("milestoneRewards")] public List<MilestoneRewardPreview> MilestoneRewards { get; set; } = new();

        /// <summary>Reward policy summary with inline bundle previews.</summary>
        [JsonProperty("rewardPolicy")] public RewardPolicySummary? RewardPolicy { get; set; }

        /// <summary>
        /// Attempts spent vs. allowed for this player. Null only on
        /// backends older than the attempt-quota field; a modern backend
        /// always sets it (null maxima when the event caps nothing).
        /// </summary>
        [JsonProperty("attemptQuota")] public AttemptQuota? AttemptQuota { get; set; }

        /// <summary>
        /// Score that ends a <c>first_to_score</c> session: the race's
        /// finish line. Draw progress toward it; <c>null</c> when the
        /// event has no such target.
        /// </summary>
        [JsonProperty("goal")] public int? Goal { get; set; }

        /// <summary>
        /// Player-condition tree that gates the event's visibility
        /// (<see cref="EntryRequirement"/> gates joining). The server
        /// enforces it on start; surfaced so the UI can show why an event
        /// is locked. <c>null</c> when there's no gate.
        /// </summary>
        [JsonProperty("unlockCondition")] public Dictionary<string, JToken>? UnlockCondition { get; set; }

        /// <summary>
        /// Length of one attempt in seconds. It's also cut short by the
        /// window end (<see cref="EndsAt"/>) and, for session events, by
        /// the session ending. <c>null</c> on backends older than the field.
        /// </summary>
        [JsonProperty("attemptDurationSeconds")] public int? AttemptDurationSeconds { get; set; }

        /// <summary>
        /// Team events only: the player's team seats in this window (score,
        /// goal / boss HP, milestones, your contribution and rank). Null for
        /// non-team events; empty while the player has no team.
        /// </summary>
        [JsonProperty("teams")] public List<TeamSeatView>? Teams { get; set; }

        /// <summary>
        /// Team events only: how teams form, i.e. what your UI does before
        /// the player scores for a team. <c>external</c> (your server posts
        /// rosters) and <c>attribute</c> (e.g. country) need nothing;
        /// <c>auto_match</c>: <see cref="EventsClient.MatchTeamAsync"/> (or just
        /// start); <c>partner_slots</c>: partner invites / random partner, then
        /// pass <see cref="ProgressInput.EventTeamId"/>.
        /// </summary>
        [JsonProperty("teamFormation")] public string? TeamFormation { get; set; }

        /// <summary>
        /// Team events only: the formation's player-facing options (partner
        /// cap, random partners, team size, the context keys a country team
        /// reads), so the UI offers the right setup before the player has a team.
        /// </summary>
        [JsonProperty("teamOptions")] public TeamClientOptions? TeamOptions { get; set; }

        /// <summary>
        /// True when <see cref="LeaderboardMode"/> is <c>lobby_matched</c>.
        /// Convenience flag for switching into a lobby-waiting view.
        /// </summary>
        [JsonIgnore]
        public bool IsLobbyMatched => LeaderboardMode == "lobby_matched";
    }

    /// <summary>
    /// One item row as exposed to game clients via the catalog
    /// endpoint. Display-relevant fields only; internal config and
    /// archival timestamps stay off the SDK wire format.
    /// </summary>
    public sealed class CatalogItem
    {
        [JsonProperty("key")] public string Key { get; set; } = string.Empty;
        /// <summary>LocalizedString.</summary>
        [JsonProperty("name")] public JToken? Name { get; set; }
        [JsonProperty("iconUrl")] public string? IconUrl { get; set; }
        /// <summary>LocalizedString.</summary>
        [JsonProperty("description")] public JToken? Description { get; set; }
        [JsonProperty("itemTypeKey")] public string ItemTypeKey { get; set; } = string.Empty;
        [JsonProperty("tags")] public List<string> Tags { get; set; } = new();
        [JsonProperty("rarity")] public string? Rarity { get; set; }
        [JsonProperty("attributes")] public Dictionary<string, JToken> Attributes { get; set; } = new();
    }

    /// <summary>
    /// One currency row as exposed to game clients. <see cref="Kind"/>
    /// distinguishes spendable currencies (<c>currency</c>) from
    /// progression resources (<c>progression</c>).
    /// </summary>
    public sealed class CatalogCurrency
    {
        [JsonProperty("key")] public string Key { get; set; } = string.Empty;
        /// <summary>LocalizedString.</summary>
        [JsonProperty("name")] public JToken? Name { get; set; }
        [JsonProperty("iconUrl")] public string? IconUrl { get; set; }
        /// <summary>LocalizedString.</summary>
        [JsonProperty("description")] public JToken? Description { get; set; }
        [JsonProperty("kind")] public string Kind { get; set; } = "currency";
        /// <summary>A derived progression item's curve (<c>level</c> from <c>xp</c>); null otherwise.</summary>
        [JsonProperty("derived")] public CatalogDerivedProgression? Derived { get; set; }
    }

    /// <summary>Each level of a derived item and the source balance that reaches it, lowest first.</summary>
    public sealed class CatalogDerivedProgression
    {
        [JsonProperty("sourceKey")] public string SourceKey { get; set; } = string.Empty;
        [JsonProperty("levels")] public List<CatalogDerivedLevel> Levels { get; set; } = new();
    }

    public sealed class CatalogDerivedLevel
    {
        [JsonProperty("level")] public int Level { get; set; }
        [JsonProperty("sourceRequired")] public long SourceRequired { get; set; }
    }

    public sealed class Catalog
    {
        [JsonProperty("items")] public List<CatalogItem> Items { get; set; } = new();
        [JsonProperty("currencies")] public List<CatalogCurrency> Currencies { get; set; } = new();
    }

    public sealed class LeaderboardEntry
    {
        [JsonProperty("participantId")] public string ParticipantId { get; set; } = string.Empty;
        /// <summary>One of <c>player</c>, <c>bot</c>.</summary>
        [JsonProperty("kind")] public string Kind { get; set; } = string.Empty;
        [JsonProperty("name")] public string? Name { get; set; }
        [JsonProperty("avatar")] public string? Avatar { get; set; }

        /// <summary>ISO-3166 alpha-2 country of the player (e.g. "PT") for a flag;
        /// null for bots and when the server couldn't resolve it.</summary>
        [JsonProperty("country")] public string? Country { get; set; }
        /// <summary>
        /// Immutable anonymized display name for players (the synthetic-pool
        /// value). <see cref="Name"/> holds the player's real display value
        /// (from <c>SetIdentity</c>) with a fallback to the pool value; this
        /// field carries the pool value directly so games can render a
        /// privacy-preserving view (public boards, cross-game aggregations)
        /// without a second lookup. Null on bot entries and on rows that
        /// predate the split feature.
        /// </summary>
        [JsonProperty("anonymizedName")] public string? AnonymizedName { get; set; }
        /// <summary>
        /// Per-player progression balances requested via
        /// <c>Progression</c> on the read options, e.g.
        /// <c>{ "level": 12 }</c>. Null when none were requested; a resource
        /// the player never earned reads <c>0</c>. Never set on bot rows.
        /// </summary>
        [JsonProperty("progression")] public Dictionary<string, int>? Progression { get; set; }
        /// <summary>Matching avatar reference for <see cref="AnonymizedName"/>.</summary>
        [JsonProperty("anonymizedAvatar")] public string? AnonymizedAvatar { get; set; }
        [JsonProperty("score")] public double Score { get; set; }
        [JsonProperty("rank")] public int Rank { get; set; }
        /// <summary>
        /// <c>true</c> when this entry is the player calling the API
        /// (resolved from the <c>externalId</c> passed via
        /// <c>includeSelf</c>). Highlight rows off this rather than
        /// matching <c>ParticipantId</c> to the external id yourself,
        /// since the server surfaces the internal player UUID, not the
        /// external one. Always <c>false</c> on entries without a
        /// self-context request, and on bot entries regardless.
        /// </summary>
        [JsonProperty("isSelf")] public bool IsSelf { get; set; }
        /// <summary>
        /// Team boards only: the event team behind this row (rows are
        /// <c>kind: player</c>, rival bot teams <c>bot</c> with a null team).
        /// </summary>
        [JsonProperty("team")] public LeaderboardEntryTeam? Team { get; set; }
    }

    /// <summary>The event team behind a team-board row.</summary>
    public sealed class LeaderboardEntryTeam
    {
        [JsonProperty("eventTeamId")] public string EventTeamId { get; set; } = string.Empty;
        [JsonProperty("badge")] public string? Badge { get; set; }
        [JsonProperty("memberCount")] public int MemberCount { get; set; }
    }

    public sealed class LeaderboardSelf
    {
        [JsonProperty("rank")] public int Rank { get; set; }
        [JsonProperty("score")] public double Score { get; set; }
    }

    /// <summary>
    /// One side of a promotion/relegation ladder: the <c>Places</c> positions
    /// (from the top for promotion, the bottom for relegation) that move, and
    /// the progression <c>ItemKey</c> they nudge.
    /// </summary>
    public sealed class ProgressionBand
    {
        /// <summary>How many positions on this end move (e.g. top 3 promote).</summary>
        [JsonProperty("places")] public int Places { get; set; }
        /// <summary>The progression item climbed / dropped.</summary>
        [JsonProperty("itemKey")] public string ItemKey { get; set; } = string.Empty;
    }

    /// <summary>
    /// Promotion / relegation limits for the caller's division, resolved from
    /// server config (a per-division override wins over the global ladder).
    /// Render "top N promote / bottom N relegate" from this instead of
    /// hardcoding the cutoffs. Each side is <c>null</c> when no ladder runs on
    /// that end.
    /// </summary>
    public sealed class ProgressionLimits
    {
        [JsonProperty("promotion")] public ProgressionBand? Promotion { get; set; }
        [JsonProperty("relegation")] public ProgressionBand? Relegation { get; set; }
    }

    /// <summary>
    /// Response from <see cref="LeaderboardsClient.ReadAsync"/>: a
    /// configurable, cross-event leaderboard addressed by its game-scoped
    /// <c>key</c> (e.g. <c>"weekly_global"</c>). This is what most games
    /// render; the auto-created per-event-window board is
    /// <see cref="EventLeaderboard"/>.
    /// </summary>
    public sealed class Leaderboard
    {
        /// <summary>The board's stable game-scoped key (e.g. <c>"weekly_global"</c>).</summary>
        [JsonProperty("key")] public string Key { get; set; } = string.Empty;
        /// <summary>UUID of the leaderboard config row.</summary>
        [JsonProperty("leaderboardId")] public string LeaderboardId { get; set; } = string.Empty;
        /// <summary>One of <c>global</c>, <c>regional</c>, etc.; set in the board's config.</summary>
        [JsonProperty("scope")] public string? Scope { get; set; }
        /// <summary>One of <c>daily</c>, <c>weekly</c>, <c>monthly</c>, <c>never</c>.</summary>
        [JsonProperty("resetCadence")] public string? ResetCadence { get; set; }
        /// <summary>How concurrent scores combine: <c>best</c>, <c>sum</c>, <c>last</c>.</summary>
        [JsonProperty("scoreAggregation")] public string? ScoreAggregation { get; set; }
        /// <summary>Resolved segment bucket for segmented boards; <c>null</c> on unsegmented boards.</summary>
        [JsonProperty("segment")] public string? Segment { get; set; }
        /// <summary>ISO timestamp of the period this read snapshot is from. <c>"current"</c> reads return the live period.</summary>
        [JsonProperty("period")] public string Period { get; set; } = string.Empty;
        [JsonProperty("entries")] public List<LeaderboardEntry> Entries { get; set; } = new();
        [JsonProperty("self")] public LeaderboardSelf? Self { get; set; }
        /// <summary>Promotion/relegation cutoffs for this division, from server
        /// config; <c>null</c> when the board runs no ladder.</summary>
        [JsonProperty("progression")] public ProgressionLimits? Progression { get; set; }
        /// <summary>
        /// <c>true</c> on the response to
        /// <see cref="LeaderboardsClient.JoinAsync"/>; <c>false</c> on plain
        /// reads (the backend omits the field there).
        /// </summary>
        [JsonProperty("joined")] public bool Joined { get; set; }
    }

    /// <summary>
    /// One entry of <see cref="LeaderboardsClient.ListAsync"/>: a
    /// dashboard-configured board the game can show, so clients discover
    /// board keys instead of hard-coding them.
    /// </summary>
    public sealed class LeaderboardSummary
    {
        /// <summary>The board's stable game-scoped key; pass it to <see cref="LeaderboardsClient.ReadAsync"/>.</summary>
        [JsonProperty("key")] public string Key { get; set; } = string.Empty;
        /// <summary>LocalizedString (string or object); see backend docs/02 § English-only v1.</summary>
        [JsonProperty("name")] public JToken? Name { get; set; }
        [JsonProperty("description")] public string? Description { get; set; }
        /// <summary>One of <c>daily</c>, <c>weekly</c>, <c>monthly</c>, <c>never</c>.</summary>
        [JsonProperty("resetCadence")] public string ResetCadence { get; set; } = string.Empty;
        /// <summary>ISO timestamp the current period ends; <c>null</c> when the board never resets.</summary>
        [JsonProperty("nextResetAt")] public string? NextResetAt { get; set; }
        /// <summary><c>false</c> when only the studio's server may submit scores to this board.</summary>
        [JsonProperty("acceptClientScores")] public bool AcceptClientScores { get; set; }
        /// <summary>
        /// <c>playerContext</c> fields that segment the board; pass one's
        /// value as the segment on read / join / score. Progression
        /// segments are resolved server-side and aren't listed.
        /// </summary>
        [JsonProperty("contextSegments")] public List<string> ContextSegments { get; set; } = new();
    }

    /// <summary>
    /// One snapshot period on a leaderboard. Returned by
    /// <see cref="LeaderboardsClient.ListPeriodsAsync"/>; pass
    /// <see cref="PeriodStartedAt"/> as <c>opts.Period</c> on
    /// <see cref="LeaderboardsClient.ReadAsync"/> to read that period's
    /// snapshot.
    /// </summary>
    public sealed class LeaderboardPeriod
    {
        [JsonProperty("periodStartedAt")] public string PeriodStartedAt { get; set; } = string.Empty;
        [JsonProperty("periodEndedAt")] public string PeriodEndedAt { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response from <see cref="LeaderboardsClient.ListPeriodsAsync"/>: the
    /// board's identity plus a newest-first list of finalized periods.
    /// Useful for "last week's top 10" UI selectors.
    /// </summary>
    public sealed class LeaderboardPeriods
    {
        [JsonProperty("key")] public string Key { get; set; } = string.Empty;
        [JsonProperty("leaderboardId")] public string LeaderboardId { get; set; } = string.Empty;
        [JsonProperty("currentPeriodStartedAt")] public string CurrentPeriodStartedAt { get; set; } = string.Empty;
        [JsonProperty("periods")] public List<LeaderboardPeriod> Periods { get; set; } = new();
    }

    /// <summary>
    /// Response from <see cref="EventLeaderboardsClient.ReadAsync"/>: the
    /// auto-generated per-event-window leaderboard, addressed by the UUID
    /// returned in <c>Events.StartAsync(...)</c>'s
    /// <c>attempt.LeaderboardId</c>. For most game UI you want
    /// <see cref="Leaderboard"/> (the dashboard-configured board) instead.
    /// </summary>
    public sealed class EventLeaderboard
    {
        [JsonProperty("leaderboardId")] public string LeaderboardId { get; set; } = string.Empty;
        [JsonProperty("mode")] public string Mode { get; set; } = string.Empty;
        /// <summary><c>player</c>, or <c>team</c> on a co-op team board (rows are teams; <c>Self</c> is your team).</summary>
        [JsonProperty("participantScope")] public string? ParticipantScope { get; set; }
        [JsonProperty("finalized")] public bool Finalized { get; set; }
        /// <summary>Why the board finalized; one of the
        /// <see cref="FinalizationReason"/> constants
        /// (<c>session_terminated</c> vs <c>window_closed</c>), or null while
        /// the board is still live.</summary>
        [JsonProperty("finalizedReason")] public string? FinalizedReason { get; set; }
        [JsonProperty("entries")] public List<LeaderboardEntry> Entries { get; set; } = new();
        [JsonProperty("self")] public LeaderboardSelf? Self { get; set; }
        /// <summary>Promotion/relegation cutoffs for this division, from server
        /// config; <c>null</c> when the event runs no ladder.</summary>
        [JsonProperty("progression")] public ProgressionLimits? Progression { get; set; }
        /// <summary>
        /// <c>true</c> on the response to
        /// <see cref="EventLeaderboardsClient.JoinAsync"/>; <c>false</c> on
        /// plain reads (the backend omits the field there).
        /// </summary>
        [JsonProperty("joined")] public bool Joined { get; set; }
    }

    /// <summary>
    /// One segment block in a <see cref="BoardStandings"/> response.
    /// </summary>
    public sealed class StandingsSegment
    {
        /// <summary>Bucket value; <c>null</c> on unsegmented boards.</summary>
        [JsonProperty("segment")] public string? Segment { get; set; }
        /// <summary><c>true</c> when the caller appears in this segment for the period.</summary>
        [JsonProperty("participated")] public bool Participated { get; set; }
        /// <summary>The caller's rank within this segment, or <c>null</c> if not present.</summary>
        [JsonProperty("selfRank")] public int? SelfRank { get; set; }
        [JsonProperty("entries")] public List<LeaderboardEntry> Entries { get; set; } = new();
    }

    /// <summary>
    /// Response from <see cref="LeaderboardsClient.StandingsAsync"/>:
    /// flexible multi-segment standings, the versatile counterpart to
    /// <see cref="Leaderboard"/>. Returns one <see cref="StandingsSegment"/>
    /// block per segment selected by <c>scope</c>, live or for a past
    /// period.
    /// </summary>
    public sealed class BoardStandings
    {
        /// <summary>The board's stable game-scoped key (e.g. <c>"weekly_global"</c>).</summary>
        [JsonProperty("key")] public string Key { get; set; } = string.Empty;
        /// <summary>UUID of the leaderboard config row.</summary>
        [JsonProperty("leaderboardId")] public string LeaderboardId { get; set; } = string.Empty;
        /// <summary>One of <c>game</c>, <c>studio</c>, etc.; set in the board's config.</summary>
        [JsonProperty("scope")] public string Scope { get; set; } = string.Empty;
        /// <summary>One of <c>never</c>, <c>weekly</c>, <c>monthly</c>.</summary>
        [JsonProperty("resetCadence")] public string ResetCadence { get; set; } = string.Empty;
        /// <summary>How concurrent scores combine: <c>best</c>, <c>latest</c>, <c>sum</c>.</summary>
        [JsonProperty("scoreAggregation")] public string ScoreAggregation { get; set; } = string.Empty;
        /// <summary>ISO timestamp of the period these standings are from.</summary>
        [JsonProperty("period")] public string Period { get; set; } = string.Empty;
        [JsonProperty("segments")] public List<StandingsSegment> Segments { get; set; } = new();
        /// <summary><c>true</c> when more segments existed than <c>maxSegments</c> returned.</summary>
        [JsonProperty("segmentsTruncated")] public bool SegmentsTruncated { get; set; }
    }

    public sealed class Grant
    {
        [JsonProperty("id")] public string Id { get; set; } = string.Empty;
        /// <summary>One of <c>reward</c>, <c>crate</c>.</summary>
        [JsonProperty("kind")] public string Kind { get; set; } = string.Empty;
        [JsonProperty("contents")] public Dictionary<string, JToken> Contents { get; set; } = new();
        [JsonProperty("sourceKind")] public string SourceKind { get; set; } = string.Empty;
        [JsonProperty("sourceRefId")] public string? SourceRefId { get; set; }
        [JsonProperty("parentGrantId")] public string? ParentGrantId { get; set; }
        /// <summary>One of <c>pending</c>, <c>claimed</c>, <c>expired</c>.</summary>
        [JsonProperty("status")] public string Status { get; set; } = string.Empty;
        [JsonProperty("rolledAt")] public string? RolledAt { get; set; }
        [JsonProperty("claimedAt")] public string? ClaimedAt { get; set; }
        [JsonProperty("expiresAt")] public string? ExpiresAt { get; set; }
        [JsonProperty("createdAt")] public string CreatedAt { get; set; } = string.Empty;
    }

    public sealed class OpenCrateResponse
    {
        [JsonProperty("crate")] public Grant Crate { get; set; } = new();
        [JsonProperty("contents")] public Grant Contents { get; set; } = new();
    }

    /// <summary>
    /// One line of a client-initiated grant (see
    /// <see cref="GrantsClient.GrantAsync"/>). Set <see cref="Type"/> to
    /// <c>"currency"</c> (+ <see cref="CurrencyKey"/> / <see cref="Amount"/>),
    /// <c>"item"</c> (+ <see cref="ItemKey"/> / <see cref="Quantity"/>), or
    /// <c>"crate"</c> (+ <see cref="CrateItemKey"/> / <see cref="Quantity"/>).
    /// </summary>
    public sealed class GrantEntry
    {
        [JsonProperty("type")] public string Type { get; set; } = "item";
        [JsonProperty("currencyKey", NullValueHandling = NullValueHandling.Ignore)] public string? CurrencyKey { get; set; }
        [JsonProperty("amount", NullValueHandling = NullValueHandling.Ignore)] public double? Amount { get; set; }
        [JsonProperty("itemKey", NullValueHandling = NullValueHandling.Ignore)] public string? ItemKey { get; set; }
        [JsonProperty("crateItemKey", NullValueHandling = NullValueHandling.Ignore)] public string? CrateItemKey { get; set; }
        [JsonProperty("quantity", NullValueHandling = NullValueHandling.Ignore)] public int? Quantity { get; set; }
        [JsonProperty("parameters", NullValueHandling = NullValueHandling.Ignore)] public Dictionary<string, object?>? Parameters { get; set; }
    }

    /// <summary>
    /// Body of a client-initiated grant. Self-only (you can only grant to the
    /// active player); requires the game's inventory management to be
    /// <c>permissive</c>.
    /// </summary>
    public sealed class GrantSelfInput
    {
        [JsonProperty("entries")] public List<GrantEntry> Entries { get; set; } = new();
        /// <summary>Defaults to <c>"reward"</c> server-side; use <c>"crate"</c> for a crate grant.</summary>
        [JsonProperty("kind", NullValueHandling = NullValueHandling.Ignore)] public string? Kind { get; set; }
        [JsonProperty("expiresAt", NullValueHandling = NullValueHandling.Ignore)] public string? ExpiresAt { get; set; }
        [JsonProperty("metadata", NullValueHandling = NullValueHandling.Ignore)] public Dictionary<string, object?>? Metadata { get; set; }
        [JsonProperty("idempotencyKey", NullValueHandling = NullValueHandling.Ignore)] public string? IdempotencyKey { get; set; }
    }

    public sealed class Lobby
    {
        [JsonProperty("id")] public string Id { get; set; } = string.Empty;
        [JsonProperty("eventId")] public string EventId { get; set; } = string.Empty;
        [JsonProperty("eventWindowId")] public string EventWindowId { get; set; } = string.Empty;
        [JsonProperty("leaderboardId")] public string? LeaderboardId { get; set; }
        [JsonProperty("mode")] public string Mode { get; set; } = string.Empty;
        /// <summary>One of <c>forming</c>, <c>active</c>, <c>closed</c>.</summary>
        [JsonProperty("status")] public string Status { get; set; } = string.Empty;
        [JsonProperty("capacity")] public int Capacity { get; set; }
        [JsonProperty("fillBy")] public string? FillBy { get; set; }
        [JsonProperty("participantCount")] public int ParticipantCount { get; set; }

        /// <summary>
        /// Projected bot count at read time, derived server-side from
        /// the lobby's age and the matchmaking drip interval. Grows
        /// monotonically while the lobby is <c>forming</c>. UI typically
        /// renders <c>ParticipantCount + BotSlots</c> filled cells out
        /// of <c>Capacity</c> for a smooth fill animation.
        /// </summary>
        [JsonProperty("botSlots")] public int BotSlots { get; set; }

        [JsonProperty("startedAt")] public string? StartedAt { get; set; }
        [JsonProperty("endsAt")] public string? EndsAt { get; set; }

        /// <summary>
        /// Total filled cells (humans + projected bots). Capped at
        /// capacity in case server / client clocks ever disagree
        /// mid-drip.
        /// </summary>
        [JsonIgnore]
        public int FilledSlots
        {
            get
            {
                var total = ParticipantCount + BotSlots;
                return total > Capacity ? Capacity : total;
            }
        }
    }

    /// <summary>
    /// Input for <see cref="EventsClient.ProgressAsync"/>. Supply either
    /// <see cref="MetricValue"/> (single-metric events) or
    /// <see cref="Metrics"/> (multi-metric). <see cref="IdempotencyKey"/>
    /// is auto-generated by the client if you leave it null.
    /// </summary>
    public sealed class ProgressInput
    {
        /// <summary><c>set</c> writes the value as the new metric; <c>increment</c> adds.</summary>
        [JsonProperty("mode")] public string Mode { get; set; } = "set";
        [JsonProperty("metricValue")] public double? MetricValue { get; set; }
        [JsonProperty("metrics")] public Dictionary<string, double>? Metrics { get; set; }
        [JsonProperty("occurredAt")] public string? OccurredAt { get; set; }
        [JsonProperty("idempotencyKey")] public string? IdempotencyKey { get; set; }
        /// <summary>
        /// Team events: which of the player's teams this progress counts for.
        /// Needed only when the player holds several seats and the event
        /// routes to a chosen team (partner bars).
        /// </summary>
        [JsonProperty("eventTeamId", NullValueHandling = NullValueHandling.Ignore)] public string? EventTeamId { get; set; }
    }

    // ── Co-op team events ─────────────────────────────────────────────

    /// <summary><see cref="EventListing.TeamOptions"/>: what a client needs to get a player onto a team.</summary>
    public sealed class TeamClientOptions
    {
        [JsonProperty("maxMembers")] public int MaxMembers { get; set; }
        /// <summary><c>auto_match</c>: players per matched team (2 on partner events); null otherwise.</summary>
        [JsonProperty("teamSize")] public int? TeamSize { get; set; }
        /// <summary><c>partner_slots</c>: partner bars a player may hold; null otherwise.</summary>
        [JsonProperty("maxPartners")] public int? MaxPartners { get; set; }
        /// <summary><c>partner_slots</c>: whether a random partner is offered; null otherwise.</summary>
        [JsonProperty("allowRandom")] public bool? AllowRandom { get; set; }
        /// <summary><c>attribute</c>: player-context keys the team is keyed by. The server-resolved <c>country</c> is never listed.</summary>
        [JsonProperty("contextKeys")] public List<string> ContextKeys { get; set; } = new();
    }

    /// <summary>One of the player's team seats, as the events listing / start / progress / finish return it.</summary>
    public sealed class TeamSeatView
    {
        [JsonProperty("eventTeamId")] public string EventTeamId { get; set; } = string.Empty;
        [JsonProperty("teamKey")] public string TeamKey { get; set; } = string.Empty;
        [JsonProperty("displayName")] public string DisplayName { get; set; } = string.Empty;
        [JsonProperty("badge")] public string? Badge { get; set; }
        [JsonProperty("formation")] public string Formation { get; set; } = string.Empty;
        /// <summary><c>forming</c> (partner invite pending) / <c>active</c> / <c>closed</c>.</summary>
        [JsonProperty("status")] public string Status { get; set; } = "active";
        [JsonProperty("score")] public double Score { get; set; }
        [JsonProperty("memberCount")] public int MemberCount { get; set; }
        /// <summary>Null when the event has no team goal. <c>Remaining</c> is the boss HP.</summary>
        [JsonProperty("goal")] public TeamGoal? Goal { get; set; }
        [JsonProperty("milestones")] public List<TeamSeatMilestone> Milestones { get; set; } = new();
        [JsonProperty("seat")] public TeamSeatState Seat { get; set; } = new();
        /// <summary>The team board, once the team entered one.</summary>
        [JsonProperty("leaderboardId")] public string? LeaderboardId { get; set; }
        /// <summary>Your team's place on that board: live during the window, final after close.</summary>
        [JsonProperty("boardRank")] public int? BoardRank { get; set; }
        /// <summary>Partner events: your bar over all your pairs and its checkpoints.</summary>
        [JsonProperty("rollup")] public TeamRollup? Rollup { get; set; }
    }

    public sealed class TeamGoal
    {
        [JsonProperty("target")] public double Target { get; set; }
        [JsonProperty("remaining")] public double Remaining { get; set; }
        [JsonProperty("completed")] public bool Completed { get; set; }
    }

    public sealed class TeamSeatMilestone
    {
        [JsonProperty("key")] public string Key { get; set; } = string.Empty;
        /// <summary>After goal scaling.</summary>
        [JsonProperty("threshold")] public double Threshold { get; set; }
        [JsonProperty("unlocked")] public bool Unlocked { get; set; }
        /// <summary>You were paid this tier.</summary>
        [JsonProperty("paid")] public bool Paid { get; set; }
        /// <summary>Unlocked while you were on the team; pays once your own contribution reaches the minimum.</summary>
        [JsonProperty("pendingMinContribution")] public bool PendingMinContribution { get; set; }
    }

    public sealed class TeamSeatState
    {
        [JsonProperty("seatId")] public string SeatId { get; set; } = string.Empty;
        /// <summary><c>invited</c> / <c>active</c> / <c>left</c> / <c>removed</c>.</summary>
        [JsonProperty("status")] public string Status { get; set; } = "active";
        [JsonProperty("contribution")] public double Contribution { get; set; }
        /// <summary>Your 1-based rank inside the team.</summary>
        [JsonProperty("rank")] public int Rank { get; set; }
        [JsonProperty("locked")] public bool Locked { get; set; }
    }

    public sealed class TeamRollup
    {
        [JsonProperty("value")] public double Value { get; set; }
        [JsonProperty("milestones")] public List<TeamRollupMilestone> Milestones { get; set; } = new();
    }

    public sealed class TeamRollupMilestone
    {
        [JsonProperty("key")] public string Key { get; set; } = string.Empty;
        [JsonProperty("threshold")] public double Threshold { get; set; }
        [JsonProperty("reached")] public bool Reached { get; set; }
        [JsonProperty("paid")] public bool Paid { get; set; }
    }

    /// <summary>One teammate, as <see cref="EventsClient.TeamMembersAsync"/> returns them.</summary>
    public sealed class EventTeamMember
    {
        [JsonProperty("externalPlayerId")] public string? ExternalPlayerId { get; set; }
        [JsonProperty("name")] public string? Name { get; set; }
        [JsonProperty("avatar")] public string? Avatar { get; set; }
        [JsonProperty("contribution")] public double Contribution { get; set; }
        [JsonProperty("rank")] public int Rank { get; set; }
        [JsonProperty("status")] public string Status { get; set; } = "active";
        [JsonProperty("isSelf")] public bool IsSelf { get; set; }
    }

    public sealed class EventTeamMembersPage
    {
        [JsonProperty("eventTeamId")] public string EventTeamId { get; set; } = string.Empty;
        [JsonProperty("teamKey")] public string TeamKey { get; set; } = string.Empty;
        [JsonProperty("memberCount")] public int MemberCount { get; set; }
        [JsonProperty("members")] public List<EventTeamMember> Members { get; set; } = new();
        /// <summary>Pass back as <c>cursor</c> for the next page; null on the last page.</summary>
        [JsonProperty("nextCursor")] public string? NextCursor { get; set; }
    }

    /// <summary><see cref="EventsClient.MatchTeamAsync"/>: your auto-matched team.</summary>
    public sealed class TeamMatchResult
    {
        [JsonProperty("windowId")] public string WindowId { get; set; } = string.Empty;
        [JsonProperty("eventTeamId")] public string EventTeamId { get; set; } = string.Empty;
        [JsonProperty("created")] public bool Created { get; set; }
        [JsonProperty("teams")] public List<TeamSeatView> Teams { get; set; } = new();
    }

    public sealed class PartnerPlayer
    {
        [JsonProperty("externalPlayerId")] public string ExternalPlayerId { get; set; } = string.Empty;
        [JsonProperty("displayIdentity")] public PlayerIdentity? DisplayIdentity { get; set; }
    }

    /// <summary>A partner invite (its id is the pair's <c>EventTeamId</c>).</summary>
    public sealed class PartnerInvite
    {
        [JsonProperty("eventTeamId")] public string EventTeamId { get; set; } = string.Empty;
        /// <summary><c>pending</c> / <c>accepted</c> / <c>declined</c> / <c>cancelled</c>.</summary>
        [JsonProperty("status")] public string Status { get; set; } = "pending";
        [JsonProperty("from")] public PartnerPlayer From { get; set; } = new();
        [JsonProperty("to")] public PartnerPlayer To { get; set; } = new();
        [JsonProperty("expiresAt")] public string? ExpiresAt { get; set; }
        [JsonProperty("createdAt")] public string CreatedAt { get; set; } = string.Empty;
        /// <summary>Set on an accept: your seats after it.</summary>
        [JsonProperty("teams")] public List<TeamSeatView>? Teams { get; set; }
    }

    /// <summary><see cref="EventsClient.RandomPartnerAsync"/>: paired with someone waiting, or your bar waits.</summary>
    public sealed class RandomPartnerResult
    {
        /// <summary><c>paired</c> or <c>waiting</c>.</summary>
        [JsonProperty("status")] public string Status { get; set; } = "waiting";
        [JsonProperty("eventTeamId")] public string EventTeamId { get; set; } = string.Empty;
        [JsonProperty("teams")] public List<TeamSeatView> Teams { get; set; } = new();
    }

    public sealed class PartnerSuggestion
    {
        [JsonProperty("externalPlayerId")] public string ExternalPlayerId { get; set; } = string.Empty;
        [JsonProperty("displayIdentity")] public PlayerIdentity? DisplayIdentity { get; set; }
        /// <summary>An accepted friend (friends are listed first).</summary>
        [JsonProperty("isFriend")] public bool IsFriend { get; set; }
    }

    /// <summary>
    /// Per-call options for <see cref="LeaderboardsClient.ReadAsync"/>.
    /// </summary>
    public sealed class LeaderboardReadOptions
    {
        /// <summary>1–200, default 50 server-side.</summary>
        public int? Limit { get; set; }
        /// <summary>
        /// Progression economy keys to attach to each player entry (max 8),
        /// e.g. <c>new[] { "level" }</c>. Omit and nothing extra is read
        /// server-side.
        /// </summary>
        public IEnumerable<string>? Progression { get; set; }

        /// <summary>
        /// Bucket value for segmented boards. Required only for
        /// <c>context</c> segmentation: pass the same value your SDK
        /// supplied in <c>playerContext[segmentation.key]</c> when
        /// starting the contributing attempt. For
        /// <c>progression</c>-segmented boards leave this null: the
        /// server derives the caller's division. Unsegmented boards
        /// ignore it.
        /// </summary>
        public string? Segment { get; set; }
        /// <summary>
        /// <c>"current"</c> (default) reads the live period off Redis; an ISO
        /// timestamp (typically one of
        /// <see cref="LeaderboardPeriod.PeriodStartedAt"/>) reads the
        /// snapshotted final ranks for that period.
        /// </summary>
        public string? Period { get; set; }
        /// <summary>When true, response includes <c>self: { rank, score }</c> (live periods only).</summary>
        public bool IncludeSelf { get; set; }
        /// <summary>Required when <see cref="IncludeSelf"/> is true.</summary>
        public string? ExternalId { get; set; }
    }

    /// <summary>
    /// Per-call options for <see cref="LeaderboardsClient.JoinAsync"/>.
    /// </summary>
    public sealed class LeaderboardJoinOptions
    {
        /// <summary>
        /// Bucket value for segmented boards. Required only for
        /// <c>context</c> segmentation. For <c>progression</c>-segmented
        /// boards leave this null; the server derives the player's
        /// division. Unsegmented boards ignore it.
        /// </summary>
        public string? Segment { get; set; }
        /// <summary>1–200, default 50 server-side.</summary>
        public int? Limit { get; set; }
        /// <summary>
        /// Override the active player (server-side tooling only). Leave
        /// null to target the SDK's active identity.
        /// </summary>
        public string? ExternalId { get; set; }
    }

    /// <summary>
    /// Per-call options for <see cref="LeaderboardsClient.StandingsAsync"/>.
    /// </summary>
    public sealed class StandingsReadOptions
    {
        /// <summary>
        /// Progression economy keys to attach to each player entry (max 8),
        /// e.g. <c>new[] { "level" }</c>. Omit and nothing extra is read
        /// server-side.
        /// </summary>
        public IEnumerable<string>? Progression { get; set; }

        /// <summary>
        /// Which segments to return (default <c>"all"</c>):
        /// <list type="bullet">
        ///   <item><description><c>"self_segment"</c>: the caller's single home segment.</description></item>
        ///   <item><description><c>"mine"</c>: every segment the caller appears in.</description></item>
        ///   <item><description><c>"segment"</c>: the one named in <see cref="Segment"/>.</description></item>
        ///   <item><description><c>"all"</c>: every segment for the period.</description></item>
        /// </list>
        /// <c>self_segment</c>/<c>mine</c> resolve the caller from
        /// <see cref="ExternalId"/> (or the SDK's active identity).
        /// </summary>
        public string? Scope { get; set; }
        /// <summary>Required when <see cref="Scope"/> is <c>"segment"</c> on a segmented board.</summary>
        public string? Segment { get; set; }
        /// <summary><c>"current"</c> (default) or an ISO period timestamp from <see cref="LeaderboardsClient.ListPeriodsAsync"/>.</summary>
        public string? Period { get; set; }
        /// <summary>Flags <c>isSelf</c>/<c>selfRank</c>; auto-resolved for <c>self_segment</c>/<c>mine</c>.</summary>
        public string? ExternalId { get; set; }
        /// <summary>Per-segment top-N (1–200, default 50 server-side).</summary>
        public int? Limit { get; set; }
        /// <summary>Cap on returned segment blocks (1–100, default 20 server-side).</summary>
        public int? MaxSegments { get; set; }
    }

    /// <summary>
    /// Per-call options for <see cref="EventLeaderboardsClient.ReadAsync"/>.
    /// </summary>
    public sealed class EventLeaderboardReadOptions
    {
        /// <summary>1–200, default 50 server-side.</summary>
        public int? Limit { get; set; }
        /// <summary>When true, response includes <c>self: { rank, score }</c>.</summary>
        public bool IncludeSelf { get; set; }
        /// <summary>Required when <see cref="IncludeSelf"/> is true.</summary>
        public string? ExternalId { get; set; }
        /// <summary>
        /// Progression economy keys to attach to each player entry (max 8),
        /// e.g. <c>new[] { "level" }</c>. Omit and nothing extra is read
        /// server-side.
        /// </summary>
        public IEnumerable<string>? Progression { get; set; }
    }

    /// <summary>
    /// Per-call options for <see cref="EventLeaderboardsClient.JoinAsync"/>.
    /// </summary>
    public sealed class EventLeaderboardJoinOptions
    {
        /// <summary>1–200, default 50 server-side.</summary>
        public int? Limit { get; set; }
        /// <summary>
        /// Override the active player (server-side tooling only). Leave
        /// null to target the SDK's active identity.
        /// </summary>
        public string? ExternalId { get; set; }
    }

    /// <summary>
    /// Result of <see cref="LeaderboardsClient.SubmitScoreAsync"/>: the
    /// player's new standing on the board after the write.
    /// <see cref="Rank"/> is <c>null</c> when the board can't resolve a
    /// position for the caller yet (e.g. the period just rolled over,
    /// or the score didn't beat a <c>best</c>-aggregation board's
    /// existing entry).
    /// </summary>
    public sealed class LeaderboardScoreResult
    {
        /// <summary>UUID of the leaderboard config row the score landed on.</summary>
        [JsonProperty("leaderboardId")] public string LeaderboardId { get; set; } = string.Empty;
        /// <summary>The score now recorded for the player on this board.</summary>
        [JsonProperty("score")] public double Score { get; set; }
        /// <summary>The player's rank after the write, or <c>null</c> if not yet ranked.</summary>
        [JsonProperty("rank")] public int? Rank { get; set; }
    }

    /// <summary>
    /// Per-call options for <see cref="LeaderboardsClient.SubmitScoreAsync"/>.
    /// </summary>
    public sealed class LeaderboardSubmitOptions
    {
        /// <summary>
        /// Bucket value for segmented boards. Required only for
        /// <c>context</c> segmentation. For <c>progression</c>-segmented
        /// boards leave this null; the server derives the player's
        /// division. Unsegmented boards ignore it.
        /// </summary>
        public string? Segment { get; set; }
        /// <summary>Optional override; leave null to let the client auto-stamp one.</summary>
        public string? IdempotencyKey { get; set; }
        /// <summary>
        /// Override the active player (server-side tooling only). Leave
        /// null to target the SDK's active identity.
        /// </summary>
        public string? ExternalId { get; set; }
    }

    /// <summary>
    /// Result of <c>players.register()</c>. The plaintext
    /// <see cref="Secret"/> is only ever surfaced HERE, so store it
    /// locally on the device immediately (e.g. <c>PlayerPrefs</c> via
    /// <c>PlayerPrefsSecretStore</c>). A subsequent call to
    /// <see cref="PlayersClient.RegisterAsync"/> for the same player
    /// returns 409 <c>player_already_registered</c>; lost-secret
    /// recovery is a studio-side admin flow, not a client capability.
    /// </summary>
    public sealed class PlayerRegistration
    {
        [JsonProperty("playerId")] public string PlayerId { get; set; } = string.Empty;
        [JsonProperty("externalPlayerId")] public string ExternalPlayerId { get; set; } = string.Empty;
        [JsonProperty("secret")] public string Secret { get; set; } = string.Empty;
        [JsonProperty("secretPrefix")] public string? SecretPrefix { get; set; }
        [JsonProperty("registeredAt")] public string? RegisteredAt { get; set; }
        /// <summary>ISO-3166 alpha-2 country resolved server-side.</summary>
        [JsonProperty("country")] public string? Country { get; set; }
        /// <summary>
        /// Real display identity: reflects the value <c>SetIdentity</c>
        /// wrote, falling back to the synthetic-pool value when the
        /// player never renamed themselves. Enriched with country.
        /// </summary>
        [JsonProperty("displayIdentity")] public PlayerIdentity? DisplayIdentity { get; set; }
        /// <summary>
        /// Immutable anonymized identity (synthetic-pool value) enriched
        /// with country. Untouched by <c>SetIdentity</c>; use it for
        /// privacy-preserving surfaces (public boards).
        /// </summary>
        [JsonProperty("anonymizedIdentity")] public PlayerIdentity? AnonymizedIdentity { get; set; }
    }

    /// <summary>
    /// Public identity envelope: <c>Name</c> + optional <c>Avatar</c>
    /// enriched with the player's <c>Country</c>. Returned by both
    /// <see cref="PlayersClient.GetIdentityAsync"/> (real display value)
    /// and <see cref="PlayersClient.GetAnonymizedIdentityAsync"/> (the
    /// immutable synthetic-pool value); <c>Country</c> is the same on
    /// either view because a player has one real country regardless of
    /// which name/avatar the caller chose to render.
    /// </summary>
    public sealed class PlayerIdentity
    {
        [JsonProperty("name")] public string Name { get; set; } = string.Empty;
        [JsonProperty("avatar")] public string? Avatar { get; set; }
        [JsonProperty("country")] public string? Country { get; set; }
    }

    /// <summary>Response of <c>PUT /sdk/v1/players/:externalId/identity</c>.</summary>
    public sealed class SetIdentityResult
    {
        [JsonProperty("externalPlayerId")] public string ExternalPlayerId { get; set; } = string.Empty;
        [JsonProperty("displayIdentity")] public PlayerIdentity? DisplayIdentity { get; set; }
        [JsonProperty("anonymizedIdentity")] public PlayerIdentity? AnonymizedIdentity { get; set; }
    }

    /// <summary>
    /// Response of <c>PUT/PATCH /sdk/v1/players/:externalId/metadata</c>.
    /// The full metadata bag AFTER the write (so a PATCH caller can see
    /// the merged result without a follow-up GET).
    /// </summary>
    public sealed class PlayerMetadataResult
    {
        [JsonProperty("externalPlayerId")] public string ExternalPlayerId { get; set; } = string.Empty;
        [JsonProperty("metadata")] public Dictionary<string, object?> Metadata { get; set; } = new();
    }

    /// <summary>
    /// One row in the player's platform-managed inventory. Returned by
    /// <c>GET /sdk/v1/players/:externalId/inventory</c>. The item's
    /// display name and other catalog metadata live on the <c>items</c>
    /// table; the SDK only carries the per-player quantity + free-form
    /// metadata stamped on deposits (e.g. a granted potion's roll
    /// details).
    /// </summary>
    public sealed class PlayerItemHolding
    {
        [JsonProperty("itemKey")] public string ItemKey { get; set; } = string.Empty;
        [JsonProperty("quantity")] public int Quantity { get; set; }
        [JsonProperty("metadata")] public Dictionary<string, JToken> Metadata { get; set; } = new();
        [JsonProperty("createdAt")] public string CreatedAt { get; set; } = string.Empty;
        [JsonProperty("updatedAt")] public string UpdatedAt { get; set; } = string.Empty;
    }

    /// <summary>
    /// The game's daily login calendar as the player sees it
    /// (<c>GET /sdk/v1/players/:p/daily-login</c>). The server pays each
    /// day's reward on the player's first session of that UTC day, as a
    /// grant with <c>SourceKind == "daily_login"</c>; read this to draw
    /// the calendar.
    /// </summary>
    public sealed class DailyLogin
    {
        /// <summary>Day 1 first; each day's reward entries as configured in the dashboard.</summary>
        [JsonProperty("days")] public List<DailyLoginDay> Days { get; set; } = new();
        /// <summary><c>restart</c>, <c>hold_last</c> or <c>stop</c>: what a streak longer than the calendar does.</summary>
        [JsonProperty("afterLastDay")] public string AfterLastDay { get; set; } = "restart";
        /// <summary>Progression item holding <see cref="CurrentDay"/>, for clients already reading the wallet.</summary>
        [JsonProperty("itemKey")] public string ItemKey { get; set; } = string.Empty;
        /// <summary>Consecutive UTC days with a session; 0 once a day was missed.</summary>
        [JsonProperty("loginStreak")] public int LoginStreak { get; set; }
        /// <summary>Calendar day the player is on (1..Days.Count); 0 without a streak.</summary>
        [JsonProperty("currentDay")] public int CurrentDay { get; set; }
        /// <summary>Whether today's reward has been granted already.</summary>
        [JsonProperty("paidToday")] public bool PaidToday { get; set; }
        /// <summary>The grant that paid today, to match against the grant list or inventory stream.</summary>
        [JsonProperty("todayGrantId")] public string? TodayGrantId { get; set; }
        /// <summary>ISO start of the next UTC day: when the next day's reward can be earned.</summary>
        [JsonProperty("nextDayAt")] public string NextDayAt { get; set; } = string.Empty;
    }

    /// <summary>One day of a <see cref="DailyLogin"/> calendar.</summary>
    public sealed class DailyLoginDay
    {
        [JsonProperty("day")] public int Day { get; set; }
        /// <summary>Reward entries, e.g. <c>{ "type": "currency", "currencyKey": "gems", "amount": 10 }</c>.</summary>
        [JsonProperty("entries")] public List<Dictionary<string, JToken>> Entries { get; set; } = new();
    }

    /// <summary>
    /// One row in the player's wallet. Wallets are kind-agnostic: a
    /// <c>gold</c> currency entry sits beside a <c>trophies</c>
    /// progression entry with the same shape. The owning currency's
    /// <c>kind</c> lives on the catalog row, not here.
    /// </summary>
    public sealed class PlayerWalletHolding
    {
        [JsonProperty("economyKey")] public string EconomyKey { get; set; } = string.Empty;
        [JsonProperty("balance")] public int Balance { get; set; }
        [JsonProperty("metadata")] public Dictionary<string, JToken> Metadata { get; set; } = new();
        [JsonProperty("createdAt")] public string CreatedAt { get; set; } = string.Empty;
        [JsonProperty("updatedAt")] public string UpdatedAt { get; set; } = string.Empty;
    }

    /// <summary>
    /// Input for <see cref="PurchasesClient.TrackAsync"/> (a.k.a.
    /// <c>kraty.TrackPurchaseAsync</c>).
    ///
    /// <para>
    /// <see cref="AmountMinor"/> is in the currency's MINOR units as
    /// charged — cents for USD, whole yen for JPY. Unity IAP already
    /// reports minor units, so pass its value through unchanged rather
    /// than converting to a <c>decimal</c> or <c>float</c>.
    /// </para>
    ///
    /// <para>
    /// There is no idempotency key: <see cref="TransactionId"/> IS the
    /// idempotency key and it comes from the store. Calling this
    /// repeatedly for one transaction records nothing new and moves no
    /// total, so calling it unconditionally on every restore is the
    /// intended usage.
    /// </para>
    /// </summary>
    public sealed class TrackPurchaseInput
    {
        /// <summary>The store's transaction id. Used for dedupe.</summary>
        [JsonProperty("transactionId")] public string TransactionId { get; set; } = string.Empty;

        /// <summary>
        /// One of <c>app_store</c>, <c>google_play</c>, <c>steam</c>,
        /// <c>epic</c>, <c>amazon</c>, <c>microsoft</c>,
        /// <c>playstation</c>, <c>nintendo</c>, <c>web</c>, <c>other</c>.
        /// </summary>
        [JsonProperty("store")] public string Store { get; set; } = string.Empty;

        /// <summary>The store's product id / SKU.</summary>
        [JsonProperty("productId")] public string ProductId { get; set; } = string.Empty;

        /// <summary>
        /// One of <c>consumable</c>, <c>non_consumable</c>,
        /// <c>subscription</c>. Optional.
        /// </summary>
        [JsonProperty("productType")] public string? ProductType { get; set; }

        /// <summary>Minor units as charged: <c>499</c> is $4.99 in USD, ¥499 in JPY.</summary>
        [JsonProperty("amountMinor")] public int AmountMinor { get; set; }

        /// <summary>ISO 4217 alpha-3, e.g. <c>"USD"</c>, <c>"BRL"</c>, <c>"JPY"</c>.</summary>
        [JsonProperty("currency")] public string Currency { get; set; } = string.Empty;

        /// <summary>Store receipt / purchase token, retained for later validation.</summary>
        [JsonProperty("receipt")] public string? Receipt { get; set; }

        /// <summary>ISO-8601. Defaults to the server's clock when null.</summary>
        [JsonProperty("purchasedAt")] public string? PurchasedAt { get; set; }

        /// <summary>Arbitrary studio fields: campaign id, A/B bucket, ….</summary>
        [JsonProperty("metadata")] public Dictionary<string, object?>? Metadata { get; set; }

        /// <summary>
        /// The offer this purchase was made from: <see cref="Offer.InstanceId"/>
        /// from <see cref="OffersClient.ListAsync"/>. Only a purchase carrying it
        /// can advance the offer (and, when the offer opted in, have Kraty grant
        /// its contents); without it the SKU is matched for revenue reporting only.
        /// </summary>
        [JsonProperty("offerInstanceId")] public string? OfferInstanceId { get; set; }
    }

    /// <summary>
    /// Result of <see cref="PurchasesClient.TrackAsync"/>. Carries both
    /// what the player was charged (<see cref="AmountMinor"/> +
    /// <see cref="Currency"/>) and the normalized USD value
    /// (<see cref="NormalizedAmountMinor"/>, always USD cents) that
    /// spend thresholds and revenue reporting compare against.
    ///
    /// <para>
    /// <see cref="Deduplicated"/> is true when this transaction id was
    /// already on file. That is a success, not an error: the client
    /// asked "is this recorded?" and the answer is yes.
    /// </para>
    /// </summary>
    public sealed class TrackPurchaseResult
    {
        [JsonProperty("purchaseId")] public string PurchaseId { get; set; } = string.Empty;
        [JsonProperty("transactionId")] public string TransactionId { get; set; } = string.Empty;
        [JsonProperty("store")] public string Store { get; set; } = string.Empty;
        [JsonProperty("productId")] public string ProductId { get; set; } = string.Empty;
        [JsonProperty("productType")] public string? ProductType { get; set; }
        [JsonProperty("amountMinor")] public int AmountMinor { get; set; }
        [JsonProperty("currency")] public string Currency { get; set; } = string.Empty;

        /// <summary>USD cents.</summary>
        [JsonProperty("normalizedAmountMinor")] public int NormalizedAmountMinor { get; set; }

        /// <summary>Always <c>"USD"</c>. Present so callers never hardcode the base currency.</summary>
        [JsonProperty("normalizedCurrency")] public string NormalizedCurrency { get; set; } = "USD";

        [JsonProperty("fxRate")] public double FxRate { get; set; }

        /// <summary>One of <c>unverified</c>, <c>verified</c>, <c>failed</c>, <c>skipped</c>.</summary>
        [JsonProperty("validationStatus")] public string ValidationStatus { get; set; } = string.Empty;

        [JsonProperty("purchasedAt")] public string PurchasedAt { get; set; } = string.Empty;
        [JsonProperty("metadata")] public Dictionary<string, object?>? Metadata { get; set; }

        /// <summary>True when this transaction id was already on file — a replay.</summary>
        [JsonProperty("deduplicated")] public bool Deduplicated { get; set; }

        /// <summary>The player's lifetime spend in USD cents, after this purchase.</summary>
        [JsonProperty("lifetimeAmountMinor")] public int LifetimeAmountMinor { get; set; }

        [JsonProperty("purchaseCount")] public int PurchaseCount { get; set; }

        /// <summary>
        /// The offer this purchase was attributed to, or null. When
        /// <see cref="PurchaseOfferResult.StudioFulfils"/> is true, deliver
        /// <see cref="PurchaseOfferResult.Contents"/> yourself.
        /// </summary>
        [JsonProperty("offer")] public PurchaseOfferResult? Offer { get; set; }
    }

    // ── Offers (docs/26) ──

    /// <summary>
    /// One offer instance for the active player
    /// (<c>GET /sdk/v1/players/:p/offers</c>). Pass <see cref="InstanceId"/>
    /// as <see cref="TrackPurchaseInput.OfferInstanceId"/> when the player buys.
    /// </summary>
    public sealed class Offer
    {
        [JsonProperty("instanceId")] public string InstanceId { get; set; } = string.Empty;
        [JsonProperty("offerKey")] public string OfferKey { get; set; } = string.Empty;
        /// <summary><c>bundle</c>, <c>starter_pack</c>, <c>chain</c>, <c>win_back</c>, <c>revenge</c>, <c>out_of_resource</c>.</summary>
        [JsonProperty("kind")] public string Kind { get; set; } = string.Empty;
        /// <summary>A string or a <c>{ locale: text }</c> map.</summary>
        [JsonProperty("name")] public JToken? Name { get; set; }
        [JsonProperty("description")] public JToken? Description { get; set; }
        /// <summary>
        /// <c>available</c>, <c>shown</c>, <c>pending_verification</c>,
        /// <c>purchased</c>, <c>completed</c>, <c>dismissed</c>, <c>expired</c>.
        /// </summary>
        [JsonProperty("state")] public string State { get; set; } = string.Empty;
        [JsonProperty("tier")] public int Tier { get; set; }
        [JsonProperty("tierCount")] public int TierCount { get; set; }
        /// <summary>What to sell now.</summary>
        [JsonProperty("current")] public OfferTier Current { get; set; } = new();
        /// <summary>Chain offers: the next, still locked tier (a preview); null otherwise.</summary>
        [JsonProperty("next")] public OfferTier? Next { get; set; }
        /// <summary>Server-anchored close time (ISO); the backstop before the first impression.</summary>
        [JsonProperty("expiresAt")] public string? ExpiresAt { get; set; }
        /// <summary>Chain offers: when the current tier's timer runs out (ISO).</summary>
        [JsonProperty("tierExpiresAt")] public string? TierExpiresAt { get; set; }
        /// <summary>The offer closes when the session ends.</summary>
        [JsonProperty("endsWithSession")] public bool EndsWithSession { get; set; }
        [JsonProperty("purchasesRemaining")] public int? PurchasesRemaining { get; set; }
        [JsonProperty("presentation")] public OfferPresentation Presentation { get; set; } = new();
        /// <summary>
        /// The A/B variant this player sees (<see cref="Current"/>, <see cref="Next"/>
        /// and <see cref="Presentation"/> already reflect it), for your analytics;
        /// null for the base configuration.
        /// </summary>
        [JsonProperty("variantKey")] public string? VariantKey { get; set; }
        /// <summary>
        /// The linked event's key when the offer is event-linked (a sale while it
        /// is live, or a post-event offer): show it on that event's screen. Null otherwise.
        /// </summary>
        [JsonProperty("linkedEventKey")] public string? LinkedEventKey { get; set; }
    }

    /// <summary>
    /// One step of an <see cref="Offer"/>: sold through the store
    /// (<c>Type == "paid"</c>), or, on chain offers, claimed for free with
    /// <see cref="OffersClient.ClaimAsync"/> (<c>Type == "free"</c>, see
    /// <see cref="IsFree"/>).
    /// </summary>
    public sealed class OfferTier
    {
        [JsonProperty("index")] public int Index { get; set; }
        /// <summary><c>paid</c> or <c>free</c>. Absent on older servers, which means paid.</summary>
        [JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)] public string Type { get; set; } = "paid";
        /// <summary>A free tier: claim it with <see cref="OffersClient.ClaimAsync"/>, never through a store.</summary>
        [JsonIgnore] public bool IsFree => Type == "free";
        /// <summary>The store product key; empty for a free tier (the wire sends null).</summary>
        [JsonProperty("storeProductKey", NullValueHandling = NullValueHandling.Ignore)] public string StoreProductKey { get; set; } = string.Empty;
        /// <summary>Store → SKU, e.g. <c>{ "google_play": "com.game.starter499" }</c>. Sell the SKU of your store. Empty for a free tier.</summary>
        [JsonProperty("skus")] public Dictionary<string, string> Skus { get; set; } = new();
        /// <summary>Reference USD price in cents (display fallback; the store price is what the player pays). 0 for a free tier.</summary>
        [JsonProperty("referencePriceMinor")] public int ReferencePriceMinor { get; set; }
        /// <summary>Reward entries, e.g. <c>{ "type": "currency", "currencyKey": "gems", "amount": 500 }</c>.</summary>
        [JsonProperty("contents")] public List<Dictionary<string, JToken>> Contents { get; set; } = new();
        [JsonProperty("label")] public JToken? Label { get; set; }
        [JsonProperty("valueLabel")] public string? ValueLabel { get; set; }
    }

    /// <summary>How the dashboard asked the offer to be drawn. All optional.</summary>
    public sealed class OfferPresentation
    {
        /// <summary><c>popup</c>, <c>banner</c> or <c>store_tile</c>.</summary>
        [JsonProperty("layout")] public string? Layout { get; set; }
        [JsonProperty("badge")] public string? Badge { get; set; }
        [JsonProperty("artKey")] public string? ArtKey { get; set; }
    }

    /// <summary>Result of <see cref="OffersClient.MarkShownAsync"/>.</summary>
    public sealed class OfferImpressionResult
    {
        [JsonProperty("offer")] public Offer Offer { get; set; } = new();
        /// <summary>True for the impression that started the offer's timer.</summary>
        [JsonProperty("firstImpression")] public bool FirstImpression { get; set; }
        /// <summary>
        /// True when a cap refused this impression: don't show the offer.
        /// Re-rendering an offer already shown this session is never refused by
        /// the game-wide caps.
        /// </summary>
        [JsonProperty("capped")] public bool Capped { get; set; }
        /// <summary>
        /// Why (<see cref="Capped"/> true), else null: <c>offer_cycle</c>,
        /// <c>offer_day</c> (the offer's own caps), <c>global_session</c>,
        /// <c>global_day</c>, <c>global_gap</c> (the game-wide fatigue caps).
        /// </summary>
        [JsonProperty("cappedReason")] public string? CappedReason { get; set; }
        /// <summary>
        /// When the same impression could succeed (ISO), when known:
        /// <c>global_gap</c> → the last impression + the gap; <c>global_day</c> /
        /// <c>offer_day</c> → the next UTC midnight. Null otherwise.
        /// </summary>
        [JsonProperty("retryAt")] public string? RetryAt { get; set; }
    }

    /// <summary>
    /// What <see cref="OffersClient.TriggerAsync"/> reports happened in the
    /// game. Build one with <see cref="FailEvent"/>,
    /// <see cref="ResourceDepleted"/> or <see cref="LevelReached"/>.
    /// </summary>
    public sealed class OfferTriggerInput
    {
        /// <summary><c>fail_event</c>, <c>resource_depleted</c> or <c>level_reached</c>.</summary>
        [JsonProperty("type")] public string Type { get; set; } = string.Empty;
        [JsonProperty("key")] public string? Key { get; set; }
        [JsonProperty("consecutive")] public int? Consecutive { get; set; }
        [JsonProperty("resource")] public string? Resource { get; set; }
        [JsonProperty("required")] public int? Required { get; set; }
        [JsonProperty("available")] public int? Available { get; set; }
        [JsonProperty("secondsToRegen")] public int? SecondsToRegen { get; set; }
        [JsonProperty("level")] public int? Level { get; set; }

        /// <summary>
        /// A fail (<paramref name="key"/>, e.g. <c>level_failed</c>), with
        /// <paramref name="consecutive"/> fails in a row including this one
        /// (default 1). Arms revenge offers.
        /// </summary>
        public static OfferTriggerInput FailEvent(string key, int? consecutive = null) =>
            new OfferTriggerInput { Type = "fail_event", Key = key, Consecutive = consecutive };

        /// <summary>
        /// The player tried an action needing <paramref name="required"/> of
        /// <paramref name="resource"/> (a currency or item key) and has
        /// <paramref name="available"/>; <paramref name="secondsToRegen"/> is
        /// when a free refill arrives, if one does. Arms top-up offers.
        /// </summary>
        public static OfferTriggerInput ResourceDepleted(string resource, int required, int available, int? secondsToRegen = null) =>
            new OfferTriggerInput
            {
                Type = "resource_depleted",
                Resource = resource,
                Required = required,
                Available = available,
                SecondsToRegen = secondsToRegen,
            };

        /// <summary>The player's level now.</summary>
        public static OfferTriggerInput LevelReached(int level) =>
            new OfferTriggerInput { Type = "level_reached", Level = level };
    }

    /// <summary>
    /// One free chain tier claim (<see cref="OfferClaimResult.Claim"/>).
    /// Exactly one of <see cref="Granted"/> / <see cref="StudioFulfils"/> is
    /// true: when Kraty granted, the contents arrive as a grant; otherwise
    /// deliver <see cref="Contents"/> yourself.
    /// </summary>
    public sealed class OfferClaim
    {
        [JsonProperty("claimId")] public string ClaimId { get; set; } = string.Empty;
        [JsonProperty("offerId")] public string OfferId { get; set; } = string.Empty;
        [JsonProperty("offerKey")] public string OfferKey { get; set; } = string.Empty;
        [JsonProperty("instanceId")] public string InstanceId { get; set; } = string.Empty;
        [JsonProperty("tier")] public int Tier { get; set; }
        [JsonProperty("contents")] public List<Dictionary<string, JToken>> Contents { get; set; } = new();
        [JsonProperty("granted")] public bool Granted { get; set; }
        [JsonProperty("grantId")] public string? GrantId { get; set; }
        [JsonProperty("studioFulfils")] public bool StudioFulfils { get; set; }
        [JsonProperty("claimedAt")] public string ClaimedAt { get; set; } = string.Empty;
        /// <summary>The A/B variant whose contents were claimed; null for the base configuration.</summary>
        [JsonProperty("variantKey")] public string? VariantKey { get; set; }
    }

    /// <summary>Result of <see cref="OffersClient.ClaimAsync"/>: the claim and the offer after it.</summary>
    public sealed class OfferClaimResult
    {
        [JsonProperty("claim")] public OfferClaim Claim { get; set; } = new();
        /// <summary>The next tier is current with a fresh timer, or the state is <c>completed</c>.</summary>
        [JsonProperty("offer")] public Offer Offer { get; set; } = new();
    }

    /// <summary>Result of <see cref="OffersClient.TriggerAsync"/>.</summary>
    public sealed class OfferTriggerResult
    {
        /// <summary>The offers this trigger armed that can be shown now; empty when none did.</summary>
        [JsonProperty("offers")] public List<Offer> Offers { get; set; } = new();
    }

    /// <summary>The offer a purchase was attributed to (<see cref="TrackPurchaseResult.Offer"/>).</summary>
    public sealed class PurchaseOfferResult
    {
        [JsonProperty("offerId")] public string OfferId { get; set; } = string.Empty;
        [JsonProperty("offerKey")] public string OfferKey { get; set; } = string.Empty;
        [JsonProperty("instanceId")] public string? InstanceId { get; set; }
        [JsonProperty("tier")] public int? Tier { get; set; }
        /// <summary><c>explicit</c>: the purchase carried <c>OfferInstanceId</c>. <c>inferred</c>: matched by SKU, revenue only.</summary>
        [JsonProperty("method")] public string Method { get; set; } = string.Empty;
        /// <summary><c>attributed</c>, <c>pending_verification</c>, <c>granted</c>, <c>refused</c>, <c>disputed</c>, <c>failed</c>.</summary>
        [JsonProperty("status")] public string Status { get; set; } = string.Empty;
        /// <summary>The instance state after the purchase; null for inferred.</summary>
        [JsonProperty("state")] public string? State { get; set; }
        /// <summary>Kraty granted the contents (they arrive as a grant).</summary>
        [JsonProperty("granted")] public bool Granted { get; set; }
        /// <summary>Kraty will grant once a verified report of the purchase arrives.</summary>
        [JsonProperty("pendingVerification")] public bool PendingVerification { get; set; }
        /// <summary>The game delivers <see cref="Contents"/> itself (the offer's default).</summary>
        [JsonProperty("studioFulfils")] public bool StudioFulfils { get; set; }
        /// <summary>What the player bought (explicit only).</summary>
        [JsonProperty("contents")] public List<Dictionary<string, JToken>>? Contents { get; set; }
        /// <summary><c>verified</c>, <c>unverified</c> or <c>skipped</c>.</summary>
        [JsonProperty("validationStatus")] public string ValidationStatus { get; set; } = string.Empty;
        /// <summary>The A/B variant whose price and contents were sold; null for the base configuration.</summary>
        [JsonProperty("variantKey")] public string? VariantKey { get; set; }
    }

    /// <summary>
    /// Input for <see cref="InventoryClient.ConsumeAsync"/>. The server
    /// requires <see cref="IdempotencyKey"/> for consume; the SDK
    /// auto-generates one if you leave it null, matching the
    /// auto-stamping behavior on every other POST endpoint.
    /// </summary>
    public sealed class ConsumeItemInput
    {
        /// <summary>
        /// Positive integer; the SDK does NOT enforce this, so the server
        /// validates and returns 400 on zero / negative.
        /// </summary>
        [JsonProperty("quantity")] public int Quantity { get; set; }

        /// <summary>
        /// Free-form tag persisted on the ledger row only. Surfaces in
        /// the admin audit screen.
        /// </summary>
        [JsonProperty("reason")] public string? Reason { get; set; }

        /// <summary>Optional override; leave null to let the client auto-stamp one.</summary>
        [JsonProperty("idempotencyKey")] public string? IdempotencyKey { get; set; }
    }

    /// <summary>
    /// Result of <see cref="InventoryClient.ConsumeAsync"/>.
    /// <see cref="Applied"/> distinguishes a fresh mutation from an
    /// idempotent replay (the server returns the prior state when the
    /// same key arrives twice).
    /// </summary>
    public sealed class ConsumeItemResult
    {
        [JsonProperty("itemKey")] public string ItemKey { get; set; } = string.Empty;
        [JsonProperty("quantity")] public int Quantity { get; set; }
        [JsonProperty("applied")] public bool Applied { get; set; }
    }

    /// <summary>
    /// Input for <see cref="WalletClient.DebitAsync"/>. Same idempotency
    /// story as <see cref="ConsumeItemInput"/>. Credits are intentionally
    /// NOT in the SDK; only the studio's backend
    /// (<c>/server/v1/...</c>) can mint balance, so a client SDK that
    /// exposed <c>credit</c> would invite money printing.
    /// </summary>
    public sealed class DebitWalletInput
    {
        [JsonProperty("amount")] public int Amount { get; set; }
        [JsonProperty("reason")] public string? Reason { get; set; }
        [JsonProperty("idempotencyKey")] public string? IdempotencyKey { get; set; }
    }

    public sealed class DebitWalletResult
    {
        [JsonProperty("economyKey")] public string EconomyKey { get; set; } = string.Empty;
        [JsonProperty("balance")] public int Balance { get; set; }
        [JsonProperty("applied")] public bool Applied { get; set; }
    }

    /// <summary>
    /// Input for <see cref="WalletClient.ProgressAsync"/>. <see cref="Amount"/>
    /// is SIGNED: positive advances, negative rolls back (the balance never
    /// goes below zero).
    /// </summary>
    public sealed class ProgressWalletInput
    {
        [JsonProperty("amount")] public int Amount { get; set; }
        [JsonProperty("reason")] public string? Reason { get; set; }
        [JsonProperty("idempotencyKey")] public string? IdempotencyKey { get; set; }
    }

    /// <summary>
    /// A progression item the platform recomputed as a consequence of a
    /// <see cref="WalletClient.ProgressAsync"/> call — typically the player's
    /// <c>level</c> following their <c>xp</c>. <see cref="GrantIds"/> are the
    /// per-level rewards that fired; claim them with
    /// <see cref="GrantsClient.CollectAllAsync"/>.
    /// </summary>
    public sealed class DerivedProgressionChange
    {
        [JsonProperty("economyKey")] public string EconomyKey { get; set; } = string.Empty;
        [JsonProperty("previous")] public int Previous { get; set; }
        [JsonProperty("current")] public int Current { get; set; }
        [JsonProperty("delta")] public int Delta { get; set; }
        [JsonProperty("grantIds")] public List<string> GrantIds { get; set; } = new();
    }

    public sealed class ProgressWalletResult
    {
        [JsonProperty("economyKey")] public string EconomyKey { get; set; } = string.Empty;
        [JsonProperty("balance")] public int Balance { get; set; }
        [JsonProperty("applied")] public bool Applied { get; set; }
        /// <summary>
        /// Empty unless another progression item is configured to derive from
        /// this one.
        /// </summary>
        [JsonProperty("derived")] public List<DerivedProgressionChange> Derived { get; set; } = new();
    }

    /// <summary>
    /// Trustworthy server clock, returned by
    /// <see cref="KratyClient.GetServerTimeAsync"/>. Use it as an anti-cheat
    /// time source: game timers (event countdowns, etc.) should be checked
    /// against the server clock rather than the device clock, which a player
    /// can trivially change to fake a still-running event. Compare
    /// <see cref="EpochMs"/> against an event's <c>endsAt</c>.
    /// </summary>
    public sealed class ServerTime
    {
        /// <summary>
        /// Unix epoch milliseconds (UTC). This is the value to compare against
        /// event <c>endsAt</c> — always compare with <see cref="EpochMs"/>, not
        /// with <see cref="Local"/>.
        /// </summary>
        [JsonProperty("epochMs")] public long EpochMs { get; set; }

        /// <summary>UTC ISO-8601 string (e.g. <c>2027-01-15T08:00:00.000Z</c>).</summary>
        [JsonProperty("iso")] public string Iso { get; set; } = string.Empty;

        /// <summary>
        /// IANA timezone the <see cref="Local"/> / <see cref="OffsetMinutes"/>
        /// fields describe (<c>UTC</c> unless a <c>timezone</c> was requested).
        /// </summary>
        [JsonProperty("timezone")] public string Timezone { get; set; } = "UTC";

        /// <summary><see cref="Timezone"/>'s offset from UTC at this instant, in minutes.</summary>
        [JsonProperty("offsetMinutes")] public int OffsetMinutes { get; set; }

        /// <summary>
        /// Wall-clock in <see cref="Timezone"/>, ISO-like without a zone suffix
        /// (<c>YYYY-MM-DDTHH:mm:ss</c>). Display only — always compare with
        /// <see cref="EpochMs"/>.
        /// </summary>
        [JsonProperty("local")] public string Local { get; set; } = string.Empty;
    }

    // ── Friends / social graph ───────────────────────────────────────────

    /// <summary>
    /// A confirmed friend, enriched with identity + live presence. Mirrors
    /// the OpenAPI <c>Friend</c> component. <see cref="Online"/> /
    /// <see cref="LastActiveAt"/> / <see cref="Status"/> come from the
    /// friend's heartbeat and are only fresh while they keep beating.
    /// </summary>
    public sealed class Friend
    {
        [JsonProperty("externalPlayerId")] public string ExternalPlayerId { get; set; } = string.Empty;
        [JsonProperty("displayIdentity")] public PlayerIdentity? DisplayIdentity { get; set; }
        /// <summary>ISO timestamp the friendship was established.</summary>
        [JsonProperty("friendsSince")] public string FriendsSince { get; set; } = string.Empty;
        [JsonProperty("online")] public bool Online { get; set; }
        /// <summary>ISO timestamp of their last heartbeat, or null when never/expired.</summary>
        [JsonProperty("lastActiveAt")] public string? LastActiveAt { get; set; }
        /// <summary>Free-form client-set status ("in_match", "lobby", …), or null.</summary>
        [JsonProperty("status")] public string? Status { get; set; }
        /// <summary>
        /// Progression balances requested via the call's <c>progression</c>
        /// argument, e.g. <c>{ "level": 12 }</c>. Null when none were asked
        /// for; a resource the player never earned reads <c>0</c>.
        /// </summary>
        [JsonProperty("progression")] public Dictionary<string, int>? Progression { get; set; }
        /// <summary>
        /// ISO timestamp when this friend may be gifted again. Null means
        /// "giftable now" (or the game has gifting turned off), so a UI can
        /// treat a non-null value as "disable the gift button until then".
        /// </summary>
        [JsonProperty("giftCooldownUntil")] public string? GiftCooldownUntil { get; set; }
    }

    /// <summary>The caller's shareable friend code + display identity.</summary>
    public sealed class FriendCode
    {
        /// <summary>Short, unambiguous, stable share code (e.g. <c>"K7F2QX"</c>).</summary>
        [JsonProperty("friendCode")] public string Code { get; set; } = string.Empty;
        [JsonProperty("displayIdentity")] public PlayerIdentity? DisplayIdentity { get; set; }
        /// <summary>
        /// The caller's OWN progression balances, when requested. Handy for a
        /// profile card showing the share code and the player's level together.
        /// </summary>
        [JsonProperty("progression")] public Dictionary<string, int>? Progression { get; set; }
    }

    /// <summary>The player's own presence after a heartbeat.</summary>
    public sealed class PlayerPresence
    {
        [JsonProperty("online")] public bool Online { get; set; }
        [JsonProperty("lastActiveAt")] public string? LastActiveAt { get; set; }
        [JsonProperty("status")] public string? Status { get; set; }
    }

    /// <summary>
    /// The other player on a <see cref="FriendRequest"/> (the requester on
    /// an incoming request, the target on an outgoing one).
    /// </summary>
    public sealed class FriendRequestPlayer
    {
        [JsonProperty("externalPlayerId")] public string ExternalPlayerId { get; set; } = string.Empty;
        [JsonProperty("displayIdentity")] public PlayerIdentity? DisplayIdentity { get; set; }
        /// <summary>
        /// Progression balances requested via the call's <c>progression</c>
        /// argument, e.g. <c>{ "level": 12 }</c>. Null when none were asked
        /// for; a resource the player never earned reads <c>0</c>.
        /// </summary>
        [JsonProperty("progression")] public Dictionary<string, int>? Progression { get; set; }
    }

    /// <summary>A pending friend request, incoming or outgoing.</summary>
    public sealed class FriendRequest
    {
        [JsonProperty("requestId")] public string RequestId { get; set; } = string.Empty;
        /// <summary>One of <c>incoming</c>, <c>outgoing</c>.</summary>
        [JsonProperty("direction")] public string Direction { get; set; } = string.Empty;
        [JsonProperty("player")] public FriendRequestPlayer Player { get; set; } = new();
        [JsonProperty("createdAt")] public string CreatedAt { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response from <see cref="FriendsClient.ListRequestsAsync"/>: the
    /// caller's pending incoming + outgoing friend requests.
    /// </summary>
    public sealed class FriendRequests
    {
        [JsonProperty("incoming")] public List<FriendRequest> Incoming { get; set; } = new();
        [JsonProperty("outgoing")] public List<FriendRequest> Outgoing { get; set; } = new();
    }

    /// <summary>
    /// A username-search hit + the caller's relationship to that player.
    /// </summary>
    public sealed class FriendSearchResult
    {
        [JsonProperty("externalPlayerId")] public string ExternalPlayerId { get; set; } = string.Empty;
        [JsonProperty("displayIdentity")] public PlayerIdentity? DisplayIdentity { get; set; }
        /// <summary>One of <c>none</c>, <c>friends</c>, <c>request_incoming</c>, <c>request_outgoing</c>.</summary>
        [JsonProperty("relationship")] public string Relationship { get; set; } = string.Empty;
        /// <summary>
        /// Progression balances requested via the call's <c>progression</c>
        /// argument, e.g. <c>{ "level": 12 }</c>. Null when none were asked
        /// for; a resource the player never earned reads <c>0</c>.
        /// </summary>
        [JsonProperty("progression")] public Dictionary<string, int>? Progression { get; set; }
    }

    /// <summary>
    /// A suggested player to add — a recently-active non-friend the caller
    /// has no relationship with. Same identity + live-presence shape as
    /// <see cref="Friend"/>, minus the friendship-specific
    /// <c>FriendsSince</c> / <c>GiftCooldownUntil</c> (there's no edge yet).
    /// Returned by <see cref="FriendsClient.SuggestionsAsync"/>.
    /// </summary>
    public sealed class Suggestion
    {
        [JsonProperty("externalPlayerId")] public string ExternalPlayerId { get; set; } = string.Empty;
        [JsonProperty("displayIdentity")] public PlayerIdentity? DisplayIdentity { get; set; }
        [JsonProperty("online")] public bool Online { get; set; }
        /// <summary>ISO timestamp of their last heartbeat, or null when never/expired.</summary>
        [JsonProperty("lastActiveAt")] public string? LastActiveAt { get; set; }
        /// <summary>Free-form client-set status ("in_match", "lobby", …), or null.</summary>
        [JsonProperty("status")] public string? Status { get; set; }
        /// <summary>
        /// Progression balances requested via the call's <c>progression</c>
        /// argument, e.g. <c>{ "level": 12 }</c>. Null when none were asked
        /// for; a resource the player never earned reads <c>0</c>.
        /// </summary>
        [JsonProperty("progression")] public Dictionary<string, int>? Progression { get; set; }
    }

    /// <summary>A player the caller has blocked.</summary>
    public sealed class BlockedPlayer
    {
        [JsonProperty("externalPlayerId")] public string ExternalPlayerId { get; set; } = string.Empty;
        [JsonProperty("displayIdentity")] public PlayerIdentity? DisplayIdentity { get; set; }
        [JsonProperty("blockedAt")] public string BlockedAt { get; set; } = string.Empty;
    }

    /// <summary>
    /// Result of <see cref="FriendsClient.AddAsync"/>: either a
    /// newly-pending request (<c>Status == "pending"</c>, see
    /// <see cref="Request"/>), or an immediately-accepted friendship
    /// (<c>Status == "accepted"</c>, see <see cref="Friend"/>) when the
    /// other player had already requested the caller (reciprocal
    /// auto-accept).
    /// </summary>
    public sealed class SendFriendRequestResult
    {
        /// <summary>One of <c>pending</c>, <c>accepted</c>.</summary>
        [JsonProperty("status")] public string Status { get; set; } = string.Empty;
        /// <summary>Set when <see cref="Status"/> is <c>pending</c>.</summary>
        [JsonProperty("request")] public FriendRequest? Request { get; set; }
        /// <summary>Set when <see cref="Status"/> is <c>accepted</c>.</summary>
        [JsonProperty("friend")] public Friend? Friend { get; set; }
    }

    /// <summary>
    /// Add-or-block target for <see cref="FriendsClient.AddAsync"/> and
    /// <see cref="FriendsClient.BlockAsync"/>: set exactly ONE of
    /// <see cref="FriendCode"/> or <see cref="ExternalPlayerId"/>. Prefer
    /// the <see cref="ByCode"/> / <see cref="ByPlayerId"/> factories.
    /// </summary>
    public sealed class FriendTarget
    {
        /// <summary>A shareable friend code (from <see cref="FriendsClient.GetCodeAsync"/>).</summary>
        [JsonProperty("friendCode", NullValueHandling = NullValueHandling.Ignore)]
        public string? FriendCode { get; set; }

        /// <summary>The target player's external id.</summary>
        [JsonProperty("externalPlayerId", NullValueHandling = NullValueHandling.Ignore)]
        public string? ExternalPlayerId { get; set; }

        /// <summary>Target a player by their shareable friend code.</summary>
        public static FriendTarget ByCode(string friendCode) => new() { FriendCode = friendCode };

        /// <summary>Target a player by their external id.</summary>
        public static FriendTarget ByPlayerId(string externalPlayerId) => new() { ExternalPlayerId = externalPlayerId };
    }

    // ── Friend gifting ───────────────────────────────────────────────────

    /// <summary>
    /// One entry of the studio's gifting allowlist, from
    /// <see cref="FriendsClient.GiftCatalogAsync"/>. <see cref="Type"/> is
    /// <c>"item"</c> or <c>"currency"</c> — the latter covers progression
    /// items (XP, trophies) as well as spendable currencies, since both live
    /// in one catalog table.
    /// </summary>
    public sealed class GiftableResource
    {
        [JsonProperty("type")] public string Type { get; set; } = string.Empty;
        [JsonProperty("key")] public string Key { get; set; } = string.Empty;
        /// <summary>Display name. A bare string, or a locale map when the
        /// studio localizes its catalog; deserialized loosely for that reason.</summary>
        [JsonProperty("name")] public Newtonsoft.Json.Linq.JToken? Name { get; set; }
        [JsonProperty("iconUrl")] public string? IconUrl { get; set; }
        /// <summary>Economy rows only: <c>currency</c> or <c>progression</c>.</summary>
        [JsonProperty("economyKind")] public string? EconomyKind { get; set; }
        /// <summary>Item rows only.</summary>
        [JsonProperty("rarity")] public string? Rarity { get; set; }
        /// <summary>Largest amount one gift of this resource may carry.</summary>
        [JsonProperty("maxAmount")] public int MaxAmount { get; set; }
        /// <summary>Whether sending debits the sender's own holdings.</summary>
        [JsonProperty("costsSender")] public bool CostsSender { get; set; }
    }

    /// <summary>The gifting allowlist plus the game's limits.</summary>
    public sealed class GiftCatalog
    {
        [JsonProperty("enabled")] public bool Enabled { get; set; }
        /// <summary>Wait between two gifts to the SAME friend; 0 ⇒ no wait.</summary>
        [JsonProperty("cooldownSeconds")] public int CooldownSeconds { get; set; }
        /// <summary>Rolling-24h cap across all friends; null ⇒ unlimited.</summary>
        [JsonProperty("dailySendLimit")] public int? DailySendLimit { get; set; }
        /// <summary>Gifts the caller already sent inside that window.</summary>
        [JsonProperty("sentToday")] public int SentToday { get; set; }
        [JsonProperty("resources")] public List<GiftableResource> Resources { get; set; } = new();
    }

    /// <summary>A gift, from either side of the exchange.</summary>
    public sealed class Gift
    {
        [JsonProperty("giftId")] public string GiftId { get; set; } = string.Empty;
        /// <summary>The grant delivering it; also claimable via the grants API.</summary>
        [JsonProperty("grantId")] public string GrantId { get; set; } = string.Empty;
        /// <summary>One of <c>incoming</c>, <c>outgoing</c>.</summary>
        [JsonProperty("direction")] public string Direction { get; set; } = string.Empty;
        /// <summary>The other party: sender for incoming, recipient for outgoing.</summary>
        [JsonProperty("player")] public GiftCounterparty? Player { get; set; }
        /// <summary>One of <c>item</c>, <c>currency</c>.</summary>
        [JsonProperty("type")] public string Type { get; set; } = string.Empty;
        [JsonProperty("resourceKey")] public string ResourceKey { get; set; } = string.Empty;
        [JsonProperty("amount")] public int Amount { get; set; }
        [JsonProperty("message")] public string? Message { get; set; }
        /// <summary>Mirrors the delivering grant: <c>pending</c> until claimed.</summary>
        [JsonProperty("status")] public string Status { get; set; } = string.Empty;
        [JsonProperty("sentAt")] public string SentAt { get; set; } = string.Empty;
        [JsonProperty("claimedAt")] public string? ClaimedAt { get; set; }
        [JsonProperty("expiresAt")] public string? ExpiresAt { get; set; }
    }

    /// <summary>The other player on a gift.</summary>
    public sealed class GiftCounterparty
    {
        [JsonProperty("externalPlayerId")] public string ExternalPlayerId { get; set; } = string.Empty;
        [JsonProperty("displayIdentity")] public PlayerIdentity? DisplayIdentity { get; set; }
    }

    /// <summary>
    /// What to send and to whom, for <see cref="FriendsClient.SendGiftAsync"/>.
    /// Set exactly one of <see cref="FriendCode"/> / <see cref="ExternalPlayerId"/>;
    /// prefer the <see cref="ToCode"/> / <see cref="ToPlayerId"/> factories.
    /// </summary>
    public sealed class SendGiftInput
    {
        [JsonProperty("friendCode", NullValueHandling = NullValueHandling.Ignore)]
        public string? FriendCode { get; set; }

        [JsonProperty("externalPlayerId", NullValueHandling = NullValueHandling.Ignore)]
        public string? ExternalPlayerId { get; set; }

        /// <summary><c>"item"</c> or <c>"currency"</c>.</summary>
        [JsonProperty("type")] public string Type { get; set; } = string.Empty;

        /// <summary>Must name a catalog row the studio flagged giftable.</summary>
        [JsonProperty("resourceKey")] public string ResourceKey { get; set; } = string.Empty;

        [JsonProperty("amount")] public int Amount { get; set; }

        /// <summary>Optional short note; trimmed to 140 chars server-side.</summary>
        [JsonProperty("message", NullValueHandling = NullValueHandling.Ignore)]
        public string? Message { get; set; }

        /// <summary>
        /// Replay guard: a retry with the same key returns the original gift
        /// instead of tripping the per-friend cooldown.
        /// </summary>
        [JsonProperty("idempotencyKey", NullValueHandling = NullValueHandling.Ignore)]
        public string? IdempotencyKey { get; set; }

        /// <summary>Send to a friend addressed by their share code.</summary>
        public static SendGiftInput ToCode(string friendCode, string type, string resourceKey, int amount) =>
            new() { FriendCode = friendCode, Type = type, ResourceKey = resourceKey, Amount = amount };

        /// <summary>Send to a friend addressed by their external id.</summary>
        public static SendGiftInput ToPlayerId(string externalPlayerId, string type, string resourceKey, int amount) =>
            new() { ExternalPlayerId = externalPlayerId, Type = type, ResourceKey = resourceKey, Amount = amount };
    }

    /// <summary>
    /// Inventory list response wrapper; the backend nests the list
    /// inside <c>{ "data": { "items": [...] } }</c>.
    /// </summary>
    internal sealed class InventoryListEnvelope
    {
        [JsonProperty("items")] public List<PlayerItemHolding>? Items { get; set; }
    }

    /// <summary>
    /// Wallet list response wrapper; the backend nests the list
    /// inside <c>{ "data": { "wallet": [...] } }</c>.
    /// </summary>
    internal sealed class WalletListEnvelope
    {
        [JsonProperty("wallet")] public List<PlayerWalletHolding>? Wallet { get; set; }
    }

    /// <summary>Wrapper for <c>{ "data": { "friends": [...] } }</c>.</summary>
    internal sealed class FriendsListEnvelope
    {
        [JsonProperty("friends")] public List<Friend>? Friends { get; set; }
    }

    /// <summary>Wrapper for <c>{ "data": { "results": [...] } }</c>.</summary>
    internal sealed class FriendSearchEnvelope
    {
        [JsonProperty("results")] public List<FriendSearchResult>? Results { get; set; }
    }

    /// <summary>Wrapper for <c>{ "data": { "suggestions": [...] } }</c>.</summary>
    internal sealed class FriendSuggestionsEnvelope
    {
        [JsonProperty("suggestions")] public List<Suggestion>? Suggestions { get; set; }
    }

    /// <summary>Wrapper for <c>{ "data": { "friend": {...} } }</c>.</summary>
    internal sealed class FriendEnvelope
    {
        [JsonProperty("friend")] public Friend? Friend { get; set; }
    }

    /// <summary>Wrapper for <c>{ "data": { "blocked": [...] } }</c>.</summary>
    internal sealed class BlockedListEnvelope
    {
        [JsonProperty("blocked")] public List<BlockedPlayer>? Blocked { get; set; }
    }

    /// <summary>Wrapper for <c>{ "data": { "blocked": {...} } }</c>.</summary>
    internal sealed class BlockEnvelope
    {
        [JsonProperty("blocked")] public BlockedPlayer? Blocked { get; set; }
    }

    /// <summary>Wrapper for <c>{ "data": { "gifts": [...] } }</c>.</summary>
    internal sealed class GiftListEnvelope
    {
        [JsonProperty("gifts")] public List<Gift>? Gifts { get; set; }
    }

    /// <summary>Wrapper for <c>{ "data": { "gift": {...} } }</c>.</summary>
    internal sealed class GiftEnvelope
    {
        [JsonProperty("gift")] public Gift? Gift { get; set; }
    }

    // ── Guilds (docs/25) ──────────────────────────────────────────────

    /// <summary>Another player as guild screens show them.</summary>
    public sealed class GuildPlayerRef
    {
        [JsonProperty("externalPlayerId")] public string ExternalPlayerId { get; set; } = string.Empty;
        [JsonProperty("displayIdentity")] public PlayerIdentity? DisplayIdentity { get; set; }
    }

    /// <summary>A guild card: search results, recommendations, board rows.</summary>
    public class GuildSummary
    {
        [JsonProperty("guildId")] public string GuildId { get; set; } = string.Empty;
        [JsonProperty("name")] public string Name { get; set; } = string.Empty;
        [JsonProperty("description")] public string? Description { get; set; }
        /// <summary>Free-form key your game maps to emblem art.</summary>
        [JsonProperty("badge")] public string? Badge { get; set; }
        /// <summary>ISO-3166-1 alpha-2 country code (<c>PT</c>, <c>US</c>), or null.</summary>
        [JsonProperty("country")] public string? Country { get; set; }
        /// <summary><c>open</c> or <c>request</c>.</summary>
        [JsonProperty("joinType")] public string JoinType { get; set; } = "open";
        [JsonProperty("memberCount")] public int MemberCount { get; set; }
        /// <summary>The fixed Kraty limit (50).</summary>
        [JsonProperty("maxMembers")] public int MaxMembers { get; set; }
        [JsonProperty("metadata")] public Dictionary<string, JToken> Metadata { get; set; } = new();
        [JsonProperty("leader")] public GuildPlayerRef? Leader { get; set; }
        [JsonProperty("createdAt")] public string CreatedAt { get; set; } = string.Empty;
    }

    /// <summary>A recommended guild: <see cref="FriendCount"/> of your friends are in it.</summary>
    public sealed class RecommendedGuild : GuildSummary
    {
        [JsonProperty("friendCount")] public int FriendCount { get; set; }
    }

    /// <summary>One member of a guild roster.</summary>
    public sealed class GuildMember
    {
        [JsonProperty("externalPlayerId")] public string ExternalPlayerId { get; set; } = string.Empty;
        [JsonProperty("displayIdentity")] public PlayerIdentity? DisplayIdentity { get; set; }
        /// <summary><c>leader</c>, <c>co_leader</c> or <c>member</c>.</summary>
        [JsonProperty("role")] public string Role { get; set; } = "member";
        [JsonProperty("joinedAt")] public string JoinedAt { get; set; } = string.Empty;
        [JsonProperty("lastSeenAt")] public string? LastSeenAt { get; set; }
        [JsonProperty("online")] public bool Online { get; set; }
        /// <summary>Donations this member made in this guild this guild week.</summary>
        [JsonProperty("weeklyHelps")] public int WeeklyHelps { get; set; }
    }

    /// <summary>A guild with its roster (leader first, then co-leaders, members).</summary>
    public sealed class GuildDetail : GuildSummary
    {
        [JsonProperty("members")] public List<GuildMember> Members { get; set; } = new();
    }

    public sealed class GuildMembership
    {
        [JsonProperty("role")] public string Role { get; set; } = "member";
        [JsonProperty("joinedAt")] public string JoinedAt { get; set; } = string.Empty;
        /// <summary>What your role may do in this game (<c>kick</c>, <c>promote</c>, …).</summary>
        [JsonProperty("permissions")] public List<string> Permissions { get; set; } = new();
    }

    /// <summary>Your guild, your role and permissions.</summary>
    public sealed class MyGuild
    {
        [JsonProperty("guild")] public GuildDetail Guild { get; set; } = new();
        [JsonProperty("membership")] public GuildMembership Membership { get; set; } = new();
    }

    /// <summary>
    /// The game's guild rules as this player sees them (unlock gate, cost,
    /// roles, help settings, guild week). Kept as JSON objects for the nested
    /// settings so new fields reach the game without an SDK update.
    /// </summary>
    public sealed class GuildClientConfig
    {
        [JsonProperty("enabled")] public bool Enabled { get; set; }
        [JsonProperty("maxMembers")] public int MaxMembers { get; set; }
        [JsonProperty("defaultJoinType")] public string DefaultJoinType { get; set; } = "open";
        [JsonProperty("roles")] public Dictionary<string, List<string>> Roles { get; set; } = new();
        /// <summary><c>{ active, itemKey, threshold, balance, eligible }</c>; <c>active: false</c> = no gate.</summary>
        [JsonProperty("unlock")] public JObject Unlock { get; set; } = new();
        [JsonProperty("createCost")] public List<JObject> CreateCost { get; set; } = new();
        /// <summary>
        /// Who takes <see cref="CreateCost"/> and donor-paid help from the player:
        /// <c>platform</c> (Kraty debits it) or <c>studio</c> (your game manages
        /// inventory itself, so Kraty charges nothing and you charge the player).
        /// </summary>
        [JsonProperty("costsChargedBy")] public string CostsChargedBy { get; set; } = "platform";
        /// <summary><c>{ declinedRetryHours, maxPendingPerPlayer, ttlHours }</c>; an unanswered request expires <c>ttlHours</c> after it was made.</summary>
        [JsonProperty("joinRequests")] public JObject JoinRequests { get; set; } = new();
        [JsonProperty("leaveCooldownHours")] public int LeaveCooldownHours { get; set; }
        [JsonProperty("kickRejoinBlockHours")] public int KickRejoinBlockHours { get; set; }
        [JsonProperty("help")] public JObject Help { get; set; } = new();
        /// <summary>The guild week <c>{ dayOfWeek, hour, minute, timezone, currentStart, currentEnd }</c>; the weekly donation cap counts over it.</summary>
        [JsonProperty("week")] public JObject Week { get; set; } = new();
    }

    /// <summary>A request to join a <c>request</c>-type guild.</summary>
    public sealed class GuildJoinRequest
    {
        [JsonProperty("requestId")] public string RequestId { get; set; } = string.Empty;
        [JsonProperty("guild")] public JObject Guild { get; set; } = new();
        [JsonProperty("player")] public GuildPlayerRef? Player { get; set; }
        [JsonProperty("message")] public string? Message { get; set; }
        /// <summary><c>pending</c>, <c>accepted</c>, <c>declined</c>, <c>cancelled</c> or <c>expired</c>.</summary>
        [JsonProperty("status")] public string Status { get; set; } = "pending";
        [JsonProperty("createdAt")] public string CreatedAt { get; set; } = string.Empty;
        [JsonProperty("decidedAt")] public string? DecidedAt { get; set; }
    }

    /// <summary>Joined an open guild (<see cref="Guild"/>), or a request is waiting (<see cref="Request"/>).</summary>
    public sealed class GuildJoinResult
    {
        /// <summary><c>joined</c> or <c>requested</c>.</summary>
        [JsonProperty("status")] public string Status { get; set; } = string.Empty;
        [JsonProperty("guild")] public MyGuild? Guild { get; set; }
        [JsonProperty("request")] public GuildJoinRequest? Request { get; set; }
    }

    public sealed class GuildLeaveResult
    {
        [JsonProperty("guildId")] public string GuildId { get; set; } = string.Empty;
        /// <summary>You were the last member: the guild is gone.</summary>
        [JsonProperty("disbanded")] public bool Disbanded { get; set; }
        /// <summary>Who leads now, when you were the leader.</summary>
        [JsonProperty("newLeader")] public GuildPlayerRef? NewLeader { get; set; }
    }

    /// <summary>Input for <c>Guilds.CreateAsync</c> / <c>UpdateProfileAsync</c>; null fields are omitted.</summary>
    public sealed class GuildProfileInput
    {
        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)] public string? Name { get; set; }
        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)] public string? Description { get; set; }
        [JsonProperty("badge", NullValueHandling = NullValueHandling.Ignore)] public string? Badge { get; set; }
        [JsonProperty("country", NullValueHandling = NullValueHandling.Ignore)] public string? Country { get; set; }
        [JsonProperty("joinType", NullValueHandling = NullValueHandling.Ignore)] public string? JoinType { get; set; }
        [JsonProperty("metadata", NullValueHandling = NullValueHandling.Ignore)] public Dictionary<string, object?>? Metadata { get; set; }
    }

    public sealed class GuildHelpRequest
    {
        [JsonProperty("requestId")] public string RequestId { get; set; } = string.Empty;
        [JsonProperty("requester")] public GuildPlayerRef? Requester { get; set; }
        /// <summary><c>item</c> or <c>currency</c>.</summary>
        [JsonProperty("type")] public string Type { get; set; } = "item";
        [JsonProperty("resourceKey")] public string ResourceKey { get; set; } = string.Empty;
        [JsonProperty("amountRequested")] public int AmountRequested { get; set; }
        [JsonProperty("amountReceived")] public int AmountReceived { get; set; }
        /// <summary><c>open</c>, <c>filled</c>, <c>cancelled</c> or <c>expired</c>.</summary>
        [JsonProperty("status")] public string Status { get; set; } = "open";
        [JsonProperty("createdAt")] public string CreatedAt { get; set; } = string.Empty;
        [JsonProperty("expiresAt")] public string ExpiresAt { get; set; } = string.Empty;
        [JsonProperty("donatedByMe")] public bool DonatedByMe { get; set; }
    }

    public sealed class GuildHelpOverview
    {
        [JsonProperty("requests")] public List<GuildHelpRequest> Requests { get; set; } = new();
        [JsonProperty("myRequest")] public GuildHelpRequest? MyRequest { get; set; }
        /// <summary>When you may ask again (null = now).</summary>
        [JsonProperty("nextRequestAt")] public string? NextRequestAt { get; set; }
        /// <summary>The weekly helps counter: <c>{ start, end, donationsMade, donationLimit }</c>.</summary>
        [JsonProperty("week")] public JObject Week { get; set; } = new();
    }

    public sealed class GuildHelpDonation
    {
        [JsonProperty("donationId")] public string DonationId { get; set; } = string.Empty;
        [JsonProperty("requestId")] public string RequestId { get; set; } = string.Empty;
        [JsonProperty("amount")] public int Amount { get; set; }
        [JsonProperty("grantId")] public string GrantId { get; set; } = string.Empty;
        [JsonProperty("rewardGrantId")] public string? RewardGrantId { get; set; }
        [JsonProperty("request")] public GuildHelpRequest Request { get; set; } = new();
        [JsonProperty("week")] public JObject Week { get; set; } = new();
    }

    internal sealed class GuildListPayload<T>
    {
        [JsonProperty("guilds")] public List<T> Guilds { get; set; } = new();
    }

    /// <summary>A page of <c>SearchPageAsync</c> results.</summary>
    public sealed class GuildSearchPage
    {
        [JsonProperty("guilds")] public List<GuildSummary> Guilds { get; set; } = new();
        /// <summary>Pass as <c>offset</c> for the next page; null on the last page.</summary>
        [JsonProperty("nextOffset")] public int? NextOffset { get; set; }
    }

    internal sealed class GuildRequestsPayload
    {
        [JsonProperty("requests")] public List<GuildJoinRequest> Requests { get; set; } = new();
    }

    // ── Chat (docs/26) ────────────────────────────────────────────────

    /// <summary>A player as chat shows them (sender, typing indicator, member).</summary>
    public class ChatPlayerRef
    {
        [JsonProperty("externalPlayerId")] public string ExternalPlayerId { get; set; } = string.Empty;
        /// <summary>Name / avatar / country, or null when the player has none.</summary>
        [JsonProperty("displayIdentity")] public PlayerIdentity? DisplayIdentity { get; set; }
    }

    /// <summary>The other player of a direct conversation.</summary>
    public sealed class ChatPeer : ChatPlayerRef
    {
        [JsonProperty("online")] public bool Online { get; set; }
    }

    /// <summary>One member of a group (or other explicit-membership) conversation.</summary>
    public sealed class ChatMember : ChatPlayerRef
    {
        /// <summary><c>owner</c>, <c>admin</c> or <c>member</c> in groups; the guild role in guild chat.</summary>
        [JsonProperty("role")] public string Role { get; set; } = "member";
        [JsonProperty("joinedAt")] public string JoinedAt { get; set; } = string.Empty;
        [JsonProperty("online")] public bool Online { get; set; }
    }

    /// <summary>Who wrote a message.</summary>
    public sealed class ChatMessageSender
    {
        /// <summary><c>player</c>, <c>server</c> (your backend) or <c>system</c> (Kraty).</summary>
        [JsonProperty("kind")] public string Kind { get; set; } = "player";
        /// <summary>The player, when <see cref="Kind"/> is <c>player</c>; null otherwise.</summary>
        [JsonProperty("player")] public ChatPlayerRef? Player { get; set; }
    }

    /// <summary>
    /// One chat message, its text already resolved for this viewer (the
    /// profanity-filtered copy when the filter applies to them).
    /// </summary>
    public sealed class ChatMessage
    {
        [JsonProperty("messageId")] public string MessageId { get; set; } = string.Empty;
        [JsonProperty("conversationId")] public string ConversationId { get; set; } = string.Empty;
        /// <summary>Position in the conversation: gapless, ascending; the <c>before</c> / <c>after</c> cursor.</summary>
        [JsonProperty("seq")] public long Seq { get; set; }
        /// <summary><c>text</c>, <c>custom</c> (a game card) or <c>system</c> (Kraty-written). Treat as an open set.</summary>
        [JsonProperty("type")] public string Type { get; set; } = "text";
        [JsonProperty("sender")] public ChatMessageSender Sender { get; set; } = new();
        /// <summary>The text this viewer sees; null for a deleted message or a payload-only card. English fallback on <c>system</c>.</summary>
        [JsonProperty("text")] public string? Text { get; set; }
        /// <summary><see cref="Text"/> is the profanity-filtered copy.</summary>
        [JsonProperty("filtered")] public bool Filtered { get; set; }
        /// <summary><c>custom</c>: your card type. <c>system</c>: the event (<c>member_joined</c>, <c>gift_sent</c>, …).</summary>
        [JsonProperty("customType")] public string? CustomType { get; set; }
        /// <summary><c>custom</c>: the card payload. <c>system</c>: the event's data. Null when deleted.</summary>
        [JsonProperty("payload")] public JToken? Payload { get; set; }
        [JsonProperty("replyToId")] public string? ReplyToId { get; set; }
        /// <summary>Echoed back to the sender only, to match a pending local message.</summary>
        [JsonProperty("clientMessageId")] public string? ClientMessageId { get; set; }
        [JsonProperty("createdAt")] public string CreatedAt { get; set; } = string.Empty;
        /// <summary>When the sender last edited it (ISO 8601); null = never edited.</summary>
        [JsonProperty("editedAt")] public string? EditedAt { get; set; }
        [JsonProperty("deleted")] public bool Deleted { get; set; }
    }

    /// <summary>A posting gate on a progression item (level, trophies…).</summary>
    public sealed class ChatRequirement
    {
        [JsonProperty("itemKey")] public string ItemKey { get; set; } = string.Empty;
        [JsonProperty("threshold")] public double Threshold { get; set; }
    }

    /// <summary>A public channel the studio defined (<c>global</c>, <c>es</c>, <c>trade</c>…).</summary>
    public sealed class ChatChannel
    {
        [JsonProperty("key")] public string Key { get; set; } = string.Empty;
        [JsonProperty("name")] public string Name { get; set; } = string.Empty;
        [JsonProperty("description")] public string? Description { get; set; }
        /// <summary><c>players</c> or <c>server_only</c> (announcements).</summary>
        [JsonProperty("postPolicy")] public string PostPolicy { get; set; } = "players";
        /// <summary>The channel's own cooldown; 0 = the channel kind's.</summary>
        [JsonProperty("slowModeSeconds")] public int SlowModeSeconds { get; set; }
        /// <summary>
        /// Seconds a player waits between two messages here: the larger of the
        /// channel kind's and <see cref="SlowModeSeconds"/>, never under 15.
        /// Sending sooner is refused with 429 <c>chat_slow_mode</c>.
        /// </summary>
        [JsonProperty("cooldownSeconds")] public int CooldownSeconds { get; set; } = 15;
        /// <summary>Players per room; null = one room for everyone.</summary>
        [JsonProperty("roomSize")] public int? RoomSize { get; set; }
        /// <summary>
        /// The viewer's room (0-based). Big channels split into rooms, each its
        /// own conversation; the server places players automatically and
        /// stickily. Announcements reach every room.
        /// </summary>
        [JsonProperty("room")] public int Room { get; set; }
        /// <summary>Who may post (reading is open); null = everyone.</summary>
        [JsonProperty("requirement")] public ChatRequirement? Requirement { get; set; }
        [JsonProperty("metadata")] public Dictionary<string, JToken> Metadata { get; set; } = new();
        /// <summary>Only on <see cref="ChatClientConfig.Channels"/>: the channel's filter mode (<c>always</c>, <c>on_by_default</c>, <c>off_by_default</c>).</summary>
        [JsonProperty("profanityFilter")] public string? ProfanityFilter { get; set; }
    }

    /// <summary>Why the viewer can't post in a conversation.</summary>
    public sealed class ChatPostDenied
    {
        [JsonProperty("code")] public string Code { get; set; } = string.Empty;
        [JsonProperty("message")] public string Message { get; set; } = string.Empty;
        [JsonProperty("details")] public JObject? Details { get; set; }
    }

    /// <summary>
    /// A conversation as the viewer sees it. <see cref="Kind"/> is an open set
    /// (<c>direct</c>, <c>group</c>, <c>guild</c>, <c>channel</c>…).
    /// </summary>
    public sealed class ChatConversation
    {
        [JsonProperty("conversationId")] public string ConversationId { get; set; } = string.Empty;
        [JsonProperty("kind")] public string Kind { get; set; } = string.Empty;
        /// <summary>Group title, guild name, channel name; null for a direct conversation (show <see cref="Peer"/>).</summary>
        [JsonProperty("title")] public string? Title { get; set; }
        [JsonProperty("metadata")] public Dictionary<string, JToken> Metadata { get; set; } = new();
        [JsonProperty("memberCount")] public int MemberCount { get; set; }
        [JsonProperty("lastSeq")] public long LastSeq { get; set; }
        [JsonProperty("lastMessageAt")] public string? LastMessageAt { get; set; }
        [JsonProperty("lastMessage")] public ChatMessage? LastMessage { get; set; }
        [JsonProperty("unreadCount")] public int UnreadCount { get; set; }
        [JsonProperty("lastReadSeq")] public long LastReadSeq { get; set; }
        /// <summary>Notifications muted by the player until then (their choice, not a moderation mute).</summary>
        [JsonProperty("mutedUntil")] public string? MutedUntil { get; set; }
        /// <summary>Direct conversations: the other player.</summary>
        [JsonProperty("peer")] public ChatPeer? Peer { get; set; }
        /// <summary>Guild conversations: the guild.</summary>
        [JsonProperty("guildId")] public string? GuildId { get; set; }
        /// <summary>Channel conversations: the channel definition.</summary>
        [JsonProperty("channel")] public ChatChannel? Channel { get; set; }
        /// <summary>The viewer's role here (group / guild role), or null.</summary>
        [JsonProperty("role")] public string? Role { get; set; }
        [JsonProperty("canPost")] public bool CanPost { get; set; }
        /// <summary>Why the viewer can't post (blocked, friends only, requirement, announcements…).</summary>
        [JsonProperty("postDenied")] public ChatPostDenied? PostDenied { get; set; }
        /// <summary>The viewer may delete other players' messages here.</summary>
        [JsonProperty("canModerate")] public bool CanModerate { get; set; }
        /// <summary>The filter mode here: <c>always</c>, <c>on_by_default</c> or <c>off_by_default</c>.</summary>
        [JsonProperty("filterMode")] public string FilterMode { get; set; } = string.Empty;
        [JsonProperty("createdAt")] public string CreatedAt { get; set; } = string.Empty;
        /// <summary>
        /// The roster, on single-conversation reads of explicit-membership kinds
        /// (groups); null on list reads and for guilds / channels / direct.
        /// </summary>
        [JsonProperty("members")] public List<ChatMember>? Members { get; set; }
    }

    /// <summary>A page of the inbox.</summary>
    public sealed class ChatConversationPage
    {
        [JsonProperty("conversations")] public List<ChatConversation> Conversations { get; set; } = new();
        /// <summary>Pass as <c>offset</c> for the next page; null on the last page.</summary>
        [JsonProperty("nextOffset")] public int? NextOffset { get; set; }
    }

    /// <summary>A page of messages, oldest to newest.</summary>
    public sealed class ChatMessagePage
    {
        [JsonProperty("messages")] public List<ChatMessage> Messages { get; set; } = new();
        /// <summary>More messages exist past this page in the direction you paged.</summary>
        [JsonProperty("hasMore")] public bool HasMore { get; set; }
    }

    /// <summary>Unread totals for a badge: muted conversations don't count.</summary>
    public sealed class ChatUnread
    {
        /// <summary>Unread messages across the inbox.</summary>
        [JsonProperty("total")] public int Total { get; set; }
        /// <summary>Conversations with at least one unread message.</summary>
        [JsonProperty("conversations")] public int Conversations { get; set; }
        /// <summary>Unread messages per conversation kind.</summary>
        [JsonProperty("byKind")] public Dictionary<string, int> ByKind { get; set; } = new();
    }

    /// <summary>The player's saved chat choices.</summary>
    public sealed class ChatPreferences
    {
        /// <summary>Saved filter choice; null = each conversation kind's default.</summary>
        [JsonProperty("profanityFilter")] public bool? ProfanityFilter { get; set; }
        /// <summary><c>everyone</c>, <c>friends</c> or <c>nobody</c>.</summary>
        [JsonProperty("allowDirectFrom")] public string AllowDirectFrom { get; set; } = "everyone";
    }

    /// <summary>
    /// Patch for <c>Chat.SetPreferencesAsync</c>. Only the properties you
    /// assign are sent; assigning <see cref="ProfanityFilter"/> = <c>null</c>
    /// clears the saved choice back to the kinds' defaults.
    /// </summary>
    public sealed class ChatPreferencesUpdate
    {
        private bool? _profanityFilter;
        internal bool HasProfanityFilter { get; private set; }

        /// <summary>true / false, or null to clear (sent whenever assigned).</summary>
        public bool? ProfanityFilter
        {
            get => _profanityFilter;
            set { _profanityFilter = value; HasProfanityFilter = true; }
        }

        /// <summary><c>everyone</c>, <c>friends</c> or <c>nobody</c>; null = unchanged.</summary>
        public string? AllowDirectFrom { get; set; }
    }

    /// <summary>
    /// Patch for <c>Chat.SetConversationSettingsAsync</c>. Only the properties
    /// you assign are sent; assigning <see cref="MutedUntil"/> = <c>null</c>
    /// unmutes.
    /// </summary>
    public sealed class ChatConversationSettings
    {
        private System.DateTimeOffset? _mutedUntil;
        internal bool HasMutedUntil { get; private set; }

        /// <summary>Mute notifications until then, or null to unmute (sent whenever assigned).</summary>
        public System.DateTimeOffset? MutedUntil
        {
            get => _mutedUntil;
            set { _mutedUntil = value; HasMutedUntil = true; }
        }

        /// <summary>Hide the conversation from the inbox (it comes back on a new message); null = unchanged.</summary>
        public bool? Hidden { get; set; }
    }

    /// <summary>
    /// One conversation kind's settings in <see cref="ChatClientConfig.Kinds"/>.
    /// The typed properties cover the built-in kinds; anything else lands in
    /// <see cref="Extra"/>.
    /// </summary>
    public sealed class ChatKindConfig
    {
        [JsonProperty("enabled")] public bool Enabled { get; set; }
        /// <summary><c>always</c>, <c>on_by_default</c> or <c>off_by_default</c>.</summary>
        [JsonProperty("profanityFilter")] public string ProfanityFilter { get; set; } = string.Empty;
        /// <summary>Seconds a player waits between two messages in one conversation of this kind (at least 1; 15 for channels).</summary>
        [JsonProperty("cooldownSeconds")] public int CooldownSeconds { get; set; }
        /// <summary><c>direct</c>: <c>friends</c> or <c>anyone</c>.</summary>
        [JsonProperty("allowFrom")] public string? AllowFrom { get; set; }
        /// <summary><c>group</c>: most members one group may have.</summary>
        [JsonProperty("maxMembers")] public int? MaxMembers { get; set; }
        /// <summary><c>group</c>: most groups one player may belong to.</summary>
        [JsonProperty("maxGroupsPerPlayer")] public int? MaxGroupsPerPlayer { get; set; }
        /// <summary><c>group</c>: who can be added: <c>friends</c> or <c>anyone</c>.</summary>
        [JsonProperty("invitePolicy")] public string? InvitePolicy { get; set; }
        /// <summary><c>guild</c>: leaders and co-leaders may delete any message.</summary>
        [JsonProperty("officersCanDelete")] public bool? OfficersCanDelete { get; set; }
        /// <summary><c>guild</c>: Kraty posts system messages for joins, leaves, help requests.</summary>
        [JsonProperty("systemMessages")] public bool? SystemMessages { get; set; }
        /// <summary>Settings this SDK version doesn't model.</summary>
        [JsonExtensionData] public IDictionary<string, JToken> Extra { get; set; } = new Dictionary<string, JToken>();
    }

    /// <summary>A game-defined card type for <c>Chat.SendCustomAsync</c>.</summary>
    public sealed class ChatCustomType
    {
        [JsonProperty("key")] public string Key { get; set; } = string.Empty;
        [JsonProperty("description")] public string? Description { get; set; }
        [JsonProperty("maxPayloadBytes")] public int MaxPayloadBytes { get; set; }
        /// <summary>Optional JSON Schema the payload must match.</summary>
        [JsonProperty("schema")] public JToken? Schema { get; set; }
    }

    public sealed class ChatMessageRules
    {
        [JsonProperty("maxLength")] public int MaxLength { get; set; }
        /// <summary>When false, links in text are masked.</summary>
        [JsonProperty("allowLinks")] public bool AllowLinks { get; set; }
        [JsonProperty("customTypes")] public List<ChatCustomType> CustomTypes { get; set; } = new();
        /// <summary>Seconds after sending during which a player may edit their message; 0 = editing off.</summary>
        [JsonProperty("editWindowSeconds")] public int EditWindowSeconds { get; set; }
    }

    public sealed class ChatRateLimit
    {
        [JsonProperty("messagesPerMinute")] public int MessagesPerMinute { get; set; }
        /// <summary>The same text again in a conversation within this many seconds is refused (429 <c>chat_duplicate_message</c>); 0 = off.</summary>
        [JsonProperty("duplicateWindowSeconds")] public int DuplicateWindowSeconds { get; set; }
    }

    /// <summary>
    /// The game's chat rules as this player sees them, plus their preferences
    /// and moderation standing: build chat screens from this.
    /// </summary>
    public sealed class ChatClientConfig
    {
        /// <summary>Per conversation kind (<c>direct</c>, <c>group</c>, <c>guild</c>, <c>channel</c>…).</summary>
        [JsonProperty("kinds")] public Dictionary<string, ChatKindConfig> Kinds { get; set; } = new();
        /// <summary>The public channels (empty when channels are off).</summary>
        [JsonProperty("channels")] public List<ChatChannel> Channels { get; set; } = new();
        [JsonProperty("messages")] public ChatMessageRules Messages { get; set; } = new();
        [JsonProperty("rateLimit")] public ChatRateLimit RateLimit { get; set; } = new();
        [JsonProperty("preferences")] public ChatPreferences Preferences { get; set; } = new();
        [JsonProperty("status")] public ModerationStatus Status { get; set; } = new();
    }

    public sealed class ChatLeaveResult
    {
        [JsonProperty("conversationId")] public string ConversationId { get; set; } = string.Empty;
        [JsonProperty("left")] public bool Left { get; set; }
    }

    public sealed class ChatMemberRemoved
    {
        [JsonProperty("conversationId")] public string ConversationId { get; set; } = string.Empty;
        /// <summary>The removed player's external id.</summary>
        [JsonProperty("removed")] public string Removed { get; set; } = string.Empty;
    }

    public sealed class ChatMessageDeleted
    {
        [JsonProperty("messageId")] public string MessageId { get; set; } = string.Empty;
        [JsonProperty("deleted")] public bool Deleted { get; set; }
    }

    public sealed class ChatReadResult
    {
        /// <summary>The read marker now stored (never moves backwards, capped at the conversation's last seq).</summary>
        [JsonProperty("lastReadSeq")] public long LastReadSeq { get; set; }
    }

    internal sealed class ChatChannelsPayload
    {
        [JsonProperty("channels")] public List<ChatConversation> Channels { get; set; } = new();
    }

    // ── Moderation (docs/26) ──────────────────────────────────────────

    /// <summary>Report categories for <c>Moderation.ReportAsync</c>.</summary>
    public static class ReportCategory
    {
        public const string Spam = "spam";
        public const string Harassment = "harassment";
        public const string HateSpeech = "hate_speech";
        public const string SexualContent = "sexual_content";
        public const string Violence = "violence";
        public const string SelfHarm = "self_harm";
        public const string Cheating = "cheating";
        public const string Impersonation = "impersonation";
        public const string InappropriateName = "inappropriate_name";
        public const string Other = "other";
    }

    /// <summary>Where the reported behaviour happened.</summary>
    public static class ReportContext
    {
        public const string Chat = "chat";
        public const string Profile = "profile";
        public const string Guild = "guild";
        public const string Gameplay = "gameplay";
        public const string Other = "other";
    }

    /// <summary>A warning, chat mute or chat ban on the player.</summary>
    public sealed class Restriction
    {
        [JsonProperty("restrictionId")] public string RestrictionId { get; set; } = string.Empty;
        /// <summary><c>warning</c>, <c>chat_mute</c> or <c>chat_ban</c>.</summary>
        [JsonProperty("kind")] public string Kind { get; set; } = string.Empty;
        /// <summary>Shown to the player.</summary>
        [JsonProperty("reason")] public string? Reason { get; set; }
        /// <summary><c>moderator</c>, <c>auto</c> or <c>server</c>.</summary>
        [JsonProperty("source")] public string Source { get; set; } = string.Empty;
        [JsonProperty("createdAt")] public string CreatedAt { get; set; } = string.Empty;
        /// <summary>null = no expiry (a warning until acknowledged, an indefinite chat ban).</summary>
        [JsonProperty("expiresAt")] public string? ExpiresAt { get; set; }
        [JsonProperty("liftedAt")] public string? LiftedAt { get; set; }
        [JsonProperty("acknowledgedAt")] public string? AcknowledgedAt { get; set; }
        [JsonProperty("active")] public bool Active { get; set; }
        [JsonProperty("reportId")] public string? ReportId { get; set; }
    }

    /// <summary>The player's standing: can they read and post in chat, and why not.</summary>
    public sealed class ModerationStatus
    {
        /// <summary>False while chat-banned (no reading or posting).</summary>
        [JsonProperty("canChat")] public bool CanChat { get; set; } = true;
        /// <summary>False while chat-banned or muted.</summary>
        [JsonProperty("canPost")] public bool CanPost { get; set; } = true;
        [JsonProperty("mutedUntil")] public string? MutedUntil { get; set; }
        [JsonProperty("chatBannedUntil")] public string? ChatBannedUntil { get; set; }
        /// <summary>In force now; warnings stay until acknowledged.</summary>
        [JsonProperty("restrictions")] public List<Restriction> Restrictions { get; set; } = new();
    }

    /// <summary>A report this player filed. The reviewer's decision stays private.</summary>
    public sealed class MyReport
    {
        [JsonProperty("reportId")] public string ReportId { get; set; } = string.Empty;
        [JsonProperty("targetExternalPlayerId")] public string TargetExternalPlayerId { get; set; } = string.Empty;
        [JsonProperty("category")] public string Category { get; set; } = string.Empty;
        [JsonProperty("context")] public string Context { get; set; } = string.Empty;
        /// <summary><c>open</c>, <c>resolved</c> or <c>dismissed</c>.</summary>
        [JsonProperty("status")] public string Status { get; set; } = "open";
        [JsonProperty("createdAt")] public string CreatedAt { get; set; } = string.Empty;
        [JsonProperty("updatedAt")] public string UpdatedAt { get; set; } = string.Empty;
        [JsonProperty("resolvedAt")] public string? ResolvedAt { get; set; }
    }

    /// <summary>Input for <c>Moderation.ReportAsync</c>; null fields are omitted.</summary>
    public sealed class ReportPlayerInput
    {
        [JsonProperty("targetExternalPlayerId")] public string TargetExternalPlayerId { get; set; } = string.Empty;
        /// <summary>One of the <see cref="ReportCategory"/> values.</summary>
        [JsonProperty("category")] public string Category { get; set; } = ReportCategory.Other;
        /// <summary>One of the <see cref="ReportContext"/> values; omitted = <c>other</c>.</summary>
        [JsonProperty("context", NullValueHandling = NullValueHandling.Ignore)] public string? Context { get; set; }
        /// <summary>The player's own words (up to the server's limit).</summary>
        [JsonProperty("comment", NullValueHandling = NullValueHandling.Ignore)] public string? Comment { get; set; }
        /// <summary>The conversation the offending messages are in.</summary>
        [JsonProperty("conversationId", NullValueHandling = NullValueHandling.Ignore)] public string? ConversationId { get; set; }
        /// <summary>The offending messages, attached as evidence.</summary>
        [JsonProperty("messageIds", NullValueHandling = NullValueHandling.Ignore)] public List<string>? MessageIds { get; set; }
        [JsonProperty("guildId", NullValueHandling = NullValueHandling.Ignore)] public string? GuildId { get; set; }
    }

    internal sealed class MyReportsPayload
    {
        [JsonProperty("reports")] public List<MyReport> Reports { get; set; } = new();
    }

    /// <summary>
    /// Generic envelope: every successful SDK response is shaped as
    /// <c>{ "data": T }</c>. The client unwraps automatically.
    /// </summary>
    internal sealed class DataEnvelope<T>
    {
        [JsonProperty("data")] public T? Data { get; set; }
    }
}
