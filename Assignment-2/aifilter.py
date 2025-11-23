"""
Triangular-Arbitrage + AI Filter  (Assignment-2 Demo)
Author : <Your Name>
Date   : <date>
Desc   : Offline-simulatable pipeline showing how to:
         1) Build streaming FX graph
         2) Detect 3-currency cycles
         3) Classify profitable vs. non-profitable
         4) Profile runtime
Note   : Replace synthetic_tick_stream() with real L1 quotes for live/paper trading.
"""

# -------------------- IMPORTS --------------------
import time
import itertools
import numpy as np
import pandas as pd
from collections import defaultdict
from sklearn.ensemble import RandomForestClassifier
from sklearn.model_selection import train_test_split
from sklearn.metrics import classification_report

# --------------------------------------------------
# 1. SAMPLE TICK STREAM  (offline demo)
# --------------------------------------------------
CURRENCIES = ["USD", "EUR", "JPY", "GBP", "AUD", "CAD", "CHF", "NZD", "CNY", "SEK"]


def synthetic_tick_stream(n_ticks=5000, seed=42):
    """
    Yield pseudo-random L1 quotes: (timestamp, base, quote, bid, ask)
    For demo & unit-test only.
    """
    rng = np.random.default_rng(seed)
    for i in range(n_ticks):
        t = pd.Timestamp.utcnow() + pd.Timedelta(milliseconds=i * 200)
        base, quote = rng.choice(CURRENCIES, size=2, replace=False)
        mid = 0.5 + rng.random()  # random mid
        spread = 0.0005 + rng.random() * 0.0005
        bid = mid - spread / 2
        ask = mid + spread / 2
        yield (t, base, quote, bid, ask)


# --------------------------------------------------
# 2. FX GRAPH  +   CROSS-RATE CALC
# --------------------------------------------------
class FXGraph:
    """Maintain directed edges base->quote with best bid/ask."""

    def __init__(self):
        self.bid = defaultdict(lambda: defaultdict(float))
        self.ask = defaultdict(lambda: defaultdict(float))

    def update(self, base, quote, bid, ask):
        self.bid[base][quote] = bid
        self.ask[base][quote] = ask

    def get_cross_rate(self, a, b):
        """Return mid-price if direct edge exists else np.nan."""
        if b in self.bid[a]:
            return 0.5 * (self.bid[a][b] + self.ask[a][b])
        return np.nan


# --------------------------------------------------
# 3. TRIANGULAR ARB DETECTION
# --------------------------------------------------
def find_cycles(graph, threshold=1e-5):
    """
    Find simple 3-currency cycles a->b->c->a
    Return list of (cycle, theoretical_pnl)
    """
    cycles = []
    for a, b, c in itertools.permutations(CURRENCIES, 3):
        if a < b < c:  # avoid duplicate permutations
            r1 = graph.get_cross_rate(a, b)
            r2 = graph.get_cross_rate(b, c)
            r3 = graph.get_cross_rate(c, a)
            if not (np.isnan(r1) or np.isnan(r2) or np.isnan(r3)):
                pnl = (r1 * r2 * r3) - 1.0
                if abs(pnl) > threshold:
                    cycles.append(((a, b, c), pnl))
    return cycles


# --------------------------------------------------
# 4. FEATURE ENGINEERING for AI FILTER
# --------------------------------------------------
def cycle_to_features(cycle, pnl):
    """
    Simple example features:
      - abs(pnl)
      - sign of pnl
      - dummy vol term
    """
    return [abs(pnl), np.sign(pnl), len(set(cycle))]


# --------------------------------------------------
# 5. TRAIN CLASSIFIER  (offline training demo)
# --------------------------------------------------
def train_ai_filter(X, y):
    X_train, X_val, y_train, y_val = train_test_split(
        X, y, test_size=0.2, random_state=42, stratify=y
    )
    clf = RandomForestClassifier(
        n_estimators=100, random_state=42, class_weight="balanced"
    )
    clf.fit(X_train, y_train)
    print("\nValidation report:\n", classification_report(y_val, clf.predict(X_val)))
    return clf


# --------------------------------------------------
# 6. MAIN LOOP  (stream + detect + filter)
# --------------------------------------------------
def main():
    graph = FXGraph()
    X_feat, y_label = [], []

    print("Collecting synthetic cycles for training …")
    for tick in synthetic_tick_stream(3000):
        _, base, quote, bid, ask = tick
        graph.update(base, quote, bid, ask)
        for cyc, pnl in find_cycles(graph):
            X_feat.append(cycle_to_features(cyc, pnl))
            y_label.append(1 if pnl > 0 else 0)

    X = np.array(X_feat)
    y = np.array(y_label)
    print(f"Collected {len(y)} candidate cycles")

    clf = train_ai_filter(X, y)

    # --- Live/Replay loop with profiling ---
    start = time.perf_counter()
    detections, executed = 0, 0
    for tick in synthetic_tick_stream(1000, seed=99):
        _, base, quote, bid, ask = tick
        graph.update(base, quote, bid, ask)
        for cyc, pnl in find_cycles(graph):
            detections += 1
            feat = np.array(cycle_to_features(cyc, pnl)).reshape(1, -1)
            if clf.predict(feat)[0] == 1:
                executed += 1
    elapsed = time.perf_counter() - start
    print(f"\nScanned {detections} cycles, executed {executed}")
    print(f"Elapsed time for live loop: {elapsed:.3f} sec")


if __name__ == "__main__":
    main()
