using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Kraty
{
    /// <summary>Who caused an inventory change.</summary>
    public static class InventoryEventOrigin
    {
        public const string Client = "client";
        public const string Server = "server";
        public const string Admin = "admin";
        public const string Engine = "engine";
    }

    /// <summary>
    /// One event off the inventory stream. <see cref="Kind"/> is the SSE
    /// <c>event:</c> line:
    /// <list type="bullet">
    /// <item><description><c>ready</c>: handshake, once the subscription is wired.</description></item>
    /// <item><description><c>inventory_changed</c>: an item quantity moved.</description></item>
    /// <item><description><c>wallet_changed</c>: a currency / progression balance moved.</description></item>
    /// <item><description><c>grant_created</c>: a reward became claimable.</description></item>
    /// </list>
    /// The typed properties cover the common fields; read <see cref="Data"/>
    /// directly for anything a newer backend adds.
    /// </summary>
    public sealed class InventoryStreamEvent
    {
        public string Kind { get; }
        public Dictionary<string, JToken> Data { get; }

        public InventoryStreamEvent(string kind, Dictionary<string, JToken> data)
        {
            Kind = kind;
            Data = data;
        }

        /// <summary><c>client</c> / <c>server</c> / <c>admin</c> / <c>engine</c>; empty on <c>ready</c>.</summary>
        public string Origin => Str("origin");

        /// <summary>Set on <c>inventory_changed</c>.</summary>
        public string ItemKey => Str("itemKey");

        /// <summary>Set on <c>wallet_changed</c>.</summary>
        public string EconomyKey => Str("economyKey");

        /// <summary>Signed change; negative for a consume. 0 when not applicable.</summary>
        public int Delta => Int("delta");

        /// <summary>Quantity AFTER the change (<c>inventory_changed</c>).</summary>
        public int Quantity => Int("quantity");

        /// <summary>Balance AFTER the change (<c>wallet_changed</c>).</summary>
        public int Balance => Int("balance");

        /// <summary>Ledger reason (<c>grant_deposit</c>, <c>consume</c>, <c>admin_grant</c>, …).</summary>
        public string Reason => Str("reason");

        /// <summary>Set on <c>grant_created</c>; claim it with <c>Grants.CollectAllAsync()</c>.</summary>
        public string GrantId => Str("grantId");

        /// <summary>True when this event is the echo of a write this client made.</summary>
        public bool IsOwnWrite => Origin == InventoryEventOrigin.Client;

        private string Str(string key) =>
            Data.TryGetValue(key, out var v) && v.Type == JTokenType.String
                ? ((string?)v ?? string.Empty)
                : string.Empty;

        private int Int(string key) =>
            Data.TryGetValue(key, out var v) &&
            (v.Type == JTokenType.Integer || v.Type == JTokenType.Float)
                ? (int)v
                : 0;
    }

    /// <summary>
    /// Handle to an active inventory subscription. Hook
    /// <see cref="OnEvent"/> / <see cref="OnError"/>, call
    /// <see cref="CancelAsync"/> to stop.
    ///
    /// <para>
    /// Callbacks fire on the HTTP background thread. In Unity, marshal to
    /// the main thread before touching <c>UnityEngine</c> APIs:
    /// </para>
    /// <code>
    /// stream.OnEvent = ev => mainThreadDispatcher.Enqueue(() => RefreshBackpack(ev));
    /// </code>
    ///
    /// <para>
    /// Reconnects on its own by default (<see cref="StreamOptions.AutoReconnect"/>):
    /// after a server <c>close</c>, a drop, a busy server or 40 s of silence
    /// it reopens, re-delivering <c>ready</c>; <see cref="OnConnection"/>
    /// reports it. Changes made while reconnecting are not replayed: re-read
    /// on <c>Connected</c>. With <c>AutoReconnect = false</c>, surface errors
    /// via <see cref="OnError"/> and re-invoke
    /// <see cref="InventoryClient.LiveAsync"/> after a backoff yourself.
    /// </para>
    /// </summary>
    public sealed class InventoryStream : IDisposable
    {
        private readonly CancellationTokenSource _cts;
        private Task _readLoop = Task.CompletedTask;
        private readonly HttpResponseMessage? _response;
        private int _disposed;

        public Action<InventoryStreamEvent>? OnEvent { get; set; }
        /// <summary>
        /// Parse failures and exceptions thrown by your callbacks. With
        /// auto-reconnect (the default) also the error that stopped the stream
        /// for good (a refused reopen: 401, 404, …); drops are handled for you.
        /// Without it, transport failures too.
        /// </summary>
        public Action<Exception>? OnError { get; set; }
        /// <summary>
        /// Auto-reconnect status: <c>Connected</c> after every <c>ready</c>,
        /// <c>Reconnecting</c> (reason, delay, attempt) each time the
        /// connection is lost. Not an error.
        /// </summary>
        public Action<StreamConnectionEvent>? OnConnection { get; set; }

        /// <summary>True when this stream reconnects on its own (<see cref="StreamOptions.AutoReconnect"/>).</summary>
        public bool AutoReconnect { get; }

        internal InventoryStream(HttpResponseMessage response, CancellationTokenSource cts, Func<InventoryStream, Task> startReadLoop)
        {
            _response = response;
            _cts = cts;
            // Start the read loop on the threadpool; it runs until the
            // server closes the stream OR the consumer cancels.
            _readLoop = Task.Run(() => startReadLoop(this));
        }

        // Auto-reconnect: the loop owns the connections; Start runs it once
        // the caller's callbacks are wired.
        internal InventoryStream(CancellationTokenSource cts)
        {
            _cts = cts;
            AutoReconnect = true;
        }

        internal void Start(Func<Task> loop)
        {
            _readLoop = Task.Run(loop);
        }

        /// <summary>
        /// Cancels the subscription + closes the HTTP socket. Idempotent.
        /// </summary>
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

        internal void EmitEvent(InventoryStreamEvent ev)
        {
            try { OnEvent?.Invoke(ev); }
            catch (Exception cbErr) { try { OnError?.Invoke(cbErr); } catch { /* swallow */ } }
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
    }

    /// <summary>
    /// Opens an SSE subscription to a player's inventory / wallet / grants.
    /// Auto-reconnects (see <see cref="StreamOptions"/>) unless told not to.
    /// Transport + parsing live in <see cref="SseReader"/>.
    /// </summary>
    internal static class InventoryStreamFactory
    {
        public static async Task<InventoryStream> OpenAsync(
            HttpClient http,
            string baseUrl,
            string externalPlayerId,
            string authHeader,
            string? playerSecret,
            CancellationToken cancellationToken,
            StreamOptions? options = null,
            Action<InventoryStream>? configure = null
        )
        {
            var url = $"{baseUrl}/sdk/v1/players/{Uri.EscapeDataString(externalPlayerId)}/inventory/stream";
            options ??= new StreamOptions();
            Task<HttpResponseMessage> Open(CancellationToken token) =>
                SseReader.OpenAsync(http, url, authHeader, playerSecret, "inventory stream", token);

            if (!options.AutoReconnect)
            {
                var response = await Open(cancellationToken).ConfigureAwait(false);
                var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var single = new InventoryStream(response, cts, (self) =>
                    SseReader.PumpAsync(
                        response,
                        cts,
                        (kind, data) => self.EmitEvent(new InventoryStreamEvent(kind, data)),
                        self.EmitError
                    )
                );
                configure?.Invoke(single);
                return single;
            }

            var autoCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var stream = new InventoryStream(autoCts);
            (HttpResponseMessage? Response, Exception? Error) first;
            try
            {
                configure?.Invoke(stream);
                first = await SseReconnectLoop.OpenFirstAsync(Open, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                autoCts.Dispose();
                throw;
            }
            var loop = new SseReconnectLoop
            {
                Open = Open,
                Deliver = (kind, data) => stream.EmitEvent(new InventoryStreamEvent(kind, data)),
                Status = stream.EmitConnection,
                Error = stream.EmitError,
                Options = options,
            };
            stream.Start(() => loop.RunAsync(first.Response, first.Error, autoCts.Token));
            return stream;
        }
    }
}
