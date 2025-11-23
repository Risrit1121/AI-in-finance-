using System;
using cAlgo.API;
using cAlgo.API.Indicators;
using System.Linq;

namespace cAlgo.Robots
{
    [Robot(AccessRights = AccessRights.None)]
    public class SmaCrossoverBot : Robot
    {
        [Parameter("--- Strategy Parameters ---")]
        public string StrategyParameters { get; set; }

        [Parameter("Fast SMA Period", DefaultValue = 20, MinValue = 1)]
        public int FastSmaPeriod { get; set; }

        [Parameter("Slow SMA Period", DefaultValue = 50, MinValue = 1)]
        public int SlowSmaPeriod { get; set; }

        [Parameter("Volume", DefaultValue = 10000, MinValue = 1)]
        public double Volume { get; set; }

        [Parameter("Label", DefaultValue = "SMACrossoverBot")]
        public string Label { get; set; }

        private SimpleMovingAverage _fastSma;
        private SimpleMovingAverage _slowSma;

        protected override void OnStart()
        {
            // Ensure the fast SMA is shorter than the slow SMA
            if (FastSmaPeriod >= SlowSmaPeriod)
            {
                Print("Error: Fast SMA Period must be less than Slow SMA Period.");
                Stop();
                return;
            }

            // Initialize the SMA indicators
            _fastSma = Indicators.SimpleMovingAverage(Bars.ClosePrices, FastSmaPeriod);
            _slowSma = Indicators.SimpleMovingAverage(Bars.ClosePrices, SlowSmaPeriod);
        }

        // OnBar is used instead of OnTick for crossover strategies
        // because we want to check for signals only on the close of a new bar.
        protected override void OnBar()
        {
            // Check if a position for this bot instance is already open
            var position = Positions.Find(Label, SymbolName);

            // --- Crossover Logic ---
            // We check the values of the SMAs on the two most recently closed bars.
            // Index [Bars.Count - 2] is the previously closed bar.
            // Index [Bars.Count - 3] is the bar before that.
            
            // Ensure we have enough bars to perform the check
            if (Bars.Count < SlowSmaPeriod + 3)
            {
                return;
            }

            double fastSmaPrevious = _fastSma.Result.Last(1);
            double slowSmaPrevious = _slowSma.Result.Last(1);

            double fastSmaTwoBarsAgo = _fastSma.Result.Last(2);
            double slowSmaTwoBarsAgo = _slowSma.Result.Last(2);

            // --- Entry Signal: Golden Cross ---
            // Check if the fast SMA has crossed ABOVE the slow SMA on the last bar close.
            bool goldenCross = fastSmaTwoBarsAgo <= slowSmaTwoBarsAgo && fastSmaPrevious > slowSmaPrevious;

            if (goldenCross && position == null)
            {
                ExecuteMarketOrder(TradeType.Buy, SymbolName, Volume, Label);
                Print($"Golden Cross detected. Opened LONG position at {Symbol.Ask}.");
            }

            // --- Exit Signal: Death Cross ---
            // Check if the fast SMA has crossed BELOW the slow SMA on the last bar close.
            bool deathCross = fastSmaTwoBarsAgo >= slowSmaTwoBarsAgo && fastSmaPrevious < slowSmaPrevious;

            if (deathCross && position != null)
            {
                ClosePosition(position);
                Print($"Death Cross detected. Closed LONG position at {Symbol.Bid}.");
            }
        }
    }
}

