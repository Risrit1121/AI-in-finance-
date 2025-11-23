using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using cAlgo.API;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(AccessRights = AccessRights.None)]
    public class TriArbLatencyMonitor10Curr : Robot
    {
        #region Parameters and Fields
        private Dictionary<string, int> currencyIndex;
        private Dictionary<int, string> indexCurrency;
        private (double bid, double ask)[,] exchangeRates;
        private double volume;
        private HashSet<string> availableSymbols;

        private StreamWriter writer;
        private Stopwatch stopwatch;
        private double totalMs;
        private int tickCount;
        private double minMs = double.MaxValue;
        private double maxMs = double.MinValue;

        private int goodTrades;
        private int badTrades;
        private double commissionPerMillion;
        #endregion

        #region OnStart
        protected override void OnStart()
        {
            InitializeCurrencies();
            InitializeExchangeMatrix();
            InitializeSymbols();
            InitializeWriter();
            InitializeStopwatch();
            InitializeTrades();

            volume = 1000000;
            commissionPerMillion = 30;

            Print("⚡ TriArbLatencyMonitor10Curr started");
        }

        private void InitializeCurrencies()
        {
            currencyIndex = new Dictionary<string, int>();
            indexCurrency = new Dictionary<int, string>();

            string[] currs = { "USD", "EUR", "JPY", "GBP", "CNY", "AUD", "CAD", "CHF", "HKD", "SGD" };
            for (int i = 0; i < currs.Length; i++)
            {
                currencyIndex[currs[i]] = i;
                indexCurrency[i] = currs[i];
            }
        }

        private void InitializeExchangeMatrix()
        {
            exchangeRates = new (double bid, double ask)[10, 10];
            for (int i = 0; i < 10; i++)
            {
                for (int j = 0; j < 10; j++)
                {
                    exchangeRates[i, j] = (0.0, 0.0);
                }
            }
        }

        private void InitializeSymbols()
        {
            availableSymbols = new HashSet<string>();
            foreach (var symbol in Symbols)
                availableSymbols.Add(symbol);
        }

        private void InitializeWriter()
        {
            writer = new StreamWriter("latency_monitor_10curr.csv", false);
            writer.WriteLine("time,c1,c2,c3,arb,latency_ms,label");
            writer.Flush();
        }

        private void InitializeStopwatch()
        {
            stopwatch = new Stopwatch();
        }

        private void InitializeTrades()
        {
            goodTrades = 0;
            badTrades = 0;
            totalMs = 0.0;
            tickCount = 0;
        }
        #endregion

        #region OnTick
protected override void OnTick()
{
    if (Server.Time.Hour >= 4) return;

    stopwatch.Restart();

    UpdateExchangeRates();
    ProcessTriangles();

    stopwatch.Stop();
    double elapsed = stopwatch.Elapsed.TotalMilliseconds;
    totalMs += elapsed;
    tickCount++;
    if (elapsed < minMs) minMs = elapsed;
    if (elapsed > maxMs) maxMs = elapsed;
}
#endregion


        #region Exchange Rates Update
        private void UpdateExchangeRates()
        {
            for (int baseCurr = 0; baseCurr < 10; baseCurr++)
            {
                for (int quoteCurr = 0; quoteCurr < 10; quoteCurr++)
                {
                    if (baseCurr == quoteCurr)
                    {
                        exchangeRates[baseCurr, quoteCurr] = (1.0, 1.0);
                        continue;
                    }

                    string symbolName = indexCurrency[baseCurr] + indexCurrency[quoteCurr];
                    if (!availableSymbols.Contains(symbolName))
                        continue;

                    var sym = Symbols.GetSymbol(symbolName);
                    if (sym != null)
                        exchangeRates[baseCurr, quoteCurr] = (sym.Bid, sym.Ask);
                    else
                        exchangeRates[baseCurr, quoteCurr] = (0.0, 0.0);
                }
            }
        }
        #endregion

       
#region Triangle Processing
private void ProcessTriangles()
{
    for (int c1 = 0; c1 < 10; c1++)
    {
        for (int c2 = 0; c2 < 10; c2++)
        {
            for (int c3 = 0; c3 < 10; c3++)
            {
                if (c1 == c2 || c2 == c3 || c3 == c1)
                    continue;

                TryProcessTriangle(c1, c2, c3);
            }
        }
    }
}

        private void TryProcessTriangle(int c1, int c2, int c3)
        {
            double bid1, ask1, bid2, ask2, bid3, ask3;
            bid1 = ask1 = bid2 = ask2 = bid3 = ask3 = 0.0;

            if (!GetBidAsk(c1, c2, out bid1, out ask1)) return;
            if (!GetBidAsk(c2, c3, out bid2, out ask2)) return;
            if (!GetBidAsk(c3, c1, out bid3, out ask3)) return;

            double arbitrage = ComputeArbitrage(bid1, ask1, bid2, ask2, bid3, ask3);
            if (arbitrage <= 1 + 1e-4) return;

            ExecuteTriangle(c1, c2, c3, bid1, ask1, bid2, ask2, bid3, ask3, arbitrage);
        }

        private bool GetBidAsk(int baseCurr, int quoteCurr, out double bid, out double ask)
        {
            bid = ask = 0.0;

            if (exchangeRates[baseCurr, quoteCurr] != (0.0, 0.0))
            {
                bid = exchangeRates[baseCurr, quoteCurr].bid;
                return true;
            }
            else if (exchangeRates[quoteCurr, baseCurr] != (0.0, 0.0))
            {
                ask = exchangeRates[quoteCurr, baseCurr].ask;
                return true;
            }

            return false;
        }

        private double ComputeArbitrage(double bid1, double ask1, double bid2, double ask2, double bid3, double ask3)
        {
            double arb = 1.0;
            arb *= (bid1 != 0) ? bid1 : 1 / ask1;
            arb *= (bid2 != 0) ? bid2 : 1 / ask2;
            arb *= (bid3 != 0) ? bid3 : 1 / ask3;
            return arb;
        }
        #endregion

        #region Triangle Execution
        private void ExecuteTriangle(int c1, int c2, int c3,
            double bid1, double ask1, double bid2, double ask2, double bid3, double ask3,
            double arbitrage)
        {
            double vol1 = volume;
            double vol2 = 0.0;
            double vol3 = 0.0;
            double pnl = 0.0;
            double commission = 0.0;

            // --- Leg 1
            ExecuteLeg(c1, c2, bid1, ask1, vol1, out vol2, ref pnl, ref commission);

            // --- Leg 2
            ExecuteLeg(c2, c3, bid2, ask2, vol2, out vol3, ref pnl, ref commission);

            // --- Leg 3
            ExecuteLeg(c3, c1, bid3, ask3, vol3, out vol1, ref pnl, ref commission);

            if (pnl > commission)
                goodTrades++;
            else
                badTrades++;

            Print($"Triangle {indexCurrency[c1]}->{indexCurrency[c2]}->{indexCurrency[c3]}->{indexCurrency[c1]}: PnL={pnl:F2}, Commission={commission:F2}");
        }

        private void ExecuteLeg(int baseCurr, int quoteCurr, double bid, double ask,
            double volIn, out double volOut, ref double pnl, ref double commission)
        {
            volOut = 0.0;
            string symbol;
            double minLot, execVol;

            if (bid != 0)
            {
                symbol = indexCurrency[baseCurr] + indexCurrency[quoteCurr];
                var symObj = Symbols.GetSymbol(symbol);
                minLot = symObj.VolumeInUnitsMin;
                execVol = Math.Floor(volIn / minLot) * minLot;
                volOut = execVol * bid;

                ExecuteMarketOrder(TradeType.Sell, symbol, execVol);
                pnl += volOut - volIn;
                commission += volIn * commissionPerMillion / 1e6;

                Print($"LEG SELL {symbol}: execVol={execVol}, bid={bid}, received={volOut}");
            }
            else
            {
                symbol = indexCurrency[quoteCurr] + indexCurrency[baseCurr];
                var symObj = Symbols.GetSymbol(symbol);
                minLot = symObj.VolumeInUnitsMin;
                execVol = Math.Floor(volIn / minLot) * minLot;
                volOut = execVol / ask;

                ExecuteMarketOrder(TradeType.Buy, symbol, execVol);
                pnl += volOut * ask - volIn;
                commission += volIn * commissionPerMillion / 1e6;

                Print($"LEG BUY {symbol}: execVol={execVol}, ask={ask}, spent={volIn}");
            }
        }
        #endregion

        #region Latency Logging
        private void LogLatency(double elapsed)
        {
            int label = (elapsed <= 5.0) ? 1 : 0;
            writer.WriteLine($"{Server.Time:o},,,,{elapsed:F3},{label}");
            writer.Flush();

            if (elapsed > 5.0)
                Print($"⚠ High latency: {elapsed:F3} ms");
        }
        #endregion

        #region OnStop
        protected override void OnStop()
        {
            writer?.Close();

            Print($"Good Trades: {goodTrades}, Bad Trades: {badTrades}");
            if (tickCount > 0)
            {
                double avg = totalMs / tickCount;
                Print($"Latency over {tickCount} ticks: Avg={avg:F3} ms, Min={minMs:F3} ms, Max={maxMs:F3} ms");
            }
        }
        #endregion
    }
}
