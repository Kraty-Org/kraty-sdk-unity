using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Kraty.Tests
{
    /// <summary>ModerationClient: report bodies, list unwrapping, standing, warning acknowledgement.</summary>
    public sealed class ModerationTests
    {
        private const string ApiKey = "pUUVdrM8.djr4-0Iv9h1JvVNSMZNDmSsSN7lSVq2F9dG6DG4A5uQ";
        private const string BaseUrl = "https://api.test.kraty.io";
        private const string C = "7c1d1e0e-2b7e-4d55-9f8f-0a4b8d1c2e33";
        private const string M = "5a0c7d7a-8c55-4b8e-a0f3-6c2f0f7e9d11";
        private const string R = "9e4f0c1a-1b2c-4d3e-8f90-123456789abc";

        private static Kraty Make(FakeHandler handler) => new Kraty(new KratyClientOptions
        {
            ApiKey = ApiKey,
            BaseUrl = BaseUrl,
            HttpMessageHandler = handler,
            PlayerSecret = "ps",
            Retry = new RetryConfig
            {
                Attempts = 1,
                InitialDelay = TimeSpan.FromMilliseconds(1),
                MaxDelay = TimeSpan.FromMilliseconds(5),
                Jitter = 0,
            },
            Timeout = TimeSpan.FromSeconds(1),
        });

        private const string ReportJson =
            "{\"reportId\":\"rep1\",\"targetExternalPlayerId\":\"bob\",\"category\":\"harassment\",\"context\":\"chat\"," +
            "\"status\":\"open\",\"createdAt\":\"2026-10-15T12:00:00.000Z\",\"updatedAt\":\"2026-10-15T12:00:00.000Z\",\"resolvedAt\":null}";

        private const string RestrictionJson =
            "{\"restrictionId\":\"" + R + "\",\"kind\":\"warning\",\"reason\":\"be nice\",\"source\":\"moderator\"," +
            "\"createdAt\":\"2026-10-15T12:00:00.000Z\",\"expiresAt\":null,\"liftedAt\":null,\"acknowledgedAt\":null,\"active\":true,\"reportId\":\"rep1\"}";

        [Fact]
        public async Task ReportSendsEvidenceAndOmitsNulls()
        {
            var handler = new FakeHandler().Push(201, "{\"data\":" + ReportJson + "}");
            using var kraty = Make(handler);
            var report = await kraty.Moderation.ReportAsync(new ReportPlayerInput
            {
                TargetExternalPlayerId = "bob",
                Category = ReportCategory.Harassment,
                Context = ReportContext.Chat,
                ConversationId = C,
                MessageIds = new List<string> { M },
            }, @as: "alice");

            var call = handler.Calls[0];
            Assert.Equal("POST", call.Method);
            Assert.EndsWith("/sdk/v1/players/alice/reports", call.Url);
            var body = JObject.Parse(call.Body!);
            Assert.Equal("bob", body["targetExternalPlayerId"]!.ToString());
            Assert.Equal("harassment", body["category"]!.ToString());
            Assert.Equal("chat", body["context"]!.ToString());
            Assert.Equal(C, body["conversationId"]!.ToString());
            Assert.Equal(M, body["messageIds"]![0]!.ToString());
            Assert.Null(body["comment"]);
            Assert.Null(body["guildId"]);
            Assert.NotNull(body["idempotencyKey"]);

            Assert.Equal("rep1", report.ReportId);
            Assert.Equal("open", report.Status);
            Assert.Null(report.ResolvedAt);
        }

        [Fact]
        public async Task ReportsListUnwrapsWithLimit()
        {
            var handler = new FakeHandler().Push(200, "{\"data\":{\"reports\":[" + ReportJson + "]}}");
            using var kraty = Make(handler);
            var reports = await kraty.Moderation.ReportsAsync(limit: 5, @as: "alice");
            Assert.EndsWith("/sdk/v1/players/alice/reports?limit=5", handler.Calls[0].Url);
            Assert.Single(reports);
            Assert.Equal("bob", reports[0].TargetExternalPlayerId);
        }

        [Fact]
        public async Task StatusParsesStanding()
        {
            var handler = new FakeHandler().Push(200,
                "{\"data\":{\"canChat\":false,\"canPost\":false,\"mutedUntil\":null,\"chatBannedUntil\":\"2026-11-01T00:00:00.000Z\"," +
                "\"restrictions\":[" + RestrictionJson + "]}}");
            using var kraty = Make(handler);
            var status = await kraty.Moderation.StatusAsync(@as: "alice");
            Assert.EndsWith("/sdk/v1/players/alice/moderation/status", handler.Calls[0].Url);
            Assert.False(status.CanChat);
            Assert.Equal("2026-11-01T00:00:00.000Z", status.ChatBannedUntil);
            Assert.Equal("warning", status.Restrictions[0].Kind);
            Assert.Equal("moderator", status.Restrictions[0].Source);
            Assert.True(status.Restrictions[0].Active);
            Assert.Equal("rep1", status.Restrictions[0].ReportId);
        }

        [Fact]
        public async Task AcknowledgeWarningPostsToItsRoute()
        {
            var handler = new FakeHandler().Push(200,
                "{\"data\":" + RestrictionJson.Replace("\"acknowledgedAt\":null", "\"acknowledgedAt\":\"2026-10-15T13:00:00.000Z\"") + "}");
            using var kraty = Make(handler);
            var r = await kraty.Moderation.AcknowledgeWarningAsync(R, @as: "alice");
            Assert.Equal("POST", handler.Calls[0].Method);
            Assert.EndsWith($"/sdk/v1/players/alice/moderation/warnings/{R}/acknowledge", handler.Calls[0].Url);
            Assert.Equal("2026-10-15T13:00:00.000Z", r.AcknowledgedAt);
        }

        [Fact]
        public async Task NotFoundWarningSurfacesAsApiError()
        {
            var handler = new FakeHandler().Push(404, "{\"error\":{\"code\":\"not_found\",\"message\":\"warning not found\"}}");
            using var kraty = Make(handler);
            var err = await Assert.ThrowsAsync<KratyApiError>(() => kraty.Moderation.AcknowledgeWarningAsync(R, @as: "alice"));
            Assert.True(err.IsNotFound);
        }
    }
}
