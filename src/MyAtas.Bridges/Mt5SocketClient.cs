using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MyAtas.Bridges
{
    public sealed class Mt5SocketClient : IDisposable
    {
        private readonly TcpClient _tcp = new();
        private readonly NetworkStream _ns;
        private readonly StreamWriter _w;
        private readonly Task _rx;

        public event Action<string>? OnAckLine;

        public Mt5SocketClient(string host = "127.0.0.1", int port = 45000)
        {
            _tcp.Connect(host, port);
            _ns = _tcp.GetStream();
            _w  = new StreamWriter(_ns, new UTF8Encoding(false)) { AutoFlush = true };
            // HELLO
            _w.WriteLine("{\"type\":\"HELLO\",\"role\":\"ATAS\",\"ts_ms\":" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + "}");
            _rx = Task.Run(ReadLoop);
        }

        private async Task ReadLoop()
        {
            using var r = new StreamReader(_ns, Encoding.UTF8, false, 1024, true);
            while (_tcp.Connected)
            {
                var line = await r.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line)) continue;
                OnAckLine?.Invoke(line);
            }
        }

        // Envío de orden con soporte de riesgo por % (el EA lo usa)
        public void SendNewMarketOrder(
            string clientOrderId,
            string symbolSrc, string symbolDst,
            int dir /* +1 buy, -1 sell */,
            double? slPrice, double? tpPrice,
            string riskMode /* "percentAccount"|"none"|"fixedUsd" */,
            double riskValue /* ej. 0.5 para 0.5% */,
            string comment = "ATAS468")
        {
            var payload = new
            {
                role = "ATAS",
                type = "NEW_ORDER",
                clientOrderId,
                symbol_src = symbolSrc,
                symbol_dst = symbolDst,
                side = dir > 0 ? "BUY" : "SELL",
                orderType = "MARKET",
                qty_lots = 0.0,                // el EA lo ignora si riskMode=percentAccount
                sl_price = slPrice,
                tp_price = tpPrice,
                risk_mode = riskMode,
                risk_value = riskValue,
                comment,
                ts_ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            var json = JsonSerializer.Serialize(payload);
            _w.WriteLine(json);
        }

        public void Dispose()
        {
            try { _tcp?.Close(); } catch { }
        }
    }
}
