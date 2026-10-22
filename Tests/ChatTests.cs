using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Kraty.Tests
{
    /// <summary>ChatClient + ChatStream: URL shaping, bodies, the filter header, unwrapping, typed stream events.</summary>
    public sealed class ChatTests
    {
        private const string ApiKey = "pUUVdrM8.djr4-0Iv9h1JvVNSMZNDmSsSN7lSVq2F9dG6DG4A5uQ";
        private const string BaseUrl = "https://api.test.kraty.io";
        private const string C = "7c1d1e0e-2b7e-4d55-9f8f-0a4b8d1c2e33";
        private const string M = "5a0c7d7a-8c55-4b8e-a0f3-6c2f0f7e9d11";
        private const string Prefix = "/sdk/v1/players/alice/chat";

        private static Kraty Make(FakeHandler handler, bool? filter = null) => new Kraty(new KratyClientOptions
        {
            ApiKey = ApiKey,
            BaseUrl = BaseUrl,
            HttpMessageHandler = handler,
            PlayerSecret = "ps",
            ChatProfanityFilter = filter,
            Retry = new RetryConfig
            {
                Attempts = 1,
                InitialDelay = TimeSpan.FromMilliseconds(1),
                MaxDelay = TimeSpan.FromMilliseconds(5),
                Jitter = 0,
            },
            Timeout = TimeSpan.FromSeconds(1),
        });

        private const string MessageJson =
            "{\"messageId\":\"" + M + "\",\"conversationId\":\"" + C + "\",\"seq\":7,\"type\":\"text\"," +
            "\"sender\":{\"kind\":\"player\",\"player\":{\"externalPlayerId\":\"alice\",\"displayIdentity\":{\"name\":\"Alice\",\"avatar\":null,\"country\":\"PT\"}}}," +
            "\"text\":\"h***o\",\"filtered\":true,\"customType\":null,\"payload\":null,\"replyToId\":null," +
            "\"clientMessageId\":\"cm-1\",\"createdAt\":\"2026-10-15T12:00:00.000Z\",\"editedAt\":null,\"deleted\":false}";

        private const string EditedMessageJson =
            "{\"messageId\":\"" + M + "\",\"conversationId\":\"" + C + "\",\"seq\":7,\"type\":\"text\"," +
            "\"sender\":{\"kind\":\"player\",\"player\":{\"externalPlayerId\":\"alice\",\"displayIdentity\":null}}," +
            "\"text\":\"fixed\",\"filtered\":false,\"customType\":null,\"payload\":null,\"replyToId\":null," +
            "\"clientMessageId\":null,\"createdAt\":\"2026-10-15T12:00:00.000Z\",\"editedAt\":\"2026-10-15T12:01:00.000Z\",\"deleted\":false}";

        private const string ConversationJson =
            "{\"conversationId\":\"" + C + "\",\"kind\":\"direct\",\"title\":null,\"metadata\":{},\"memberCount\":2," +
            "\"lastSeq\":7,\"lastMessageAt\":\"2026-10-15T12:00:00.000Z\",\"lastMessage\":" + MessageJson + "," +
            "\"unreadCount\":3,\"lastReadSeq\":4,\"mutedUntil\":null," +
            "\"peer\":{\"externalPlayerId\":\"bob\",\"displayIdentity\":null,\"online\":true}," +
            "\"guildId\":null,\"channel\":null,\"role\":null,\"canPost\":false," +
            "\"postDenied\":{\"code\":\"chat_friends_only\",\"message\":\"friends only\"}," +
            "\"canModerate\":false,\"filterMode\":\"on_by_default\",\"createdAt\":\"2026-10-01T00:00:00.000Z\"}";

        [Fact]
        public async Task ConfigParsesKindsChannelsRulesAndStanding()
        {
            var handler = new FakeHandler().Push(200,
                "{\"data\":{\"kinds\":{" +
                "\"direct\":{\"enabled\":true,\"profanityFilter\":\"on_by_default\",\"allowFrom\":\"friends\"}," +
                "\"group\":{\"enabled\":true,\"profanityFilter\":\"always\",\"maxMembers\":50,\"maxGroupsPerPlayer\":10,\"invitePolicy\":\"anyone\"}," +
                "\"guild\":{\"enabled\":false,\"profanityFilter\":\"off_by_default\",\"officersCanDelete\":true,\"systemMessages\":true,\"futureFlag\":3}}," +
                "\"channels\":[{\"key\":\"global\",\"name\":\"Global\",\"description\":null,\"postPolicy\":\"players\",\"slowModeSeconds\":5," +
                "\"requirement\":{\"itemKey\":\"level\",\"threshold\":10},\"metadata\":{\"lang\":\"en\"},\"profanityFilter\":\"always\"}]," +
                "\"messages\":{\"maxLength\":500,\"allowLinks\":false,\"customTypes\":[{\"key\":\"gift\",\"description\":null,\"maxPayloadBytes\":1024,\"schema\":null}],\"editWindowSeconds\":300}," +
                "\"rateLimit\":{\"messagesPerMinute\":20}," +
                "\"preferences\":{\"profanityFilter\":null,\"allowDirectFrom\":\"friends\"}," +
                "\"status\":{\"canChat\":true,\"canPost\":false,\"mutedUntil\":\"2026-10-16T00:00:00.000Z\",\"chatBannedUntil\":null," +
                "\"restrictions\":[{\"restrictionId\":\"r1\",\"kind\":\"chat_mute\",\"reason\":\"spam\",\"source\":\"auto\",\"createdAt\":\"x\"," +
                "\"expiresAt\":\"2026-10-16T00:00:00.000Z\",\"liftedAt\":null,\"acknowledgedAt\":null,\"active\":true,\"reportId\":null}]}}}");
            using var kraty = Make(handler);
            var cfg = await kraty.Chat.ConfigAsync(@as: "alice");

            Assert.EndsWith($"{Prefix}/config", handler.Calls[0].Url);
            Assert.Equal("GET", handler.Calls[0].Method);
            Assert.Equal("friends", cfg.Kinds["direct"].AllowFrom);
            Assert.Equal(50, cfg.Kinds["group"].MaxMembers);
            Assert.Equal("always", cfg.Kinds["group"].ProfanityFilter);
            Assert.False(cfg.Kinds["guild"].Enabled);
            Assert.True(cfg.Kinds["guild"].OfficersCanDelete);
            Assert.Equal(3, (int)cfg.Kinds["guild"].Extra["futureFlag"]);
            Assert.Equal("global", cfg.Channels[0].Key);
            Assert.Equal(10, cfg.Channels[0].Requirement!.Threshold);
            Assert.Equal("always", cfg.Channels[0].ProfanityFilter);
            Assert.Equal("en", cfg.Channels[0].Metadata["lang"].ToString());
            Assert.Equal(500, cfg.Messages.MaxLength);
            Assert.Equal("gift", cfg.Messages.CustomTypes[0].Key);
            Assert.Equal(300, cfg.Messages.EditWindowSeconds);
            Assert.Equal(20, cfg.RateLimit.MessagesPerMinute);
            Assert.Null(cfg.Preferences.ProfanityFilter);
            Assert.Equal("friends", cfg.Preferences.AllowDirectFrom);
            Assert.False(cfg.Status.CanPost);
            Assert.Equal("chat_mute", cfg.Status.Restrictions[0].Kind);
            Assert.Equal("2026-10-16T00:00:00.000Z", cfg.Status.MutedUntil);
        }

        [Theory]
        [InlineData(true, "on")]
        [InlineData(false, "off")]
        public async Task FilterHeaderSentOnEveryChatRequestWhenConfigured(bool filter, string expected)
        {
            var handler = new FakeHandler()
                .Push(200, "{\"data\":{\"total\":0,\"conversations\":0,\"byKind\":{}}}")
                .Push(201, "{\"data\":" + MessageJson + "}")
                .Push(200, "{\"data\":{\"canChat\":true,\"canPost\":true,\"mutedUntil\":null,\"chatBannedUntil\":null,\"restrictions\":[]}}");
            using var kraty = Make(handler, filter);
            await kraty.Chat.UnreadAsync(@as: "alice");
            await kraty.Chat.SendAsync(C, "hello", @as: "alice");
            await kraty.Moderation.StatusAsync(@as: "alice");
            Assert.Equal(expected, handler.Calls[0].Headers["x-kraty-chat-filter"]);
            Assert.Equal(expected, handler.Calls[1].Headers["x-kraty-chat-filter"]);
            // Moderation isn't chat: no filter choice to make there.
            Assert.False(handler.Calls[2].Headers.ContainsKey("x-kraty-chat-filter"));
        }

        [Fact]
        public async Task FilterHeaderOmittedByDefault()
        {
            var handler = new FakeHandler().Push(200, "{\"data\":{\"total\":2,\"conversations\":1,\"byKind\":{\"direct\":2}}}");
            using var kraty = Make(handler);
            var unread = await kraty.Chat.UnreadAsync(@as: "alice");
            Assert.False(handler.Calls[0].Headers.ContainsKey("x-kraty-chat-filter"));
            Assert.Equal("ps", handler.Calls[0].Headers["x-player-secret"]);
            Assert.Equal(2, unread.Total);
            Assert.Equal(1, unread.Conversations);
            Assert.Equal(2, unread.ByKind["direct"]);
        }

        [Fact]
        public void FilterOptionSurvivesOptionCopies()
        {
            var opts = new KratyClientOptions { ApiKey = ApiKey, ChatProfanityFilter = false };
            Assert.False(opts.WithPlayerSecret("s").ChatProfanityFilter);
            Assert.False(opts.WithActiveExternalPlayerId("p").ChatProfanityFilter);
        }

        [Fact]
        public async Task SendAutoGeneratesClientMessageIdAndParsesMessage()
        {
            var handler = new FakeHandler().Push(201, "{\"data\":" + MessageJson + "}");
            using var kraty = Make(handler);
            var msg = await kraty.Chat.SendAsync(C, "hello", @as: "alice");

            var call = handler.Calls[0];
            Assert.Equal("POST", call.Method);
            Assert.EndsWith($"{Prefix}/conversations/{C}/messages", call.Url);
            var body = JObject.Parse(call.Body!);
            Assert.Equal("text", body["type"]!.ToString());
            Assert.Equal("hello", body["text"]!.ToString());
            Assert.True(Guid.TryParse(body["clientMessageId"]!.ToString(), out _));
            Assert.Null(body["replyToId"]);
            Assert.NotNull(body["idempotencyKey"]);

            Assert.Equal(M, msg.MessageId);
            Assert.Equal(7, msg.Seq);
            Assert.True(msg.Filtered);
            Assert.Equal("h***o", msg.Text);
            Assert.Equal("player", msg.Sender.Kind);
            Assert.Equal("Alice", msg.Sender.Player!.DisplayIdentity!.Name);
            Assert.Equal("2026-10-15T12:00:00.000Z", msg.CreatedAt);
            Assert.True(msg.Payload == null || msg.Payload.Type == JTokenType.Null);
        }

        [Fact]
        public async Task EachSendGetsItsOwnClientMessageIdUnlessGiven()
        {
            var handler = new FakeHandler()
                .Push(201, "{\"data\":" + MessageJson + "}")
                .Push(201, "{\"data\":" + MessageJson + "}")
                .Push(201, "{\"data\":" + MessageJson + "}");
            using var kraty = Make(handler);
            await kraty.Chat.SendAsync(C, "a", @as: "alice");
            await kraty.Chat.SendAsync(C, "b", @as: "alice");
            await kraty.Chat.SendAsync(C, "c", replyToId: M, clientMessageId: "local-42", @as: "alice");
            var first = JObject.Parse(handler.Calls[0].Body!)["clientMessageId"]!.ToString();
            var second = JObject.Parse(handler.Calls[1].Body!)["clientMessageId"]!.ToString();
            Assert.NotEqual(first, second);
            var third = JObject.Parse(handler.Calls[2].Body!);
            Assert.Equal("local-42", third["clientMessageId"]!.ToString());
            Assert.Equal(M, third["replyToId"]!.ToString());
        }

        [Fact]
        public async Task SendCustomSendsCardAndKeepsPayloadKeys()
        {
            var handler = new FakeHandler().Push(201,
                "{\"data\":{\"messageId\":\"" + M + "\",\"conversationId\":\"" + C + "\",\"seq\":8,\"type\":\"custom\"," +
                "\"sender\":{\"kind\":\"player\",\"player\":null},\"text\":\"for you\",\"filtered\":false,\"customType\":\"gift\"," +
                "\"payload\":{\"ItemKey\":\"sword\",\"amount\":2},\"replyToId\":null,\"clientMessageId\":\"x\",\"createdAt\":\"t\",\"deleted\":false}}");
            using var kraty = Make(handler);
            var msg = await kraty.Chat.SendCustomAsync(
                C, "gift", new Dictionary<string, object?> { ["ItemKey"] = "sword", ["amount"] = 2 },
                text: "for you", @as: "alice");

            var body = JObject.Parse(handler.Calls[0].Body!);
            Assert.Equal("custom", body["type"]!.ToString());
            Assert.Equal("gift", body["customType"]!.ToString());
            Assert.Equal("for you", body["text"]!.ToString());
            Assert.Equal("sword", body["payload"]!["ItemKey"]!.ToString());
            Assert.Equal(2, (int)body["payload"]!["amount"]!);
            Assert.True(Guid.TryParse(body["clientMessageId"]!.ToString(), out _));

            Assert.Equal("gift", msg.CustomType);
            Assert.Equal("sword", msg.Payload!["ItemKey"]!.ToString());
        }

        [Fact]
        public async Task ConversationsBuildsQueryAndParsesPage()
        {
            var handler = new FakeHandler().Push(200, "{\"data\":{\"conversations\":[" + ConversationJson + "],\"nextOffset\":20}}");
            using var kraty = Make(handler);
            var page = await kraty.Chat.ConversationsAsync(kind: "direct", limit: 20, offset: 20, @as: "alice");
            Assert.EndsWith($"{Prefix}/conversations?kind=direct&limit=20&offset=20", handler.Calls[0].Url);
            Assert.Equal(20, page.NextOffset);
            var c = page.Conversations[0];
            Assert.Equal("direct", c.Kind);
            Assert.Null(c.Title);
            Assert.True(c.Peer!.Online);
            Assert.Equal("bob", c.Peer.ExternalPlayerId);
            Assert.Equal(3, c.UnreadCount);
            Assert.Equal(7, c.LastSeq);
            Assert.Equal(4, c.LastReadSeq);
            Assert.Equal(7, c.LastMessage!.Seq);
            Assert.False(c.CanPost);
            Assert.Equal("chat_friends_only", c.PostDenied!.Code);
            Assert.Equal("on_by_default", c.FilterMode);
            Assert.Null(c.Members);
        }

        [Fact]
        public async Task ConversationCarriesMembersAndChannelsUnwrap()
        {
            var group = "{\"conversationId\":\"" + C + "\",\"kind\":\"group\",\"title\":\"Squad\",\"metadata\":{\"Color\":\"red\"},\"memberCount\":2," +
                "\"lastSeq\":0,\"lastMessageAt\":null,\"lastMessage\":null,\"unreadCount\":0,\"lastReadSeq\":0,\"mutedUntil\":null,\"peer\":null," +
                "\"guildId\":null,\"channel\":null,\"role\":\"owner\",\"canPost\":true,\"postDenied\":null,\"canModerate\":true,\"filterMode\":\"always\"," +
                "\"createdAt\":\"t\",\"members\":[{\"externalPlayerId\":\"alice\",\"displayIdentity\":null,\"role\":\"owner\",\"joinedAt\":\"t\",\"online\":true}," +
                "{\"externalPlayerId\":\"bob\",\"displayIdentity\":null,\"role\":\"member\",\"joinedAt\":\"t\",\"online\":false}]}";
            var channel = "{\"conversationId\":\"" + C + "\",\"kind\":\"channel\",\"title\":\"Global\",\"metadata\":{},\"memberCount\":0," +
                "\"lastSeq\":3,\"lastMessageAt\":null,\"lastMessage\":null,\"unreadCount\":0,\"lastReadSeq\":0,\"mutedUntil\":null,\"peer\":null," +
                "\"guildId\":null,\"channel\":{\"key\":\"global\",\"name\":\"Global\",\"description\":\"All players\",\"postPolicy\":\"server_only\"," +
                "\"slowModeSeconds\":0,\"requirement\":null,\"metadata\":{}},\"role\":null,\"canPost\":false,\"postDenied\":null,\"canModerate\":false," +
                "\"filterMode\":\"always\",\"createdAt\":\"t\"}";
            var handler = new FakeHandler()
                .Push(200, "{\"data\":" + group + "}")
                .Push(200, "{\"data\":{\"channels\":[" + channel + "]}}")
                .Push(200, "{\"data\":" + channel + "}")
                .Push(200, "{\"data\":" + group.Replace("\"group\"", "\"guild\"") + "}");
            using var kraty = Make(handler);

            var conv = await kraty.Chat.ConversationAsync(C, @as: "alice");
            Assert.Equal(2, conv.Members!.Count);
            Assert.Equal("owner", conv.Members[0].Role);
            Assert.False(conv.Members[1].Online);
            Assert.Equal("red", conv.Metadata["Color"].ToString());
            Assert.EndsWith($"{Prefix}/conversations/{C}", handler.Calls[0].Url);

            var channels = await kraty.Chat.ChannelsAsync(@as: "alice");
            Assert.Single(channels);
            Assert.Equal("server_only", channels[0].Channel!.PostPolicy);
            Assert.EndsWith($"{Prefix}/channels", handler.Calls[1].Url);

            var one = await kraty.Chat.ChannelAsync("global", @as: "alice");
            Assert.Equal("global", one.Channel!.Key);
            Assert.EndsWith($"{Prefix}/channels/global", handler.Calls[2].Url);

            var guild = await kraty.Chat.GuildAsync(@as: "alice");
            Assert.Equal("guild", guild.Kind);
            Assert.EndsWith($"{Prefix}/guild", handler.Calls[3].Url);
        }

        [Fact]
        public async Task OpenDirectByIdOrFriendCode()
        {
            var handler = new FakeHandler()
                .Push(200, "{\"data\":" + ConversationJson + "}")
                .Push(200, "{\"data\":" + ConversationJson + "}");
            using var kraty = Make(handler);
            await kraty.Chat.OpenDirectAsync(externalPlayerId: "bob", @as: "alice");
            await kraty.Chat.OpenDirectAsync(friendCode: "ABCD-1234", @as: "alice");
            Assert.EndsWith($"{Prefix}/direct", handler.Calls[0].Url);
            var byId = JObject.Parse(handler.Calls[0].Body!);
            Assert.Equal("bob", byId["externalPlayerId"]!.ToString());
            Assert.Null(byId["friendCode"]);
            var byCode = JObject.Parse(handler.Calls[1].Body!);
            Assert.Equal("ABCD-1234", byCode["friendCode"]!.ToString());
            Assert.Null(byCode["externalPlayerId"]);

            await Assert.ThrowsAsync<ArgumentException>(() => kraty.Chat.OpenDirectAsync(@as: "alice"));
            await Assert.ThrowsAsync<ArgumentException>(() => kraty.Chat.OpenDirectAsync("bob", "ABCD", @as: "alice"));
        }

        [Fact]
        public async Task GroupLifecycleRoutesAndBodies()
        {
            var handler = new FakeHandler()
                .Push(201, "{\"data\":" + ConversationJson + "}")
                .Push(200, "{\"data\":" + ConversationJson + "}")
                .Push(200, "{\"data\":" + ConversationJson + "}")
                .Push(200, "{\"data\":" + ConversationJson + "}")
                .Push(200, "{\"data\":{\"conversationId\":\"" + C + "\",\"removed\":\"bob\"}}")
                .Push(200, "{\"data\":{\"conversationId\":\"" + C + "\",\"left\":true}}");
            using var kraty = Make(handler);

            await kraty.Chat.CreateGroupAsync("Squad", new[] { "bob", "carol" },
                new Dictionary<string, object?> { ["Emblem"] = "owl" }, @as: "alice");
            var create = JObject.Parse(handler.Calls[0].Body!);
            Assert.EndsWith($"{Prefix}/groups", handler.Calls[0].Url);
            Assert.Equal("Squad", create["title"]!.ToString());
            Assert.Equal(new[] { "bob", "carol" }, create["externalPlayerIds"]!.ToObject<string[]>());
            Assert.Equal("owl", create["metadata"]!["Emblem"]!.ToString());

            await kraty.Chat.UpdateGroupAsync(C, title: "Squad 2", @as: "alice");
            Assert.Equal("PATCH", handler.Calls[1].Method);
            Assert.EndsWith($"{Prefix}/conversations/{C}", handler.Calls[1].Url);
            var patch = JObject.Parse(handler.Calls[1].Body!);
            Assert.Equal("Squad 2", patch["title"]!.ToString());
            Assert.Null(patch["metadata"]);

            await kraty.Chat.AddMembersAsync(C, new[] { "dave" }, @as: "alice");
            Assert.EndsWith($"{Prefix}/conversations/{C}/members", handler.Calls[2].Url);
            Assert.Equal("dave", JObject.Parse(handler.Calls[2].Body!)["externalPlayerIds"]![0]!.ToString());

            await kraty.Chat.SetRoleAsync(C, "bob", "admin", @as: "alice");
            Assert.Equal("PUT", handler.Calls[3].Method);
            Assert.EndsWith($"{Prefix}/conversations/{C}/members/bob/role", handler.Calls[3].Url);
            Assert.Equal("admin", JObject.Parse(handler.Calls[3].Body!)["role"]!.ToString());

            var removed = await kraty.Chat.RemoveMemberAsync(C, "bob", @as: "alice");
            Assert.Equal("DELETE", handler.Calls[4].Method);
            Assert.EndsWith($"{Prefix}/conversations/{C}/members/bob", handler.Calls[4].Url);
            Assert.Equal("bob", removed.Removed);

            var left = await kraty.Chat.LeaveAsync(C, @as: "alice");
            Assert.EndsWith($"{Prefix}/conversations/{C}/leave", handler.Calls[5].Url);
            Assert.True(left.Left);
        }

        [Fact]
        public async Task CreateGroupDefaultsToNoInvitees()
        {
            var handler = new FakeHandler().Push(201, "{\"data\":" + ConversationJson + "}");
            using var kraty = Make(handler);
            await kraty.Chat.CreateGroupAsync("Solo", @as: "alice");
            var body = JObject.Parse(handler.Calls[0].Body!);
            Assert.Empty(body["externalPlayerIds"]!);
            Assert.Null(body["metadata"]);
        }

        [Fact]
        public async Task MessagesPagingReadTypingAndDelete()
        {
            var handler = new FakeHandler()
                .Push(200, "{\"data\":{\"messages\":[" + MessageJson + "],\"hasMore\":true}}")
                .Push(200, "{\"data\":{\"messages\":[],\"hasMore\":false}}")
                .Push(200, "{\"data\":{\"lastReadSeq\":7}}")
                .Push(200, "{\"data\":{\"ok\":true}}")
                .Push(200, "{\"data\":{\"messageId\":\"" + M + "\",\"deleted\":true}}");
            using var kraty = Make(handler);

            var page = await kraty.Chat.MessagesAsync(C, before: 50, limit: 25, @as: "alice");
            Assert.EndsWith($"{Prefix}/conversations/{C}/messages?before=50&limit=25", handler.Calls[0].Url);
            Assert.True(page.HasMore);
            Assert.Equal(7, page.Messages[0].Seq);
            Assert.Null(page.Messages[0].EditedAt);

            await kraty.Chat.MessagesAsync(C, after: 7, @as: "alice");
            Assert.EndsWith($"{Prefix}/conversations/{C}/messages?after=7", handler.Calls[1].Url);

            var read = await kraty.Chat.MarkReadAsync(C, 7, @as: "alice");
            Assert.EndsWith($"{Prefix}/conversations/{C}/read", handler.Calls[2].Url);
            Assert.Equal(7, (long)JObject.Parse(handler.Calls[2].Body!)["seq"]!);
            Assert.Equal(7, read.LastReadSeq);

            await kraty.Chat.TypingAsync(C, @as: "alice");
            Assert.Equal("POST", handler.Calls[3].Method);
            Assert.EndsWith($"{Prefix}/conversations/{C}/typing", handler.Calls[3].Url);

            var deleted = await kraty.Chat.DeleteMessageAsync(C, M, @as: "alice");
            Assert.Equal("DELETE", handler.Calls[4].Method);
            Assert.EndsWith($"{Prefix}/conversations/{C}/messages/{M}", handler.Calls[4].Url);
            Assert.True(deleted.Deleted);

            await Assert.ThrowsAsync<ArgumentException>(() => kraty.Chat.MessagesAsync(C, before: 5, after: 1, @as: "alice"));
        }

        [Fact]
        public async Task EditMessagePatchesTextAndParsesEditedAt()
        {
            var handler = new FakeHandler().Push(200, "{\"data\":" + EditedMessageJson + "}");
            using var kraty = Make(handler, filter: false);

            var edited = await kraty.Chat.EditMessageAsync(C, M, "fixed", @as: "alice");
            var call = handler.Calls[0];
            Assert.Equal("PATCH", call.Method);
            Assert.EndsWith($"{Prefix}/conversations/{C}/messages/{M}", call.Url);
            var body = JObject.Parse(call.Body!);
            Assert.Equal("fixed", body["text"]!.ToString());
            // Only `text` plus the client's auto idempotencyKey (the backend strips it).
            Assert.Equal(new[] { "idempotencyKey", "text" }, body.Properties().Select(p => p.Name).OrderBy(n => n));
            Assert.Equal("off", call.Headers["x-kraty-chat-filter"]);
            Assert.Equal("fixed", edited.Text);
            Assert.Equal("2026-10-15T12:01:00.000Z", edited.EditedAt);

            await Assert.ThrowsAsync<ArgumentNullException>(() => kraty.Chat.EditMessageAsync(C, M, null!, @as: "alice"));
        }

        [Fact]
        public async Task EditOutsideWindowSurfacesChatEditNotAllowed()
        {
            var handler = new FakeHandler().Push(403,
                "{\"error\":{\"code\":\"chat_edit_not_allowed\",\"message\":\"the edit window has passed\"}}");
            using var kraty = Make(handler);
            var err = await Assert.ThrowsAsync<KratyApiError>(() => kraty.Chat.EditMessageAsync(C, M, "late", @as: "alice"));
            Assert.Equal(403, err.Status);
            Assert.Equal("chat_edit_not_allowed", err.Code);
        }

        [Fact]
        public async Task PreferencesSendOnlyAssignedFieldsIncludingExplicitNull()
        {
            var handler = new FakeHandler()
                .Push(200, "{\"data\":{\"profanityFilter\":true,\"allowDirectFrom\":\"everyone\"}}")
                .Push(200, "{\"data\":{\"profanityFilter\":null,\"allowDirectFrom\":\"everyone\"}}")
                .Push(200, "{\"data\":{\"profanityFilter\":null,\"allowDirectFrom\":\"nobody\"}}");
            using var kraty = Make(handler);

            var prefs = await kraty.Chat.PreferencesAsync(@as: "alice");
            Assert.True(prefs.ProfanityFilter);
            Assert.EndsWith($"{Prefix}/preferences", handler.Calls[0].Url);

            await kraty.Chat.SetPreferencesAsync(new ChatPreferencesUpdate { ProfanityFilter = null }, @as: "alice");
            var clear = JObject.Parse(handler.Calls[1].Body!);
            Assert.Equal("PUT", handler.Calls[1].Method);
            Assert.Equal(JTokenType.Null, clear["profanityFilter"]!.Type);
            Assert.Null(clear["allowDirectFrom"]);

            var after = await kraty.Chat.SetPreferencesAsync(new ChatPreferencesUpdate { AllowDirectFrom = "nobody" }, @as: "alice");
            var dm = JObject.Parse(handler.Calls[2].Body!);
            Assert.Null(dm["profanityFilter"]);
            Assert.Equal("nobody", dm["allowDirectFrom"]!.ToString());
            Assert.Equal("nobody", after.AllowDirectFrom);

            await Assert.ThrowsAsync<ArgumentException>(() =>
                kraty.Chat.SetPreferencesAsync(new ChatPreferencesUpdate(), @as: "alice"));
        }

        [Fact]
        public async Task ConversationSettingsMuteAndUnmute()
        {
            var handler = new FakeHandler()
                .Push(200, "{\"data\":" + ConversationJson + "}")
                .Push(200, "{\"data\":" + ConversationJson + "}");
            using var kraty = Make(handler);
            await kraty.Chat.SetConversationSettingsAsync(C, new ChatConversationSettings
            {
                MutedUntil = new DateTimeOffset(2026, 10, 16, 9, 30, 0, TimeSpan.FromHours(1)),
            }, @as: "alice");
            var mute = JObject.Parse(handler.Calls[0].Body!);
            Assert.Equal("PUT", handler.Calls[0].Method);
            Assert.EndsWith($"{Prefix}/conversations/{C}/settings", handler.Calls[0].Url);
            Assert.Contains("\"mutedUntil\":\"2026-10-16T08:30:00.000Z\"", handler.Calls[0].Body!);
            Assert.Null(mute["hidden"]);

            await kraty.Chat.SetConversationSettingsAsync(C, new ChatConversationSettings { MutedUntil = null, Hidden = true }, @as: "alice");
            var unmute = JObject.Parse(handler.Calls[1].Body!);
            Assert.Equal(JTokenType.Null, unmute["mutedUntil"]!.Type);
            Assert.True((bool)unmute["hidden"]!);
        }

        [Fact]
        public async Task ChatDisabledSurfacesAsApiError()
        {
            var handler = new FakeHandler().Push(403,
                "{\"error\":{\"code\":\"chat_disabled\",\"message\":\"chat is not enabled for this game\"}}");
            using var kraty = Make(handler);
            var err = await Assert.ThrowsAsync<KratyApiError>(() => kraty.Chat.ConversationsAsync(@as: "alice"));
            Assert.Equal(403, err.Status);
            Assert.Equal("chat_disabled", err.Code);
        }

        [Fact]
        public async Task LiveStreamSendsChannelsAndFilterAndDispatchesTypedEvents()
        {
            var sse =
                ": ping 1\n\n" +
                "event: ready\ndata: {\"externalPlayerId\":\"alice\",\"conversationIds\":[\"" + C + "\"],\"channels\":[{\"key\":\"global\",\"conversationId\":\"g1\"}]}\n\n" +
                "event: message\ndata: " + MessageJson + "\n\n" +
                "event: message_edited\ndata: " + EditedMessageJson + "\n\n" +
                "event: message_deleted\ndata: {\"conversationId\":\"" + C + "\",\"messageId\":\"" + M + "\",\"seq\":7}\n\n" +
                "event: typing\ndata: {\"conversationId\":\"" + C + "\",\"player\":{\"externalPlayerId\":\"bob\",\"displayIdentity\":null}}\n\n" +
                "event: read\ndata: {\"conversationId\":\"" + C + "\",\"externalPlayerId\":\"bob\",\"seq\":7}\n\n" +
                "event: conversation_added\ndata: {\"conversationId\":\"c2\",\"kind\":\"group\"}\n\n" +
                "event: conversation_updated\ndata: {\"conversationId\":\"c2\",\"title\":\"Squad\",\"metadata\":{\"k\":1},\"memberCount\":3}\n\n" +
                "event: conversation_removed\ndata: {\"conversationId\":\"c2\",\"reason\":\"kicked\"}\n\n" +
                "event: restriction\ndata: {\"restrictionId\":\"r1\",\"kind\":\"chat_mute\",\"reason\":\"spam\",\"expiresAt\":\"2026-10-16T00:00:00.000Z\"}\n\n" +
                "event: restriction_lifted\ndata: {\"restrictionId\":\"r1\",\"kind\":\"chat_mute\"}\n\n" +
                "event: close\ndata: {\"reason\":\"draining\"}\n\n";
            var handler = new FakeHandler().Push(200, sse);
            using var kraty = Make(handler, filter: true);

            var seen = new List<string>();
            var closed = new TaskCompletionSource<ChatCloseEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
            ChatStreamReady? ready = null;
            ChatMessage? message = null;
            ChatMessage? edited = null;
            ChatMessageDeletedEvent? deleted = null;
            ChatTypingEvent? typing = null;
            ChatReadEvent? read = null;
            ChatConversationAddedEvent? added = null;
            ChatConversationUpdatedEvent? updated = null;
            ChatConversationRemovedEvent? removed = null;
            ChatRestrictionEvent? restriction = null;
            ChatRestrictionLiftedEvent? lifted = null;
            Exception? error = null;

            var stream = await kraty.Chat.LiveAsync(new[] { "global", "trade" }, s =>
            {
                s.OnEvent = e => seen.Add(e.Kind);
                s.OnReady = r => ready = r;
                s.OnMessage = m => message = m;
                s.OnMessageEdited = m => edited = m;
                s.OnMessageDeleted = d => deleted = d;
                s.OnTyping = t => typing = t;
                s.OnRead = r => read = r;
                s.OnConversationAdded = a => added = a;
                s.OnConversationUpdated = u => updated = u;
                s.OnConversationRemoved = r => removed = r;
                s.OnRestriction = r => restriction = r;
                s.OnRestrictionLifted = l => lifted = l;
                s.OnClose = c => closed.TrySetResult(c);
                s.OnError = e => error = e;
            }, @as: "alice");

            var close = await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await stream.CancelAsync();

            var call = handler.Calls[0];
            Assert.EndsWith($"{Prefix}/stream?channels=global,trade&filter=on", call.Url);
            Assert.Equal("on", call.Headers["x-kraty-chat-filter"]);
            Assert.Equal("text/event-stream", call.Headers["accept"]);
            Assert.Equal("ps", call.Headers["x-player-secret"]);

            Assert.Null(error);
            Assert.Equal(new[]
            {
                "ready", "message", "message_edited", "message_deleted", "typing", "read", "conversation_added",
                "conversation_updated", "conversation_removed", "restriction", "restriction_lifted", "close",
            }, seen);
            Assert.Equal("g1", ready!.Channels[0].ConversationId);
            Assert.Same(ready, stream.Ready);
            Assert.Equal(C, ready.ConversationIds[0]);
            Assert.Equal(7, message!.Seq);
            Assert.Equal("2026-10-15T12:00:00.000Z", message.CreatedAt);
            Assert.Null(message.EditedAt);
            Assert.Equal(M, edited!.MessageId);
            Assert.Equal("fixed", edited.Text);
            Assert.Equal("2026-10-15T12:01:00.000Z", edited.EditedAt);
            Assert.Equal(M, deleted!.MessageId);
            Assert.Equal("bob", typing!.Player.ExternalPlayerId);
            Assert.Equal(7, read!.Seq);
            Assert.Equal("group", added!.Kind);
            Assert.Equal(3, updated!.MemberCount);
            Assert.Equal(1, (int)updated.Metadata["k"]);
            Assert.Equal("kicked", removed!.Reason);
            Assert.Equal("2026-10-16T00:00:00.000Z", restriction!.ExpiresAt);
            Assert.Equal("chat_mute", lifted!.Kind);
            Assert.Equal("draining", close.Reason);
        }

        [Fact]
        public async Task LiveStreamWithoutChannelsOrFilterHasNoQuery()
        {
            var handler = new FakeHandler().Push(200, "event: close\ndata: {\"reason\":\"draining\"}\n\n");
            using var kraty = Make(handler);
            var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var stream = await kraty.Chat.LiveAsync(configure: s => s.OnClose = _ => closed.TrySetResult(true), @as: "alice");
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await stream.CancelAsync();
            Assert.EndsWith($"{Prefix}/stream", handler.Calls[0].Url);
            Assert.False(handler.Calls[0].Headers.ContainsKey("x-kraty-chat-filter"));
        }

        [Fact]
        public async Task LiveStreamOpenFailureMapsErrorEnvelope()
        {
            var handler = new FakeHandler().Push(400,
                "{\"error\":{\"code\":\"validation_failed\",\"message\":\"follow at most 10 channels per stream\"}}");
            using var kraty = Make(handler);
            var err = await Assert.ThrowsAsync<KratyApiError>(() => kraty.Chat.LiveAsync(new[] { "a" }, @as: "alice"));
            Assert.Equal("validation_failed", err.Code);
        }

        [Fact]
        public async Task CallbackExceptionsGoToOnError()
        {
            var handler = new FakeHandler().Push(200,
                "event: message\ndata: " + MessageJson + "\n\nevent: close\ndata: {\"reason\":\"draining\"}\n\n");
            using var kraty = Make(handler);
            var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Exception? error = null;
            var stream = await kraty.Chat.LiveAsync(configure: s =>
            {
                s.OnMessage = _ => throw new InvalidOperationException("boom");
                s.OnError = e => error = e;
                s.OnClose = _ => closed.TrySetResult(true);
            }, @as: "alice");
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await stream.CancelAsync();
            Assert.IsType<InvalidOperationException>(error);
        }
    }
}
