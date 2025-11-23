import pandas as pd
import numpy as np
import math
from enum import Enum

# --- 1. Manual Indicator Implementations (No external libraries needed) ---


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
    # Using simple moving average for smoothing
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

    # Handle potential division by zero
    dx_denominator = plus_di + minus_di
    dx = 100 * (
        np.abs(plus_di - minus_di) / dx_denominator.where(dx_denominator != 0, 1)
    )
    return dx.ewm(alpha=1 / period, adjust=False).mean()


# --- 2. Main Bot and Strategy Classes ---


class StrategyMode(Enum):
    FULL_AI = "Full AI"
    NO_CLASSIFIER = "No Classifier"
    NO_VOLATILITY_FILTER = "No Volatility Filter"
    BASELINE = "Baseline"


class AdvancedDCBot:
    def __init__(self, data, params):
        self.df = data
        self.params = params
        self.trades = []
        self.total_pnl = 0.0
        self.use_volatility_filter = params["mode"] in [
            StrategyMode.FULL_AI,
            StrategyMode.NO_CLASSIFIER,
        ]
        self.use_classifier = params["mode"] in [
            StrategyMode.FULL_AI,
            StrategyMode.NO_VOLATILITY_FILTER,
        ]
        print(f"Initializing bot in mode: {params['mode'].value}")
        print(f"Volatility Filter Enabled: {self.use_volatility_filter}")
        print(f"Entry Classifier Enabled: {self.use_classifier}")

    def run_backtest(self):
        if self.df.empty:
            print(
                "\nCRITICAL ERROR: Backtest cannot start because the DataFrame is empty."
            )
            return 0.0, []

        current_trend = -1
        last_extreme_price = self.df["low"].iloc[0]
        max_overshoot = 0.0001
        is_position_open = False
        entry_price = 0.0
        is_trading_paused = False
        trade = {}

        for i, row in self.df.iterrows():
            date, high, low = row["date"], row["high"], row["low"]

            if pd.isna(row["sma"]):
                continue

            if self.use_volatility_filter:
                is_high_volatility = row["atr"] > self.params["volatility_threshold"]
                if is_high_volatility and not is_trading_paused:
                    is_trading_paused = True
                elif not is_high_volatility and is_trading_paused:
                    is_trading_paused = False

            if current_trend == -1:
                if low < last_extreme_price:
                    last_extreme_price = low
                if high >= last_extreme_price * (1 + self.params["dc_threshold"]):
                    if is_trading_paused:
                        continue
                    classifier_pass = True
                    if self.use_classifier:
                        is_sma_confirm = high > row["sma"]
                        is_rsi_confirm = row["rsi"] < self.params["rsi_overbought"]
                        is_macd_confirm = row["macd"] > row["macdsignal"]
                        is_stoch_confirm = row["stoch_k"] > row["stoch_d"]
                        is_trending_market = row["adx"] > 25
                        classifier_pass = all(
                            [
                                is_sma_confirm,
                                is_rsi_confirm,
                                is_macd_confirm,
                                is_stoch_confirm,
                                is_trending_market,
                            ]
                        )

                    if classifier_pass:
                        entry_price = high
                        is_position_open = True
                        current_trend = 1
                        old_extreme = last_extreme_price
                        last_extreme_price = high
                        max_overshoot = max(
                            max_overshoot,
                            (
                                (high - old_extreme) / old_extreme
                                if old_extreme != 0
                                else 0
                            ),
                        )
                        trade = {
                            "entry_date": date,
                            "entry_price": entry_price,
                            "pnl": 0,
                        }
                        print(
                            f"{date.strftime('%Y-%m-%d')}: Opened LONG position at {entry_price:.4f}"
                        )

            elif current_trend == 1:
                if high > last_extreme_price:
                    last_extreme_price = high
                if is_position_open:
                    overshoot = (
                        (high - entry_price) / entry_price if entry_price != 0 else 0
                    )
                    max_overshoot = max(max_overshoot, overshoot)
                    dynamic_exit_threshold = (
                        self.params["dc_threshold"]
                        * self.params["y_value"]
                        * math.exp(-max_overshoot)
                    )

                    if low <= last_extreme_price * (1 - dynamic_exit_threshold):
                        exit_price = low
                        pnl = (exit_price - entry_price) * self.params["volume"]
                        self.total_pnl += pnl
                        trade.update(
                            {"exit_date": date, "exit_price": exit_price, "pnl": pnl}
                        )
                        self.trades.append(trade)
                        is_position_open = False
                        current_trend = -1
                        last_extreme_price = low
                        print(
                            f"{date.strftime('%Y-%m-%d')}: Closed LONG position at {exit_price:.4f} | PnL: ${pnl:.2f}"
                        )

        if is_position_open:
            final_price = self.df["close"].iloc[-1]
            pnl = (final_price - entry_price) * self.params["volume"]
            self.total_pnl += pnl
            trade.update(
                {
                    "exit_date": self.df["date"].iloc[-1],
                    "exit_price": final_price,
                    "pnl": pnl,
                }
            )
            self.trades.append(trade)
            print(f"Force closing open position at end of backtest. PnL: ${pnl:.2f}")

        return self.total_pnl, self.trades


