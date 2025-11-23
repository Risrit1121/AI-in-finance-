using System;
using System.IO;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(AccessRights = AccessRights.FullAccess, AddIndicators = true)]
    public class DirectionalChangeBot_Unified : Robot
    {
        // Define the possible trend directions
        private enum Trend { Up, Down }

        // Define the four modes required for the ablation study
        public enum StrategyModeType { Full_AI, No_Classifier, No_Volatility_Filter, Baseline }

        [Parameter("--- Test Configuration ---")]
        public string TestConfigParams { get; set; }

        [Parameter("Strategy Mode", DefaultValue = StrategyModeType.Full_AI)]
        public StrategyModeType StrategyMode { get; set; }

        [Parameter("--- Strategy: Core DC ---")]
        public string StrategyParameters { get; set; }

        [Parameter("Theta θ (%)", DefaultValue = 0.5, MinValue = 0.01)]
        public double ThresholdPercent { get; set; }

        [Parameter("Volume", DefaultValue = 10000, MinValue = 1)]
        public double Volume { get; set; }

        [Parameter("--- Strategy: Scaling Law Exit ---")]
        public string ScalingLawParams { get; set; }

        [Parameter("Y value for θ'", DefaultValue = 0.5, MinValue = 0.1)]
        public double Y_Value { get; set; }

        [Parameter("--- AI #1: Volatility Filter ---")]
        public string Ai1Parameters { get; set; }

        [Parameter("ATR Period", DefaultValue = 14, MinValue = 1)]
        public int AtrPeriod { get; set; }

        [Parameter("ATR TimeFrame", DefaultValue = "Hour1")]
        public TimeFrame AtrTimeFrame { get; set; }

        [Parameter("Max Volatility (ATR in Pips)", DefaultValue = 20, MinValue = 1)]
        public double VolatilityThresholdPips { get; set; }

        [Parameter("--- AI #2: Entry Quality Classifier ---")]
        public string Ai2Parameters { get; set; }

        [Parameter("SMA Period", DefaultValue = 50, MinValue = 1)]
        public int SmaPeriod { get; set; }

        [Parameter("RSI Period", DefaultValue = 14, MinValue = 1)]
        public int RsiPeriod { get; set; }

        [Parameter("RSI Overbought Threshold", DefaultValue = 70, MinValue = 1)]
        public double RsiOverbought { get; set; }

        [Parameter("--- Other ---")]
        public string OtherParameters { get; set; }

        [Parameter("Label", DefaultValue = "DCBot_Unified")]
        public string Label { get; set; }

        private double _lastExtremePrice;
        private Trend _currentTrend;
        private double _threshold; // This is θ
        private double _maxOvershootValue; // This is maxOSV
        private StreamWriter _csvWriter;
        private string _filePath;

        private AverageTrueRange _atr;
        private SimpleMovingAverage _sma;
        private RelativeStrengthIndex _rsi;
        private MacdCrossOver _macd; // If your cAlgo version does not have MacdCrossOver, replace appropriately
        private StochasticOscillator _stoch;
        private AverageDirectionalMovementIndexRating _adx;

        private bool _isTradingPaused = false;

        // --- Control Flags ---
        private bool _useAtrFilter;
        private bool _useClassifierFilter;

        protected override void OnStart()
        {
            _useAtrFilter = (StrategyMode == StrategyModeType.Full_AI || StrategyMode == StrategyModeType.No_Classifier);
            _useClassifierFilter = (StrategyMode == StrategyModeType.Full_AI || StrategyMode == StrategyModeType.No_Volatility_Filter);

            var logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "cAlgo", "Logs");
            Directory.CreateDirectory(logDirectory);
            _filePath = Path.Combine(logDirectory, $"{SymbolName}_{TimeFrame}_{StrategyMode}_Log_{Server.Time:yyyyMMdd_HHmmss}.csv");

            try
            {
                _csvWriter = new StreamWriter(_filePath, false);
                _csvWriter.WriteLine("Timestamp,Event,Price,ExtremePrice,Trend,CurrentAtrPips,IsTradingPaused,ClassifierSignal,Overshoot,MaxOvershoot,DynamicExitThreshold,Mode,MarketRegime,Extra");
            }
            catch (Exception ex)
            {
                Print($"Failed to open log file: {ex.Message}");
                _csvWriter = null;
            }

            var atrBars = MarketData.GetBars(AtrTimeFrame);
            _atr = Indicators.AverageTrueRange(atrBars, AtrPeriod, MovingAverageType.Simple);
            _sma = Indicators.SimpleMovingAverage(Bars.ClosePrices, SmaPeriod);
            _rsi = Indicators.RelativeStrengthIndex(Bars.ClosePrices, RsiPeriod);
            _macd = Indicators.MacdCrossOver(Bars.ClosePrices, 12, 26, 9);
            _stoch = Indicators.StochasticOscillator(9, 3, 9, MovingAverageType.Simple);
            _adx = Indicators.AverageDirectionalMovementIndexRating(14);

            _threshold = ThresholdPercent / 100.0;
            _lastExtremePrice = Symbol.Bid;
            _currentTrend = Trend.Down;
            _maxOvershootValue = 0.0001;

            Print($"Unified DC Bot Started. Mode: {StrategyMode}");
            Print($"Logging data to: {_filePath}");

            LogData("BotStart", Symbol.Bid, _lastExtremePrice, _currentTrend.ToString(), "", 0, 0, 0, "Unknown");
        }

        protected override void OnTick()
        {
            try
            {
                double currentAsk = Symbol.Ask;
                double currentBid = Symbol.Bid;

                // --- Volatility Filter (ATR) ---
                if (_useAtrFilter)
                {
                    double currentAtrPips = (_atr.Result.LastValue) / Symbol.PipSize;
                    bool isHighVolatility = currentAtrPips > VolatilityThresholdPips;

                    if (isHighVolatility && !_isTradingPaused)
                    {
                        _isTradingPaused = true;
                        LogData("PauseTrading", Symbol.Bid, _lastExtremePrice, _currentTrend.ToString(), "", 0, 0, 0, "Volatile");
                    }
                    else if (!isHighVolatility && _isTradingPaused)
                    {
                        _isTradingPaused = false;
                        LogData("ResumeTrading", Symbol.Bid, _lastExtremePrice, _currentTrend.ToString(), "", 0, 0, 0, "Normal");
                    }
                }

                // --- Regime Detection using ADX ---
                double adxValue = _adx.ADX.LastValue;
                string marketRegime = adxValue > 25 ? "Trending" : "Ranging";

                if (_currentTrend == Trend.Down)
                {
                    if (currentBid < _lastExtremePrice)
                    {
                        _lastExtremePrice = currentBid;
                        LogData("New Low", currentBid, _lastExtremePrice, _currentTrend.ToString(), "", 0, 0, 0, marketRegime);
                    }

                    if (!_isTradingPaused && currentAsk >= _lastExtremePrice * (1 + _threshold))
                    {
                        bool classifierPass = true;

                        if (_useClassifierFilter)
                        {
                            // --- Classifier checks ---
                            bool isSmaConfirm = currentAsk > _sma.Result.LastValue;
                            bool isRsiConfirm = _rsi.Result.LastValue < RsiOverbought;
                            bool isMacdConfirm = _macd.MACD.LastValue > _macd.Signal.LastValue;
                            bool isStochConfirm = _stoch.PercentK.LastValue > _stoch.PercentD.LastValue;

                            classifierPass = isSmaConfirm && isRsiConfirm && isMacdConfirm && isStochConfirm;

                            Print($"Classifier Check → SMA:{isSmaConfirm}, RSI:{isRsiConfirm}, MACD:{isMacdConfirm}, STOCH:{isStochConfirm}");
                        }

                        // Reject trades in ranging markets
                        if (marketRegime == "Ranging")
                        {
                            classifierPass = false;
                            LogData("RegimeRejected", currentAsk, _lastExtremePrice, _currentTrend.ToString(), "Ranging", 0, 0, 0, marketRegime);
                        }

                        if (classifierPass)
                        {
                            double oldExtreme = _lastExtremePrice;
                            double overshoot = (currentAsk - oldExtreme) / oldExtreme;
                            _maxOvershootValue = Math.Max(_maxOvershootValue, overshoot);

                            try
                            {
                                ExecuteMarketOrder(TradeType.Buy, SymbolName, Volume, Label);
                                _currentTrend = Trend.Up;
                                _lastExtremePrice = currentAsk;
                                LogData("Long Entry", currentAsk, oldExtreme, "Up", "Accepted", overshoot, _maxOvershootValue, 0, marketRegime);
                            }
                            catch (Exception ex)
                            {
                                Print($"ExecuteMarketOrder failed: {ex.Message}");
                                LogData("EntryFailed", currentAsk, oldExtreme, "Up", "ExecError", overshoot, _maxOvershootValue, 0, marketRegime, ex.Message);
                            }
                        }
                        else
                        {
                            LogData("SignalRejected", currentAsk, _lastExtremePrice, _currentTrend.ToString(), "Rejected", 0, 0, 0, marketRegime);
                        }
                    }
                }
                else // _currentTrend == Trend.Up
                {
                    if (currentAsk > _lastExtremePrice)
                    {
                        _lastExtremePrice = currentAsk;
                        LogData("New High", currentAsk, _lastExtremePrice, _currentTrend.ToString(), "", 0, 0, 0, marketRegime);
                    }

                    double dynamicExitThreshold = _threshold * Y_Value * Math.Exp(-_maxOvershootValue);

                    if (currentBid <= _lastExtremePrice * (1 - dynamicExitThreshold))
                    {
                        try
                        {
                            ClosePositions(TradeType.Buy);
                            _currentTrend = Trend.Down;
                            double oldExtreme = _lastExtremePrice;
                            _lastExtremePrice = currentBid;
                            LogData("Long Exit", currentBid, oldExtreme, "Down", "", 0, _maxOvershootValue, dynamicExitThreshold, marketRegime);
                        }
                        catch (Exception ex)
                        {
                            Print($"ClosePositions failed: {ex.Message}");
                            LogData("ExitFailed", currentBid, _lastExtremePrice, "Down", "ExecError", 0, _maxOvershootValue, dynamicExitThreshold, marketRegime, ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Print($"OnTick top-level error: {ex.Message}");
            }
        }

        /// <summary>
        /// Flexible logger to avoid mismatched argument crashes.
        /// Accepts a primary set of parameters, and then any extras are appended to the CSV "Extra" column.
        /// </summary>
        private void LogData(string eventType, double price, double extremePrice, string trend, string classifierSignal,
            double overshoot, double maxOvershoot, double dynamicExitThreshold, string marketRegime, params object[] extra)
        {
            var timestamp = Server.Time.ToString("yyyy-MM-dd HH:mm:ss.fff");
            double currentAtr = 0;
            try
            {
                currentAtr = _atr.Result.LastValue / Symbol.PipSize;
            }
            catch { currentAtr = 0; }

            string extraStr = "";
            if (extra != null && extra.Length > 0)
            {
                extraStr = string.Join("|", Array.ConvertAll(extra, o => o == null ? "null" : o.ToString().Replace(",", ";")));
            }

            string line = $"{timestamp},{eventType},{price},{extremePrice},{trend},{currentAtr:F2},{_isTradingPaused},{classifierSignal},{overshoot:F5},{maxOvershoot:F5},{dynamicExitThreshold:F5},{StrategyMode},{marketRegime},{extraStr}";
            try
            {
                if (_csvWriter != null)
                {
                    _csvWriter.WriteLine(line);
                    _csvWriter.Flush();
                }
            }
            catch (Exception ex)
            {
                Print($"LogData write failed: {ex.Message}");
            }
        }

        protected override void OnStop()
        {
            try
            {
                if (_csvWriter != null)
                {
                    _csvWriter.Flush();
                    _csvWriter.Close();
                    _csvWriter.Dispose();
                    _csvWriter = null;
                }
            }
            catch (Exception ex)
            {
                Print($"OnStop error while closing writer: {ex.Message}");
            }
        }

        private void ClosePositions(TradeType tradeType)
        {
            try
            {
                foreach (var position in Positions.FindAll(Label, SymbolName, tradeType))
                {
                    ClosePosition(position);
                }
            }
            catch (Exception ex)
            {
                Print($"ClosePositions error: {ex.Message}");
            }
        }
    }
}
