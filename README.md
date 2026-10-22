# Peak-Load Capacity & Anomaly Detection Tool

**ENSE707 Software Quality Assurance, Auckland University of Technology (AUT)**
Group project (3 members), 2026

A C# toolchain that load-tests a simulated e-commerce storefront, stores the results build after build, detects performance regressions between builds, and presents them on a live dashboard and an exportable QA report.

## The problem

E-commerce platforms often behave well under everyday load and then fail at peak demand (the project was motivated by a Shopify outage during Cyber Monday trading). Fixed-threshold monitoring only reacts once a limit is crossed, so gradual build-over-build slowdowns go unnoticed until customers are affected.

This project treats that as a software quality problem (performance efficiency and reliability) and builds three cooperating pieces:

1. **A target system and load generator** that can reproduce steady-ramp, spike and soak load.
2. **Regression tracking** that persists every run and compares each new build against a rolling baseline.
3. **Reporting** through a Blazor dashboard and a Markdown QA report.

## How it fits together

```
 StoreSim (storefront)          P2.LoadGenerator                  P3.Core + P3.Dashboard
 WinForms + PostgreSQL   --->   ramps concurrent users,   --->    stores runs, flags regressions,
 (system under test)            records p50/p95/p99,              live dashboard, trend charts,
                                error rate, throughput            Markdown QA report
                                        |                                 ^
                                        +---- SampleData/runs.json -------+
                                              (shared TestRun JSON contract)
```

The load generator appends each run to `SampleData/runs.json`. The dashboard reads the same file, so a new run shows up without any manual copying.

## Repository layout

| Folder | What it is |
|---|---|
| `P2.LoadGenerator/` | Load-generation engine (console app). Load profiles (`SteadyRamp`, `Spike`, `Soak`), request factory built from a product catalogue, simulated and HTTP endpoint clients, metrics aggregation, console and JSON export. |
| `P2.LoadGenerator.Gui/` | WinForms front end for choosing a profile, duration and peak concurrency, then running and exporting a test. |
| `P2.LoadGenerator.Tests/` | MSTest suite for the load generator (18 tests). |
| `P3.Core/` | Class library with no UI: data models, JSON run store, threshold regression detector, health evaluator, Markdown report exporter. |
| `P3.Core.Tests/` | MSTest suite for P3.Core (18 tests). |
| `P3.Dashboard/` | Blazor Server dashboard: live view (`/`) and historical trends (`/trends`) with a hand-drawn SVG chart. |
| `StoreSim/` | Sample storefront (customers, products, carts) with a WinForms GUI and a PostgreSQL back end. This is the system under test. |
| `SampleData/` | `runs.json` (shared run history) and sample data for local use. |
| `Documentation/` | P3 design notes and build instructions. |
| `Journal/` | Project pitch, mid-project report draft, planning notes, P3 verification notes. |
| `P2.LoadGeneratorScreenshots/` | Screenshots of AI-assisted development prompts and outputs, kept as evidence for the course's Copilot requirement. |
| `.github/workflows/` | CI pipelines (see below). |

> **Naming note:** folder names (`P2.*`, `P3.*`) follow the module labels used while the code was written. The mid-project report in `Journal/` numbers the modules differently (load generation as P1, anomaly detection as P2, regression tracking as P3). Where the two differ, this README describes components by what they do.

## Regression detection

`ThresholdRegressionDetector` takes the newest run and, for each endpoint, averages the p95 latency of the previous N builds to form a baseline (default N = 5). It classifies the build as:

| Result | Condition |
|---|---|
| None | within 10% of baseline |
| Minor | 10% or more slower than baseline |
| Major | 25% or more slower than baseline (appears in the report's breach log) |

`HealthStatusEvaluator` then rolls this up into **Healthy**, **Warning** or **Critical** for the latest build. All thresholds are configurable through `RegressionThresholds`.

## Getting started

**Prerequisites**

- .NET 8 SDK for the load generator, `P3.Core` and the dashboard
- .NET 10 SDK for `P3.Core.Tests` and `StoreSim`
- Windows for the two WinForms apps (`P2.LoadGenerator.Gui`, `StoreSim`)
- PostgreSQL, only if you want to run `StoreSim`

**Build and test**

```bash
dotnet build P3RegressionTracking.slnx
dotnet test P3.Core.Tests/P3.Core.Tests.csproj
dotnet test P2.LoadGenerator.Tests/P2.LoadGenerator.Tests.csproj
```

**Run a load test**

```bash
dotnet run --project P2.LoadGenerator
```

By default this runs a 20-second spike profile against a simulated endpoint, prints a summary, and appends the run to `SampleData/runs.json`. Set the `BUILD_ID` environment variable to label the run with a build identifier. To test a real storefront instead, swap `SimulatedCustomerEndpointClient` for `HttpCustomerEndpointClient` in `P2.LoadGenerator/Program.cs`.

**Open the dashboard**

```bash
dotnet run --project P3.Dashboard
```

Open the URL that `dotnet run` prints. `/` is the live dashboard, `/trends` is the historical view, and the QA report can be exported as Markdown from the dashboard. `P3.Core/VerificationOutput/qa-report-sample.md` shows an example report.

**Run the GUI load generator (Windows)**

```bash
dotnet run --project P2.LoadGenerator.Gui
```

**Run StoreSim (Windows, optional)**

1. Install PostgreSQL and create the database and tables using the SQL in `dbSetupReadme.txt`.
2. Set your own database password in the connection string in `StoreSim/StoreSim/Store.cs` (it ships with a placeholder). Do not commit a real password.
3. `dotnet run --project StoreSim/StoreSimGUI`

## Continuous integration

Two GitHub Actions workflows run on Windows runners:

- **`ci.yml`**: builds the solution, runs both MSTest suites, then runs a smoke test that checks the load generator writes a run the dashboard's store can read (`BuildId` and `EndpointMetrics` present).
- **`p2-load-generator.yml`**: builds the load generator and its GUI, runs its tests, enforces at least 70% line coverage on the engine, and smoke-tests both the console app and the GUI against the simulated endpoint.

## Status and limitations

- The **ML.NET anomaly detection** module described in the project proposal is not implemented in this repository. `TestRun.AnomalyDetected` is a nullable field reserved for it, and the dashboard and report handle both the "flagged" and "nothing flagged" cases.
- The default load test targets a **simulated endpoint**; results reflect the simulation's configured latency, jitter and error rate, not a production system.
- The dashboard stores runs in a JSON file. The `ITestRunStore` interface allows a database-backed store to be swapped in without changing the pages.
- The WinForms projects only run on Windows.

## Team

| Member | Main areas (from commit history) |
|---|---|
| **Suemon Kwok** | Regression tracking and reporting: `P3.Core`, `P3.Dashboard`, `P3.Core.Tests`, sample data, project pitch and planning documents |
| **Avathanshu Bhat** | Load generator and its GUI, `P2.LoadGenerator.Tests`, load-generator CI workflow, AI-assisted development evidence |
| **Quentin Masoe** | `StoreSim` storefront and PostgreSQL setup |

Commit counts at the time of writing: Suemon Kwok 40, Avathanshu Bhat 28, Quentin Masoe 10.

## AI-assisted development

GitHub Copilot was used for boilerplate, test scaffolding and documentation, in line with the course's terms. Generated code was reviewed and corrected by hand; examples are documented in the mid-project report in `Journal/`.

## Licence

No licence has been specified. This is coursework; please contact the authors before reusing the code.