# --- Main Execution Block ---
try:
    # 3. Load and Prepare the 'EURUSD Data.csv' file
    df = pd.read_csv("EUR_USD.csv")
    initial_rows = len(df)

    df.columns = [col.lower() for col in df.columns]

    df["date"] = pd.to_datetime(df["date"], format="%m/%d/%Y")
    df.sort_values(by="date", inplace=True)
    df.reset_index(drop=True, inplace=True)

    cleaned_rows = len(df)
    print(
        f"Data Loading: Started with {initial_rows} rows, {cleaned_rows} rows remaining after cleaning."
    )

    # 4. Define Strategy Parameters
    strategy_params = {
        "mode": StrategyMode.FULL_AI,
        "dc_threshold": 0.005,
        "volume": 10000,
        "y_value": 0.5,
        "atr_period": 14,
        "volatility_threshold_pips": 20,
        "sma_period": 50,
        "rsi_period": 14,
        "rsi_overbought": 70,
    }

    required_data_length = strategy_params["sma_period"]
    if len(df) < required_data_length:
        raise ValueError(
            f"Data is too short. Need at least {required_data_length} valid rows, but found only {len(df)}."
        )

    print("Calculating technical indicators...")
    pip_size = 0.0001
    df["atr"] = calculate_atr(
        df["high"], df["low"], df["close"], period=strategy_params["atr_period"]
    )
    strategy_params["volatility_threshold"] = (
        strategy_params["volatility_threshold_pips"] * pip_size
    )
    df["sma"] = df["close"].rolling(window=strategy_params["sma_period"]).mean()
    df["rsi"] = calculate_rsi(df["close"], period=strategy_params["rsi_period"])
    df["macd"], df["macdsignal"] = calculate_macd(df["close"])
    df["stoch_k"], df["stoch_d"] = calculate_stoch(df["high"], df["low"], df["close"])
    df["adx"] = calculate_adx(df["high"], df["low"], df["close"])

    df.dropna(inplace=True)
    df.reset_index(drop=True, inplace=True)

    print(
        f"Data ready for backtest: {len(df)} rows remaining after indicator calculation."
    )

    # 5. Run the Backtest
    bot = AdvancedDCBot(df, strategy_params)
    total_pnl, trades = bot.run_backtest()

    if not df.empty and trades is not None:
        print("\n--- Backtest Summary ---")
        print(f"Strategy Mode: {strategy_params['mode'].value}")
        print(
            f"Time Period: {df['date'].iloc[0].strftime('%Y-%m-%d')} to {df['date'].iloc[-1].strftime('%Y-%m-%d')}"
        )
        print(f"Total Trades: {len(trades)}")
        print(f"Total PnL: ${total_pnl:.2f}")
        print("------------------------")

except FileNotFoundError:
    print(
        "Error: 'EURUSD Data.csv' not found. Please ensure the file is in the same directory as the script."
    )
except ValueError as ve:
    print(f"\nBACKTEST STOPPED: {ve}")
except Exception as e:
    print(f"An unexpected error occurred: {e}")
