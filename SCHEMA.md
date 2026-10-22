# Kraty Unity SDK: public surface (v0.6.0)

Canonical method + type listing for `app.kraty.sdk`. Update this file in
the same commit as any signature change so a single grep tells you
exactly what's available.

All methods sit on the `Kraty` facade unless noted otherwise. Every
async method takes an optional trailing `CancellationToken ct = default`.

## `Kraty` facade

```csharp
Kraty(KratyClientOptions opts)
void Dispose()

string? ActiveExternalPlayerId { get; }
Task<(string ExternalPlayerId, string Secret)> EnsureIdentityAsync(CancellationToken ct = default)
Task LogoutAsync(CancellationToken ct = default)
Task SignInAsync(string externalPlayerId, string secret, CancellationToken ct = default)

// Finalization catch-up (docs/05b). Fires exactly once per board across the
// live SSE `finalized` event AND boards that ended while the player was away.
Action OnFinalized(Action<FinalizationResult> cb)         // returns unsubscribe
Task<List<FinalizationResult>> CheckFinalizationsAsync()  // call on app foreground/reconnect
Task DismissAsync(MembershipRef ref)                      // ack one handled result
Task<int> ClearReportedAsync()                            // bulk-drop delivered entries

// Static identity helpers
static Task<StoredIdentity?> ReadStoredIdentityAsync(ISecretStore secretStore, CancellationToken ct = default)
static Task RestoreStoredIdentityAsync(KratyClient client, StoredIdentity identity, ISecretStore secretStore, CancellationToken ct = default)
static Task ClearStoredIdentityAsync(ISecretStore secretStore, CancellationToken ct = default)
static Task<Kraty> ConnectAsPlayerAsync(KratyClientOptions opts, string? externalPlayerId, ISecretStore secretStore, bool autoRotateOnConflict = false, CancellationToken ct = default)
```

## `kraty.Events`: `EventsClient`

```csharp
Task<List<EventListing>> ListForPlayerAsync(string? @as = null, CancellationToken ct = default)
Task<StartAttemptResponse> StartAsync(string eventKey, Dictionary<string, object?>? playerContext = null, string? idempotencyKey = null, string? @as = null, CancellationToken ct = default)
Task<ProgressResult> ProgressAsync(string eventKey, string attemptId, ProgressInput input, string? @as = null, CancellationToken ct = default)

// Co-op team events (listing / start / progress / finish also carry `Teams`; the listing carries `TeamFormation` and `TeamOptions`;
// ProgressInput.EventTeamId names a partner bar)
Task<EventTeamMembersPage> TeamMembersAsync(string eventTeamId, string? cursor = null, int? limit = null, string? @as = null, CancellationToken ct = default)
Task<TeamMatchResult> MatchTeamAsync(string eventKey, string? @as = null, CancellationToken ct = default)
Task<PartnerInvite> InvitePartnerAsync(string eventKey, string? externalPlayerId = null, string? friendCode = null, string? @as = null, CancellationToken ct = default)
Task<List<PartnerInvite>> PartnerInvitesAsync(string eventKey, string direction = "in", string? @as = null, CancellationToken ct = default)
Task<PartnerInvite> AcceptInviteAsync(string eventKey, string eventTeamId, string? @as = null, CancellationToken ct = default)
Task<PartnerInvite> DeclineInviteAsync(string eventKey, string eventTeamId, string? @as = null, CancellationToken ct = default)
Task<PartnerInvite> CancelInviteAsync(string eventKey, string eventTeamId, string? @as = null, CancellationToken ct = default)
Task<RandomPartnerResult> RandomPartnerAsync(string eventKey, string? @as = null, CancellationToken ct = default)
Task<List<PartnerSuggestion>> PartnerSuggestionsAsync(string eventKey, int? limit = null, string? @as = null, CancellationToken ct = default)
```

## `kraty.Leaderboards`: `LeaderboardsClient`

The dashboard-configured cross-event boards (weekly / monthly /
all-time, optionally segmented). Addressed by stable game-scoped
**key**. Wire endpoints:

- `GET /sdk/v1/leaderboards` (list the game's boards)
- `GET /sdk/v1/leaderboards/:key`
- `GET /sdk/v1/leaderboards/:key/standings`
- `GET /sdk/v1/leaderboards/:key/periods`
- `POST /sdk/v1/players/:externalId/leaderboards/:key/join`
- `POST /sdk/v1/players/:externalId/leaderboards/:key/score`

```csharp
Task<List<LeaderboardSummary>> ListAsync(CancellationToken ct = default)
Task<Leaderboard>            ReadAsync(string key, LeaderboardReadOptions? opts = null, CancellationToken ct = default)
Task<Leaderboard>            JoinAsync(string key, LeaderboardJoinOptions? opts = null, CancellationToken ct = default)
Task<BoardStandings>         StandingsAsync(string key, StandingsReadOptions? opts = null, CancellationToken ct = default)
Task<LeaderboardScoreResult> SubmitScoreAsync(string key, double value, LeaderboardSubmitOptions? opts = null, CancellationToken ct = default)
Task<LeaderboardPeriods>     ListPeriodsAsync(string key, int? limit = null, CancellationToken ct = default)
```

`LeaderboardReadOptions`:
- `int? Limit`: 1–200, default 50 server-side
- `string? Segment`: bucket value; required only for `context` segmentation. Leave null for `progression`-segmented boards (server derives the caller's division); unsegmented boards ignore it
- `string? Period`: `"current"` (default) or an ISO timestamp from `LeaderboardPeriod.PeriodStartedAt`
- `bool IncludeSelf`: when true, response includes `self: { rank, score }` (live periods only)
- `string? ExternalId`: required when `IncludeSelf` is true; lazily resolved from the active player otherwise

`JoinAsync`: add the active player to the board at score 0 without submitting a score, and return the current standings. Idempotent (never resets an existing score). Response `Joined = true`. `LeaderboardJoinOptions`:
- `int? Limit`: 1–200, default 50 server-side
- `string? Segment`: bucket value for `context` boards; leave null for `progression` boards (server derives the division from the caller's balance)
- `string? ExternalId`: address a different player (server-side tooling only); lazily resolved otherwise

`StandingsAsync`: multi-segment read. Returns one `StandingsSegment` block per segment picked by `StandingsReadOptions.Scope`. `StandingsReadOptions`:
- `string? Scope`: `"self_segment"`, `"mine"`, `"segment"`, `"all"` (default `"all"`)
- `string? Segment`: required when `Scope == "segment"` on a segmented board
- `string? Period`: `"current"` (default) or an ISO timestamp from `ListPeriodsAsync`
- `string? ExternalId`: flags `isSelf` / `selfRank`; auto-resolved for `self_segment` / `mine`
- `int? Limit`: per-segment top-N (1..200, default 50)
- `int? MaxSegments`: cap on returned segment blocks (1..100, default 20)

`BoardStandings`: `key`, `leaderboardId`, `scope`, `resetCadence`, `scoreAggregation`, `period`, `List<StandingsSegment> Segments`, `bool SegmentsTruncated`.
`StandingsSegment`: `string? Segment`, `bool Participated`, `int? SelfRank`, `List<LeaderboardEntry> Entries`.

`SubmitScoreAsync`: submit a score for the active player directly to the board, outside an event attempt. Errors: `client_scoring_disabled` (403, board is server-only), `score_not_supported` (400, progression-ranked board), `not_found` (404), `validation_failed` (400). `LeaderboardSubmitOptions`:
- `string? Segment`: required only for `context` segmentation; null for `progression` boards (server derives the division); unsegmented boards ignore it
- `string? IdempotencyKey`: auto-stamped when null
- `string? ExternalId`: address a different player (server-side tooling only); lazily resolved otherwise

`LeaderboardScoreResult`:
- `string LeaderboardId`
- `double Score`
- `int? Rank`

## `kraty.EventLeaderboards`: `EventLeaderboardsClient`

The auto-generated per-event-window leaderboard. Addressed by the
**UUID** returned in `Events.StartAsync(...)`'s
`attempt.leaderboardId`. Includes Server-Sent Events live streaming.
Wire endpoints:

- `GET /sdk/v1/event-leaderboards/:id`
- `GET /sdk/v1/event-leaderboards/:id/stream`

```csharp
Task<EventLeaderboard>          ReadAsync(string leaderboardId, EventLeaderboardReadOptions? opts = null, CancellationToken ct = default)
Task<LeaderboardStream>         LiveAsync(string leaderboardId, CancellationToken ct = default)
LiveLeaderboardSubscription     Subscribe(string leaderboardId, Action<LeaderboardStreamEvent> onEvent, SubscribeOptions? opts = null)
```

`EventLeaderboardReadOptions`:
- `int? Limit`: 1–200, default 50 server-side
- `bool IncludeSelf`
- `string? ExternalId`

`SubscribeOptions`:
- `int PollIntervalMs`: default 15_000; 0 disables polling (SSE-only)
- `Action<Exception>? OnError`

`Leaderboard` and `EventLeaderboard` reads also carry `ProgressionLimits? Progression` — the resolved promotion/relegation cutoffs for the caller's division (`Promotion` / `Relegation`, each `{ int Places; string ItemKey }` or null; a per-division override wins over the global ladder) — so a client renders "top N promote / bottom N relegate" from server config. `EventLeaderboard` read response includes `bool Finalized` and, when finalized, `string? FinalizedReason` (`session_terminated` \| `window_closed`), which powers the finalization catch-up's session-vs-window distinction. `FinalizationResult` = `{ MembershipRef Ref; string Reason; SelfEntry? Self; IReadOnlyList<FinalStanding>? Standings; string? EventKey }`; `Reason` uses the `FinalizationReason` consts (`SessionTerminated` \| `WindowClosed` \| `PeriodRolled` \| `Finalized`). Registry persistence is injectable via `KratyClientOptions.MembershipStore` (`PlayerPrefsMembershipStore` in Unity, `InMemoryMembershipStore` otherwise).

`LiveLeaderboardSubscription` exposes `Task CancelAsync()` /
`void Dispose()`. Callbacks fire on the HTTP background thread, so
marshal to Unity's main thread before touching `UnityEngine` APIs.

## `kraty.Grants`: `GrantsClient`

```csharp
Task<List<Grant>>       ListPendingAsync(int? limit = null, string? @as = null, CancellationToken ct = default)
Task<Grant>             ClaimAsync(string grantId, string? idempotencyKey = null, string? @as = null, CancellationToken ct = default)
Task<OpenCrateResponse> OpenAsync(string grantId, string? idempotencyKey = null, string? @as = null, CancellationToken ct = default)
Task<CollectAllResult>  CollectAllAsync(string? @as = null, CancellationToken ct = default)
```

`CollectAllResult` carries:
- `List<Grant> Opened`: crate grants whose contents were rolled
- `List<Grant> Claimed`: reward grants flipped to claimed
- `List<CollectAllFailure> Failures`: per-grant errors that didn't abort the sweep
- `bool HasFailures => Failures.Count > 0`

## `kraty.Lobbies`: `LobbiesClient`

```csharp
Task<Lobby> ReadAsync(string lobbyId, CancellationToken ct = default)
```

## `kraty.Inventory`: `InventoryClient`

```csharp
Task<List<PlayerItemHolding>> ListAsync(string? @as = null, CancellationToken ct = default)
Task<ConsumeItemResult>       ConsumeAsync(string itemKey, ConsumeItemInput input, string? @as = null, CancellationToken ct = default)
```

## `kraty.Wallet`: `WalletClient`

```csharp
Task<List<PlayerWalletHolding>> ListAsync(string? @as = null, CancellationToken ct = default)
Task<DebitWalletResult>         DebitAsync(string economyKey, DebitWalletInput input, string? @as = null, CancellationToken ct = default)
```

## `kraty.DailyLogin`: `DailyLoginClient`

```csharp
Task<DailyLogin?> ReadAsync(string? @as = null, CancellationToken ct = default)
```

`GET /sdk/v1/players/:p/daily-login`. The game's daily login calendar:
`Days[{ Day, Entries }]`, `AfterLastDay`, `LoginStreak`, `CurrentDay`,
`PaidToday`, `TodayGrantId`, `NextDayAt`, `ItemKey`. `null` when the game
has none or it is off for the key's environment. Read-only: the request's
session is what pays today's reward (a `daily_login` grant).

## `kraty.Guilds`: `GuildsClient`

```csharp
Task<GuildClientConfig> ConfigAsync(string? @as = null, CancellationToken ct = default)
Task<MyGuild?> MineAsync(...)
Task<GuildDetail> GetAsync(string guildId, ...)
Task<List<GuildSummary>> SearchAsync(string query, string? country = null, int? limit = null, ...)
Task<GuildSearchPage> SearchPageAsync(string query, string? country = null, int? limit = null, int? offset = null, ...) // { Guilds, NextOffset }
Task<List<RecommendedGuild>> RecommendedAsync(string? country = null, int? limit = null, ...)
Task<MyGuild> CreateAsync(GuildProfileInput input, ...)
Task<GuildJoinResult> JoinAsync(string guildId, string? message = null, ...)
Task<List<GuildJoinRequest>> MyRequestsAsync(...)
Task CancelRequestAsync(string requestId, ...)
Task<GuildDetail> UpdateProfileAsync(GuildProfileInput patch, ...)
Task<GuildLeaveResult> LeaveAsync(...)
Task<List<GuildJoinRequest>> RequestsAsync(...)
Task<GuildDetail> AcceptRequestAsync(string requestId, ...)
Task DeclineRequestAsync(string requestId, ...)
Task<GuildDetail> KickAsync / PromoteAsync / DemoteAsync(string memberExternalId, ...)
Task<GuildDetail> TransferLeadershipAsync(string memberExternalId, ...)
Task<GuildHelpOverview> HelpAsync(...)
Task<GuildHelpRequest> RequestHelpAsync(string type, string resourceKey, int amount, ...)
Task<GuildHelpDonation> DonateAsync(string requestId, int? amount = null, ...)
Task<GuildHelpRequest> CancelHelpAsync(string requestId, ...)
```

`/sdk/v1/players/:p/guild*` and `/guilds*` (docs/25). Every method ends with
`string? @as = null, CancellationToken ct = default`.

## `kraty.Chat`: `ChatClient`

```csharp
Task<ChatClientConfig> ConfigAsync(...)                         // kinds, channels, message rules, rate limit, Preferences, Status
Task<ChatPreferences> PreferencesAsync(...)
Task<ChatPreferences> SetPreferencesAsync(ChatPreferencesUpdate update, ...)   // only assigned props sent; ProfanityFilter = null clears
Task<ChatConversationPage> ConversationsAsync(string? kind = null, int? limit = null, int? offset = null, ...) // { Conversations, NextOffset }
Task<ChatUnread> UnreadAsync(...)                               // { Total, Conversations, ByKind }
Task<ChatConversation> ConversationAsync(string conversationId, ...)          // Members set for groups
Task<ChatConversation> OpenDirectAsync(string? externalPlayerId = null, string? friendCode = null, ...) // exactly one
Task<ChatConversation> CreateGroupAsync(string title, IEnumerable<string>? externalPlayerIds = null, Dictionary<string, object?>? metadata = null, ...)
Task<ChatConversation> UpdateGroupAsync(string conversationId, string? title = null, Dictionary<string, object?>? metadata = null, ...)
Task<ChatConversation> AddMembersAsync(string conversationId, IEnumerable<string> externalPlayerIds, ...)
Task<ChatMemberRemoved> RemoveMemberAsync(string conversationId, string externalPlayerId, ...)
Task<ChatConversation> SetRoleAsync(string conversationId, string externalPlayerId, string role, ...) // owner | admin | member
Task<ChatLeaveResult> LeaveAsync(string conversationId, ...)
Task<ChatConversation> GuildAsync(...)                          // 409 conflict when guildless
Task<List<ChatConversation>> ChannelsAsync(...)
Task<ChatConversation> ChannelAsync(string channelKey, ...)
Task<ChatMessagePage> MessagesAsync(string conversationId, long? before = null, long? after = null, int? limit = null, ...) // oldest→newest; { Messages, HasMore }
Task<ChatMessage> SendAsync(string conversationId, string text, string? replyToId = null, string? clientMessageId = null, ...)
Task<ChatMessage> SendCustomAsync(string conversationId, string customType, object? payload, string? text = null, string? replyToId = null, string? clientMessageId = null, ...)
Task<ChatMessage> EditMessageAsync(string conversationId, string messageId, string text, ...) // own message, within Messages.EditWindowSeconds
Task<ChatMessageDeleted> DeleteMessageAsync(string conversationId, string messageId, ...)
Task<ChatReadResult> MarkReadAsync(string conversationId, long seq, ...)      // { LastReadSeq }
Task TypingAsync(string conversationId, ...)
Task<ChatConversation> SetConversationSettingsAsync(string conversationId, ChatConversationSettings settings, ...) // MutedUntil (null = unmute), Hidden
Task<ChatStream> LiveAsync(IEnumerable<string>? channels = null, Action<ChatStream>? configure = null, ...)
```

`/sdk/v1/players/:p/chat/*` (docs/26). Every method ends with
`string? @as = null, CancellationToken ct = default`. Chat off for the game =
`KratyApiError` `403 chat_disabled`. `SendAsync` / `SendCustomAsync` fill
`clientMessageId` with a fresh GUID when you pass none, so a retried send is
idempotent. `payload` / `metadata` keep their keys exactly (no camelCasing).
`EditMessageAsync` (`PATCH .../messages/:messageId` `{ text }`) edits your own
message while `ChatClientConfig.Messages.EditWindowSeconds` (0 = editing off)
has not elapsed; the result has `ChatMessage.EditedAt` (ISO string, null =
never edited) set. Otherwise `403 chat_edit_not_allowed`; the filter and
`chat_message_rejected` / `chat_message_invalid` / 429 behave as on send.

`KratyClientOptions.ChatProfanityFilter` (`bool?`, default null): when set,
every chat request and the stream send `X-Kraty-Chat-Filter: on|off` (and
`?filter=on|off` on the stream URL); null = the player's saved preference,
then the kind's default.

`ChatStream` (SSE, `GET /chat/stream?channels=a,b`): typed callbacks
`OnReady(ChatStreamReady)`, `OnMessage(ChatMessage)`,
`OnMessageEdited(ChatMessage)` (`message_edited`, the full updated message),
`OnMessageDeleted(ChatMessageDeletedEvent)`, `OnTyping(ChatTypingEvent)`,
`OnRead(ChatReadEvent)`, `OnConversationAdded(ChatConversationAddedEvent)`,
`OnConversationUpdated(ChatConversationUpdatedEvent)`,
`OnConversationRemoved(ChatConversationRemovedEvent)`,
`OnRestriction(ChatRestrictionEvent)`,
`OnRestrictionLifted(ChatRestrictionLiftedEvent)`, `OnClose(ChatCloseEvent)`;
plus raw `OnEvent(ChatStreamEvent)`, `OnError(Exception)`, the `Ready`
payload, `Task CancelAsync()` / `void Dispose()`. Set callbacks inside
`configure` (runs before the read loop starts). No auto-reconnect: on close,
reopen and catch up with `MessagesAsync(id, after: lastSeq)`. Event names:
`ChatStreamEventKind` consts.

## `kraty.Moderation`: `ModerationClient`

```csharp
Task<MyReport> ReportAsync(ReportPlayerInput input, ...)        // TargetExternalPlayerId, Category, Context?, Comment?, ConversationId?, MessageIds?, GuildId?
Task<List<MyReport>> ReportsAsync(int? limit = null, ...)
Task<ModerationStatus> StatusAsync(...)                         // CanChat, CanPost, MutedUntil, ChatBannedUntil, Restrictions
Task<Restriction> AcknowledgeWarningAsync(string restrictionId, ...)
```

`/sdk/v1/players/:p/reports` and `/moderation/*`; works in every game (no
chat add-on needed). Every method ends with `string? @as = null,
CancellationToken ct = default`. Category / context values:
`ReportCategory` and `ReportContext` consts.

## `kraty.Players`: `PlayersClient`

```csharp
Task<PlayerRegistration> RegisterAsync(string externalPlayerId, bool force = false, CancellationToken ct = default)
Task<PlayerIdentity> SetIdentityAsync(string name, string? avatar = null, string? @as = null, CancellationToken ct = default)
```

## `kraty.Catalog`: `CatalogClient`

```csharp
Task<Catalog> GetAsync(CancellationToken ct = default)
```

## Polling helpers (static)

```csharp
GrantPolling.PollPendingAsync(GrantsClient grants, string? @as = null, GrantPollOptions? opts = null, CancellationToken ct = default)
LobbyPolling.UntilActiveAsync(LobbiesClient lobbies, string lobbyId, LobbyPollOptions? opts = null, CancellationToken ct = default)
```

## Secret stores

```csharp
ISecretStore                  // interface; implement for custom storage
InMemorySecretStore           // tests + non-Unity
PlayerPrefsSecretStore        // Unity, wraps UnityEngine.PlayerPrefs (default on Unity)
```

## DTOs (response shapes)

`Attempt`, `StartAttemptResponse`, `ProgressResult`, `MilestoneFired`,
`EventListing`, `EntryCost`, `EntryCostCurrency`, `EntryCostItem`,
`Leaderboard`, `LeaderboardSummary`, `EventLeaderboard`, `LeaderboardEntry`,
`LeaderboardSelf`, `ProgressionLimits`, `ProgressionBand`,
`LeaderboardPeriod`, `LeaderboardPeriods`,
`Grant`, `OpenCrateResponse`, `Lobby`, `ProgressInput`,
`PlayerRegistration`, `PlayerItemHolding`, `PlayerWalletHolding`,
`DailyLogin`, `DailyLoginDay`,
`GuildClientConfig`, `GuildSummary`, `RecommendedGuild`, `GuildMember`,
`GuildDetail`, `MyGuild`, `GuildJoinRequest`, `GuildJoinResult`, `GuildLeaveResult`,
`GuildProfileInput`, `GuildHelpRequest`, `GuildHelpOverview`,
`GuildHelpDonation`, `GuildPlayerRef`,
`ChatClientConfig`, `ChatKindConfig`, `ChatMessageRules`, `ChatCustomType`, `ChatRateLimit`,
`ChatConversation`, `ChatConversationPage`, `ChatMessage`, `ChatMessageSender`, `ChatMessagePage`,
`ChatMember`, `ChatPeer`, `ChatPlayerRef`, `ChatChannel`, `ChatRequirement`, `ChatPostDenied`,
`ChatPreferences`, `ChatPreferencesUpdate`, `ChatConversationSettings`, `ChatUnread`,
`ChatLeaveResult`, `ChatMemberRemoved`, `ChatMessageDeleted`, `ChatReadResult`,
`ChatStreamEvent`, `ChatStreamReady`, `ChatStreamChannel`, `ChatMessageDeletedEvent`, `ChatTypingEvent`,
`ChatReadEvent`, `ChatConversationAddedEvent`, `ChatConversationUpdatedEvent`,
`ChatConversationRemovedEvent`, `ChatRestrictionEvent`, `ChatRestrictionLiftedEvent`, `ChatCloseEvent`,
`MyReport`, `ReportPlayerInput`, `ModerationStatus`, `Restriction`,
`ConsumeItemInput`, `ConsumeItemResult`, `DebitWalletInput`,
`DebitWalletResult`, `Catalog`, `CatalogItem`, `CatalogCurrency`,
`RewardBundlePreview`, `RewardEntryPreview`, `RewardPolicySummary`,
`StoredIdentity`, `TeamSeatView`, `TeamGoal`, `TeamSeatMilestone`,
`TeamSeatState`, `TeamClientOptions`, `TeamRollup`, `TeamRollupMilestone`, `LeaderboardEntryTeam`,
`EventTeamMember`, `EventTeamMembersPage`, `TeamMatchResult`, `PartnerPlayer`,
`PartnerInvite`, `RandomPartnerResult`, `PartnerSuggestion`.

## Errors

```csharp
KratyApiError      // non-2xx response; has Code (KratyErrorCode), Status, Message, Body
KratyNetworkError  // transport / parse failure
```

`KratyApiError` boolean helpers:
`IsLobbyForming`, `IsInsufficientEntryCost`, `IsPlayerSecretInvalid`,
`IsPlayerAlreadyRegistered`, `IsEntryRequirementFailed`,
`IsNoLeaderboard`, `IsNoActiveWindow`, `IsMaxAttemptsReached`,
`IsAttemptExpired`, `IsAttemptCompleted`.

Full `KratyErrorCode` enum: see `Runtime/Errors/KratyErrorCode.cs`.
