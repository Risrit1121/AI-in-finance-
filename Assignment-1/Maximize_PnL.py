import pandas as pd
import numpy as np
import math
from enum import Enum
from datetime import datetime

# --- 1. Load and clean data ---
file_path = "EUR_USD.csv"  # Make sure this path is correct
df = pd.read_csv(file_path, dayfirst=True)  # Parse dates correctly

# Standardize column names
df.columns = [col.strip().lower() for col in df.columns]

# Map 'price' to 'close' if present
if "price" in df.columns:
    df.rename(columns={"price": "close"}, inplace=True)

# Ensure numeric columns
numeric_cols = ["close", "high", "low", "open"]
for col in numeric_cols:
    if col not in df.columns:
        raise ValueError(f"Missing required column: {col}")
    df[col] = pd.to_numeric(df[col], errors="coerce")

# Convert date column to datetime
df["date"] = pd.to_datetime(df["date"], dayfirst=True)

# Drop rows with missing price data
df.dropna(subset=numeric_cols, inplace=True)

# Sort by date
df.sort_values("date", inplace=True)
df.reset_index(drop=True, inplace=True)

print(f"Data loaded: {len(df)} rows")
print(df.head())


# --- 2. Indicator Functions ---
def calculate_atr(high, low, close, period=14):
    high_low = high - low
    high_close = np.abs(high - close.shift())
    low_close = np.abs(low - close.shift())
    tr = pd.concat([high_low, high_close, low_close], axis=1).max(axis=1)
    return tr.ewm(alpha=1 / period, adjust=False).mean()


def calculate_rsi(close, period=14):
    delta = close.diff()
    gain = (delta.where(delta > 0, 0)).ewm(alpha=1 / period, adjust=False).mean()
    loss = (-delta.where(delta < 0, 0)).ewm(alpha=1 / period, adjust=False).mean()
    rs = gain / loss
    return 100 - (100 / (1 + rs))


def calculate_macd(close, fast=12, slow=26, signal=9):
    ema_fast = close.ewm(span=fast, adjust=False).mean()
    ema_slow = close.ewm(span=slow, adjust=False).mean()
    macd_line = ema_fast - ema_slow
    signal_line = macd_line.ewm(span=signal, adjust=False).mean()
    return macd_line, signal_line


def calculate_stoch(high, low, close, k=9, d=3, smooth_k=9):
    lowest_low = low.rolling(window=k).min()
    highest_high = high.rolling(window=k).max()
    percent_k = 100 * ((close - lowest_low) / (highest_high - lowest_low))
    percent_k = percent_k.rolling(window=smooth_k).mean()
    percent_d = percent_k.rolling(window=d).mean()
    return percent_k, percent_d


def calculate_adx(high, low, close, period=14):
    tr_series = (
        (high - low)
        .to_frame("tr1")
        .join((high - close.shift()).abs().to_frame("tr2"))
        .join((low - close.shift()).abs().to_frame("tr3"))
        .max(axis=1)
    )
    atr = tr_series.ewm(alpha=1 / period, adjust=False).mean()
    delta_up = high.diff()
    delta_down = -low.diff()
    plus_dm = np.where((delta_up > delta_down) & (delta_up > 0), delta_up, 0)
    minus_dm = np.where((delta_down > delta_up) & (delta_down > 0), delta_down, 0)
    plus_di = 100 * (
        pd.Series(plus_dm).ewm(alpha=1 / period, adjust=False).mean() / atr
    )
    minus_di = 100 * (
        pd.Series(minus_dm).ewm(alpha=1 / period, adjust=False).mean() / atr
    )
    dx_denominator = plus_di + minus_di
    dx = 100 * (
        np.abs(plus_di - minus_di) / dx_denominator.where(dx_denominator != 0, 1)
    )
    return dx.ewm(alpha=1 / period, adjust=False).mean()


# --- 3. Strategy Mode Enum ---
class StrategyMode(Enum):
    FULL_AI = "Full AI"
    NO_CLASSIFIER = "No Classifier"
    NO_VOLATILITY_FILTER = "No Volatility Filter"
    BASELINE = "Baseline"


