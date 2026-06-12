# REVIVER: Consequence-Driven Attack Recovery for Manufacturing Systems

This repository contains the implementation and evaluation artifact for **REVIVER**, a consequence-driven attack recovery framework for manufacturing systems.

REVIVER systematically explores attack consequences through control-logic fault injection, generates recovery procedures that restore disrupted manufacturing tasks, and deploys reusable recovery modules using formally mined applicability boundaries.

## Repository Structure

```text
.
├── Logs/                              # Execution traces and experiment logs
├── Manipulations/                     # Fault-injection specifications
├── Recovery/                          # Recovery controller implementations
├── Scenes/                            # Factory I/O scenes
├── spec/                              # Mined applicability boundaries
├── Controller.cs                      # Main controller implementation
├── Program_Benign.cs                  # Nominal manufacturing execution
├── Program_FaultInjection.cs          # Attack-consequence exploration
├── Program_Recovery.cs                # Recovery deployment
├── state_identification.cs            # Execution-state identification
└── recovery_module_identification.cs  # Recovery module matching
```

### Attack Consequence Exploration

`Program_FaultInjection.cs` injects manipulations defined in `Manipulations/` and records the resulting attack-consequence traces in `Logs/`.

### Recovery Procedure Generation

`Program_Recovery.cs` implemnts online recovery which executes

1. `state_identification.cs` determining whether the observed system state already satisfies a nominal invariant.
2. `recovery_module_identification.cs` evaluating applicability boundaries and selects the best matching recovery module.
