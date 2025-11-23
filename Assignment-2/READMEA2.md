AI-Enhanced Triangular Arbitrage Strategy
An advanced triangular arbitrage trading system that combines traditional currency arbitrage detection with artificial intelligence to optimize trade execution and profitability. The system operates across 10 major currencies and includes comprehensive latency monitoring and performance tracking.

🚀 Overview
This project implements a sophisticated triangular arbitrage strategy that identifies and executes profitable three-currency cycles (e.g., USD→EUR→JPY→USD). The system leverages machine learning to filter trade opportunities and includes real-time latency monitoring to ensure optimal execution speed.

Key Features
Multi-Currency Support: Operates across 10 major currencies (USD, EUR, JPY, GBP, CNY, AUD, CAD, CHF, HKD, SGD)

AI-Powered Filtering: Random Forest classifier to identify profitable arbitrage opportunities

Real-Time Latency Monitoring: Sub-millisecond execution tracking and optimization

Comprehensive Backtesting: Full simulation environment with commission modeling

Performance Analytics: Detailed logging and equity curve generation

📊 Performance
The strategy has demonstrated consistent profitability with an upward-trending equity curve showing steady capital growth over approximately 900,000 executed trades.

![Equity Curve](equity_curve.jpgCore Components

AI Filter (aifilter.py): Python-based machine learning pipeline

Feature engineering for arbitrage opportunities

Random Forest classification for trade filtering

Real-time prediction and execution

Triangular Arbitrage Engine (TriArbCycleLogger.cs): C# trading bot for cTrader

Real-time quote matrix management

Three-leg arbitrage cycle detection and execution

Commission modeling and P&L calculation

Latency Monitor (TriArbLatencyMonitor.cs): Performance optimization system

Sub-millisecond latency tracking

Real-time performance alerts

Execution quality monitoring

🛠️ Installation & Setup
Prerequisites
Install required Python packages:

bash
pip install -r requirements.txt
Required Dependencies
numpy==1.26.4: Numerical operations and array processing

numba==0.59.1: High-performance numerical computation acceleration

pandas==2.2.2: Data manipulation and analysis

scikit-learn==1.4.2: Machine learning algorithms and model training

lightgbm==4.3.0: Gradient boosting framework for advanced ML models

matplotlib==3.8.4: Plotting and visualization for performance analysis

Usage
Running the AI Filter
python
python aifilter.py
The AI filter will:

Collect synthetic tick data for training

Train the Random Forest classifier

Execute live filtering with profitability predictions

Deploying Trading Bots
Load TriArbCycleLogger.cs in cTrader for full backtesting

Load TriArbLatencyMonitor.cs for real-time latency monitoring

Configure parameters:

Start capital: $1,000,000

Commission: 30 units per million traded

Minimum profit factor: 0.0001 (1 basis point)

🎯 Strategy Logic
Triangular Arbitrage Detection
The system continuously scans for profitable cycles across currency pairs:

Quote Matrix Update: Real-time bid/ask prices for all currency combinations

Cycle Detection: Identify three-currency paths (A→B→C→A)

Profitability Check: Calculate theoretical profit after spreads and commissions

AI Filtering: Use trained model to predict execution success

Trade Execution: Execute three-leg sequence with latency monitoring

Machine Learning Enhancement
The AI filter uses features including:

Absolute profit potential

Market direction indicators

Volatility measures

Historical success patterns

 Logging & Monitoring
Trade Logging
CSV Export: Detailed trade logs with timestamps and P&L

Real-time Console: Live trade execution updates

Performance Metrics: Win rate, total commission, pair frequency

Latency Monitoring
Execution Speed: Per-tick latency measurement

Performance Alerts: Warnings for high-latency situations (>5ms)

Statistical Analysis: Min/Max/Average latency tracking
⚡ Performance Optimization
Speed Enhancements
Numba Acceleration: JIT compilation for numerical operations

Efficient Data Structures: Optimized matrix operations for quote handling

Throttling Controls: Maximum trades per tick to prevent overloading

Risk Management
Commission Modeling: Realistic transaction cost calculations

Position Sizing: Fixed notional amounts for consistent risk exposure

Time-Based Filters: Trading hour restrictions to avoid low-liquidity periods

 Results & Analytics
The system generates comprehensive performance analytics:

Equity Curve: Visual representation of cumulative returns

Trade Statistics: Success rates, profit factors, and frequency analysis

Latency Analysis: Execution speed optimization metrics

Currency Pair Performance: Individual pair profitability tracking

Future Enhancements
Real-time Data Integration: Connect to live market data feeds

Advanced ML Models: Implement deep learning for pattern recognition

Multi-Asset Support: Extend beyond currency pairs to crypto and commodities

Risk-Adjusted Position Sizing: Dynamic position sizing based on volatility