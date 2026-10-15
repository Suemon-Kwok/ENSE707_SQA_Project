# QA Report — Regression Tracking
Generated: 2026-09-26 03:51:22Z

## Breach Log (Build-over-Build Regressions)
| Endpoint | Build | Baseline Build(s) | Current p95 (ms) | Baseline p95 (ms) | % Change |
|---|---|---|---|---|---|
| POST /checkout | build-104 | build-103,build-102,build-101 | 410.0 | 210.0 | 95.2% |

## Anomaly Log (P2 ML.NET Flags)
| Build | Timestamp (UTC) | Load Profile |
|---|---|---|
| build-104 | 2026-07-08 09:00:00Z | SteadyRamp |

## Historical Trend (p95 latency by endpoint)
| Endpoint | Build | Timestamp (UTC) | p95 (ms) |
|---|---|---|---|
| GET /cart | build-101 | 2026-07-01 09:00:00Z | 95.0 |
| GET /cart | build-102 | 2026-07-03 09:00:00Z | 92.0 |
| GET /cart | build-103 | 2026-07-05 09:00:00Z | 94.0 |
| GET /cart | build-104 | 2026-07-08 09:00:00Z | 96.0 |
| POST /checkout | build-101 | 2026-07-01 09:00:00Z | 210.0 |
| POST /checkout | build-102 | 2026-07-03 09:00:00Z | 205.0 |
| POST /checkout | build-103 | 2026-07-05 09:00:00Z | 215.0 |
| POST /checkout | build-104 | 2026-07-08 09:00:00Z | 410.0 |

