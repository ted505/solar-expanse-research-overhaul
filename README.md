# solar-expanse-research-overhaul
Solar Expanse Research Overhaul

## Research cost scaling reports

Use the balancing script to prototype prerequisite-depth-based research cost scaling from the Teddit dump:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\research-cost-scaling.ps1 -WriteOverrideYaml
```

The script writes local scratch files under `research-scaling-output/`:

- `research-cost-scaling-report.csv`: all research entries with vanilla cost, prerequisite depth, multiplier, and scaled cost.
- `<targetResearchId>-queue.csv`: prerequisite closure plus target for the configured target, defaulting to `research_sc_nike`.
- `research-cost-overrides.yaml`: optional Teddit-style `workHourToComplete` override output when `-WriteOverrideYaml` is used.

Formula:

```text
scaled = vanilla * min((1 + depthRpMonths / pivotMonths) ^ exponent, maxMultiplier)
```

`depthRpMonths` is the vanilla RP-month total of the research entry's full prerequisite closure.
