// ================================================================
//  Triangular Arbitrage Bot - Extended Version for Backtesting
//  Author: (Your Name)
//  Date:   2025
//  Purpose: Detect profitable triangular cycles among a basket
//           of currencies and execute them in cTrader Backtesting.
// ================================================================

using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using cAlgo.API;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class TriangularArbFull : Robot
    {
        // ---------- Currency Universe ----------
        private readonly string[] _currencies =
            { "USD", "EUR", "JPY", "GBP", "CNY", "AUD", "CAD", "CHF", "HKD", "SGD" };

        private Dictionary<string, int> _c2i;
        private Dictionary<int, string> _i2c;

        // matrix of bid/ask quotes
        private (double bid, double ask)[,] _quotes;

        // track notional balances (for reporting)
        private double[] _balances;

        // ---------- Parameters ----------
        private double _startCapital = 1_000_000;         // start with 1M of base currency
        private double _commissionPerMM = 30.0;           // 30 units per million traded
        private double _minProfitFactor = 1.0001;         // need at least this factor to trade
        private int _maxTradesPerTick = 3;                // throttle: no more than N trades per tick

        // ---------- Statistics ----------
        private int _goodTrades;
        private int _badTrades;
        private double _totalCommission;
        private int _totalCycles;
        private Dictionary<string, int> _pairTradeCount;  // track how many times each pair traded

        // ---------- Latency Measurement ----------
        private Stopwatch _timer;
        private double _sumMs;
        private int _tickCount;
        private double _minMs = double.MaxValue;
        private double _maxMs = double.MinValue;

        // ---------- File Logging ----------
        private string _logFilePath;


        // ------------------------------------------------------------
        // OnStart: initialize all data structures
        // ------------------------------------------------------------
        protected override void OnStart()
        {
            // Map currencies to indices
            _c2i = new Dictionary<string, int>();
            _i2c = new Dictionary<int, string>();
            for (int i = 0; i < _currencies.Length; i++)
            {
                _c2i[_currencies[i]] = i;
                _i2c[i] = _currencies[i];
            }

            _quotes = new (double, double)[_currencies.Length, _currencies.Length];
            _balances = new double[_currencies.Length];
            for (int i = 0; i < _balances.Length; i++) _balances[i] = 0;

            _goodTrades = 0;
            _badTrades = 0;
            _totalCommission = 0;
            _totalCycles = 0;

            _pairTradeCount = new Dictionary<string, int>();

            _timer = new Stopwatch();

            // Prepare CSV log file
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            _logFilePath = Path.Combine(dir, "triangular_trades_log.csv");
            using (var sw = new StreamWriter(_logFilePath, false))
            {
                sw.WriteLine("Timestamp,Cycle,Leg1,Leg2,Leg3,Profit,Commission");
            }

            Print("TriangularArbFull Bot Initialized.");
            Print("Trade log will be saved at: {0}", _logFilePath);
        }


        // ------------------------------------------------------------
        // OnTick: called on each incoming tick
        // ------------------------------------------------------------
        protected override void OnTick()
        {
            // Optional: avoid trading after certain hours
            if (Server.Time.Hour >= 4)
                return;

            _timer.Restart();

            RefreshQuotes();

            int executed = 0;

            for (int i = 0; i < _currencies.Length; i++)
            {
                for (int j = 0; j < _currencies.Length; j++)
                {
                    if (i == j) continue;
                    for (int k = 0; k < _currencies.Length; k++)
                    {
                        if (k == i || k == j) continue;

                        if (executed >= _maxTradesPerTick)
                            break;

                        if (TryExecuteCycle(i, j, k))
                            executed++;
                    }
                }
            }

            _timer.Stop();
            double ms = _timer.Elapsed.TotalMilliseconds;
            _sumMs += ms;
            _tickCount++;
            _minMs = Math.Min(_minMs, ms);
            _maxMs = Math.Max(_maxMs, ms);
        }


        // ------------------------------------------------------------
        // RefreshQuotes: update bid/ask matrix for all currency pairs
        // ------------------------------------------------------------
        private void RefreshQuotes()
        {
            int n = _currencies.Length;

            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    if (i == j)
                    {
                        _quotes[i, j] = (1.0, 1.0);
                        continue;
                    }

                    string sym = _currencies[i] + _currencies[j];
                    var s = Symbols.GetSymbol(sym);
                    if (s != null)
                    {
                        _quotes[i, j] = (s.Bid, s.Ask);
                    }
                    else
                    {
                        _quotes[i, j] = (0.0, 0.0);
                    }
                }
            }
        }


        // ------------------------------------------------------------
        // TryExecuteCycle: detect and execute one triangular cycle
        // ------------------------------------------------------------
        private bool TryExecuteCycle(int a, int b, int c)
        {
            if (!GetRate(a, b, out double b1, out double a1)) return false;
            if (!GetRate(b, c, out double b2, out double a2)) return false;
            if (!GetRate(c, a, out double b3, out double a3)) return false;

            // Compute roundtrip multiplier
            double factor = (b1 > 0 ? b1 : 1 / a1)
                          * (b2 > 0 ? b2 : 1 / a2)
                          * (b3 > 0 ? b3 : 1 / a3);

            if (factor <= _minProfitFactor)
                return false;  // not profitable enough

            // We found a cycle worth trading
            ExecuteCycle(a, b, c, b1, a1, b2, a2, b3, a3);
            return true;
        }


        // ------------------------------------------------------------
        // GetRate: read bid/ask for a pair
        // ------------------------------------------------------------
        private bool GetRate(int from, int to, out double bid, out double ask)
        {
            bid = _quotes[from, to].bid;
            ask = _quotes[from, to].ask;
            if (bid == 0 && ask == 0) return false;
            return true;
        }


        // ------------------------------------------------------------
        // ExecuteCycle: simulate 3-leg execution
        // ------------------------------------------------------------
        private void ExecuteCycle(int a, int b, int c,
                                  double b1, double a1,
                                  double b2, double a2,
                                  double b3, double a3)
        {
            _totalCycles++;

            double volA = _startCapital;
            double volB, volC;
            double commission = 0;
            double pnl = 0;

            // ---- leg 1: a → b
            if (b1 > 0)
            {
                volB = volA * b1;
                commission += volA * _commissionPerMM / 1e6;
                pnl += (volB / b1 - volA);
                CountPairTrade(_i2c[a] + _i2c[b]);
            }
            else
            {
                volB = volA / a1;
                commission += volA * _commissionPerMM / 1e6;
                pnl += (volB * a1 - volA);
                CountPairTrade(_i2c[b] + _i2c[a]);
            }

            // ---- leg 2: b → c
            if (b2 > 0)
            {
                volC = volB * b2;
                commission += volB * _commissionPerMM / 1e6;
                pnl += (volC / b2 - volB);
                CountPairTrade(_i2c[b] + _i2c[c]);
            }
            else
            {
                volC = volB / a2;
                commission += volB * _commissionPerMM / 1e6;
                pnl += (volC * a2 - volB);
                CountPairTrade(_i2c[c] + _i2c[b]);
            }

            // ---- leg 3: c → a
            double finalA;
            if (b3 > 0)
            {
                finalA = volC * b3;
                commission += volC * _commissionPerMM / 1e6;
                pnl += (finalA / b3 - volC);
                CountPairTrade(_i2c[c] + _i2c[a]);
            }
            else
            {
                finalA = volC / a3;
                commission += volC * _commissionPerMM / 1e6;
                pnl += (finalA * a3 - volC);
                CountPairTrade(_i2c[a] + _i2c[c]);
            }

            _totalCommission += commission;

            if (pnl > commission)
                _goodTrades++;
            else
                _badTrades++;

            LogTrade(a, b, c, pnl, commission);
        }


        // ------------------------------------------------------------
        // CountPairTrade: count how many times each pair traded
        // ------------------------------------------------------------
        private void CountPairTrade(string pair)
        {
            if (!_pairTradeCount.ContainsKey(pair))
                _pairTradeCount[pair] = 0;
            _pairTradeCount[pair]++;
        }


        // ------------------------------------------------------------
        // LogTrade: write details to CSV + console
        // ------------------------------------------------------------
        private void LogTrade(int a, int b, int c, double pnl, double commission)
        {
            string cycle = $"{_i2c[a]}→{_i2c[b]}→{_i2c[c]}→{_i2c[a]}";
            string ts = Server.Time.ToString("yyyy-MM-dd HH:mm:ss");

            using (var sw = new StreamWriter(_logFilePath, true))
            {
                sw.WriteLine($"{ts},{cycle},{_i2c[a]}{_i2c[b]},{_i2c[b]}{_i2c[c]},{_i2c[c]}{_i2c[a]},{pnl:F2},{commission:F2}");
            }

            Print($"TRADE at {ts}: {cycle} | PnL={pnl:F2} | Comm={commission:F2}");
        }


        // ------------------------------------------------------------
        // OnStop: summarize after backtest
        // ------------------------------------------------------------
        protected override void OnStop()
        {
            Print("==================================================");
            Print("Triangular Arbitrage Backtest Finished");
            Print("Total Cycles Tried : {0}", _totalCycles);
            Print("Good Trades        : {0}", _goodTrades);
            Print("Bad Trades         : {0}", _badTrades);
            Print("Total Commission   : {0:F2}", _totalCommission);
            Print("Unique Pairs Traded: {0}", _pairTradeCount.Count);

            foreach (var kv in _pairTradeCount)
                Print("Pair {0} -> {1} trades", kv.Key, kv.Value);

            if (_tickCount > 0)
            {
                double avg = _sumMs / _tickCount;
                Print("Latency over {0} ticks: Avg={1:F3} ms, Min={2:F3} ms, Max={3:F3} ms",
                    _tickCount, avg, _minMs, _maxMs);
            }

            Print("Trade log saved to {0}", _logFilePath);
            Print("==================================================");
        }
    }
}