# --- 4. MaximizePnLBot Class ---
class MaximizePnLBot:
    def __init__(self, df, params):
        if df.empty:
            raise ValueError("DataFrame is empty. Cannot run backtest.")
        self.df = df
        self.params = params
        self.total_pnl = 0.0
        self.trades = []

        self.use_volatility_filter = params["mode"] in [
            StrategyMode.FULL_AI,
            StrategyMode.NO_CLASSIFIER,
        ]
        self.use_classifier = params["mode"] in [
            StrategyMode.FULL_AI,
            StrategyMode.NO_VOLATILITY_FILTER,
        ]

        self.current_trend = -1  # -1 = down, 1 = up
        self.last_extreme = df["low"].iloc[0]
        self.max_overshoot = 0.0001
        self.is_position_open = False
        self.entry_price = 0.0
        self.is_trading_paused = False

    def run_backtest(self):
        for i, row in self.df.iterrows():
            high, low, close = row["high"], row["low"], row["close"]

            # Volatility filter
            if self.use_volatility_filter:
                if row["atr"] > self.params["volatility_threshold"]:
                    self.is_trading_paused = True
                    continue
                else:
                    self.is_trading_paused = False

            if self.current_trend == -1:
                if low < self.last_extreme:
                    self.last_extreme = low
                if high >= self.last_extreme * (1 + self.params["dc_threshold"]):
                    if self.is_trading_paused:
                        print(
                            f"Skipped index {i}: ATR {row['atr']:.5f} above threshold"
                        )
                        continue
                    classifier_pass = True
                    classifier_pass = (
                        (high > row["sma"] if not np.isnan(row["sma"]) else True)
                        and row["rsi"] < self.params["rsi_overbought"]
                        and row["adx"] > 10
                    )
                    if self.use_classifier:
                        classifier_pass = (
                            high > row["sma"]
                            and row["rsi"] < self.params["rsi_overbought"]
                            and row["macd"] > row["macdsignal"]
                            and row["stoch_k"] > row["stoch_d"]
                            and row["adx"] > 10
                        )
                    if classifier_pass:
                        print(f"Trade triggered at index {i}, price={high}")
                        self.entry_price = high
                        self.is_position_open = True
                        old_extreme = self.last_extreme
                        self.last_extreme = high
                        self.max_overshoot = max(
                            self.max_overshoot, (high - old_extreme) / old_extreme
                        )
                        self.current_trend = 1
                        self.trades.append(
                            {"entry_price": self.entry_price, "entry_idx": i}
                        )
            else:  # current_trend == 1
                if high > self.last_extreme:
                    self.last_extreme = high
                if self.is_position_open:
                    overshoot = (high - self.entry_price) / self.entry_price
                    self.max_overshoot = max(self.max_overshoot, overshoot)
                    dynamic_exit_threshold = (
                        self.params["dc_threshold"]
                        * self.params["y_value"]
                        * math.exp(-self.max_overshoot)
                    )
                    if low <= self.last_extreme * (1 - dynamic_exit_threshold):
                        exit_price = low
                        pnl = (exit_price - self.entry_price) * self.params["volume"]
                        self.total_pnl += pnl
                        self.trades[-1].update(
                            {"exit_price": exit_price, "pnl": pnl, "exit_idx": i}
                        )
                        self.is_position_open = False
                        self.current_trend = -1
                        self.last_extreme = low

        # Force-close last position
        if self.is_position_open:
            final_price = self.df["close"].iloc[-1]
            pnl = (final_price - self.entry_price) * self.params["volume"]
            self.total_pnl += pnl
            self.trades[-1].update(
                {"exit_price": final_price, "pnl": pnl, "exit_idx": len(self.df) - 1}
            )

        return self.total_pnl, self.trades


# --- 5. Parameters ---
params = {
    "mode": StrategyMode.FULL_AI,
    "dc_threshold": 0.001,
    "volume": 10000,
    "y_value": 0.5,
    "atr_period": 14,
    "volatility_threshold_pips": 50,
    "sma_period": 50,
    "rsi_period": 14,
    "rsi_overbought": 70,
}
pip_size = 0.0001
params["volatility_threshold"] = params["volatility_threshold_pips"] * pip_size

# --- 6. Compute Indicators ---
df["atr"] = calculate_atr(
    df["high"], df["low"], df["close"], period=params["atr_period"]
)
df["sma"] = df["close"].rolling(window=params["sma_period"]).mean()
df["rsi"] = calculate_rsi(df["close"], period=params["rsi_period"])
df["macd"], df["macdsignal"] = calculate_macd(df["close"])
df["stoch_k"], df["stoch_d"] = calculate_stoch(df["high"], df["low"], df["close"])
df["adx"] = calculate_adx(df["high"], df["low"], df["close"])

# Fill initial NaNs instead of dropping all rows
df.fillna(method="bfill", inplace=True)
df.reset_index(drop=True, inplace=True)

# --- 7. Run Backtest ---
bot = MaximizePnLBot(df, params)
total_pnl, trades = bot.run_backtest()
print(f"Total PnL: ${total_pnl:.2f}, Total Trades: {len(trades)}")
