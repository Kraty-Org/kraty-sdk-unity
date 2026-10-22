using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Kraty.Tests
{
    /// <summary>GuildsClient: URL shaping, bodies, unwrapping and the null guild.</summary>
    public sealed class GuildsTests
    {
        private const string ApiKey = "pUUVdrM8.djr4-0Iv9h1JvVNSMZNDmSsSN7lSVq2F9dG6DG4A5uQ";
        private const string BaseUrl = "https://api.test.kraty.io";
        private const string G = "0b8a6c56-49bd-4d5a-9d38-1f2b6a5f9b3e";

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

        [Fact]
        public async Task MineReturnsNullWhenGuildless()
        {
            var handler = new FakeHandler().Push(200, "{\"data\":null}");
            using var kraty = Make(handler);
            Assert.Null(await kraty.Guilds.MineAsync(@as: "alice"));
            Assert.EndsWith("/sdk/v1/players/alice/guild", handler.Calls[0].Url);
        }

        [Fact]
        public async Task MineUnwrapsRosterAndRole()
        {
            var handler = new FakeHandler().Push(200,
                "{\"data\":{\"guild\":{\"guildId\":\"" + G + "\",\"name\":\"Owls\",\"joinType\":\"open\",\"memberCount\":2," +
                "\"maxMembers\":50,\"metadata\":{},\"leader\":null,\"createdAt\":\"x\"," +
                "\"members\":[{\"externalPlayerId\":\"alice\",\"role\":\"leader\",\"joinedAt\":\"x\",\"online\":true,\"weeklyHelps\":1}]}," +
                "\"membership\":{\"role\":\"leader\",\"joinedAt\":\"x\",\"permissions\":[\"kick\"]}}}");
            using var kraty = Make(handler);
            var mine = await kraty.Guilds.MineAsync(@as: "alice");
            Assert.NotNull(mine);
            Assert.Equal(50, mine!.Guild.MaxMembers);
            Assert.Equal("leader", mine.Guild.Members[0].Role);
            Assert.Equal("kick", mine.Membership.Permissions[0]);
            Assert.Equal(1, mine.Guild.Members[0].WeeklyHelps);
        }

        [Fact]
        public async Task SearchBuildsQueryAndUnwrapsList()
        {
            var handler = new FakeHandler().Push(200, "{\"data\":{\"guilds\":[{\"guildId\":\"" + G + "\",\"name\":\"Owls\"}]}}");
            using var kraty = Make(handler);
            var list = await kraty.Guilds.SearchAsync("night owls", country: "US", limit: 5, @as: "alice");
            Assert.Single(list);
            // FakeHandler records the decoded URI.
            Assert.EndsWith("/sdk/v1/players/alice/guilds?q=night owls&country=US&limit=5", handler.Calls[0].Url);
        }

        [Fact]
        public async Task CreateAndJoinSendBodies()
        {
            var handler = new FakeHandler()
                .Push(201, "{\"data\":{\"guild\":{\"guildId\":\"" + G + "\"},\"membership\":{\"role\":\"leader\"}}}")
                .Push(201, "{\"data\":{\"status\":\"requested\",\"request\":{\"requestId\":\"r1\",\"guild\":{},\"status\":\"pending\"}}}");
            using var kraty = Make(handler);
            var created = await kraty.Guilds.CreateAsync(new GuildProfileInput { Name = "Owls", JoinType = "request" }, @as: "alice");
            Assert.Equal(G, created.Guild.GuildId);
            var joined = await kraty.Guilds.JoinAsync(G, message: "hi", @as: "bob");
            Assert.Equal("requested", joined.Status);
            Assert.Equal("r1", joined.Request!.RequestId);
            var createBody = JObject.Parse(handler.Calls[0].Body!);
            Assert.Equal("Owls", createBody["name"]!.ToString());
            Assert.Null(createBody["description"]);
            Assert.EndsWith($"/sdk/v1/players/bob/guilds/{G}/join", handler.Calls[1].Url);
            Assert.Equal("hi", JObject.Parse(handler.Calls[1].Body!)["message"]!.ToString());
        }

        [Fact]
        public async Task OfficerActionsAndHelp()
        {
            var handler = new FakeHandler()
                .Push(200, "{\"data\":{\"guildId\":\"" + G + "\",\"members\":[]}}")
                .Push(200, "{\"data\":{\"guildId\":\"" + G + "\",\"members\":[]}}")
                .Push(201, "{\"data\":{\"donationId\":\"d1\",\"requestId\":\"h1\",\"amount\":1,\"grantId\":\"g1\",\"request\":{\"requestId\":\"h1\"},\"week\":{}}}");
            using var kraty = Make(handler);
            await kraty.Guilds.KickAsync("bob 1", @as: "alice");
            await kraty.Guilds.TransferLeadershipAsync("carol", @as: "alice");
            var donation = await kraty.Guilds.DonateAsync("h1", amount: 1, @as: "alice");
            Assert.Equal("d1", donation.DonationId);
            Assert.EndsWith("/sdk/v1/players/alice/guild/members/bob 1/kick", handler.Calls[0].Url);
            Assert.Equal("carol", JObject.Parse(handler.Calls[1].Body!)["externalPlayerId"]!.ToString());
            Assert.EndsWith("/sdk/v1/players/alice/guild/help/h1/donate", handler.Calls[2].Url);
        }
    }
}
