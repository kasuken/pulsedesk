---
description: "Use when naming the product, writing user-facing copy, proposing repo or solution names, restructuring the app into projects, or describing PulseDesk in docs, prompts, issues, PRs, and store listing text. Covers branding, positioning, tagline, and preferred project layout."
name: "PulseDesk Branding"
---
# PulseDesk Branding

- Use `PulseDesk` as the product name in code, docs, UX copy, issues, PRs, and planning notes.
- Treat `pulsedesk` as the preferred repository name and `PulseDesk` as the preferred solution name.
- Default tagline: `Your Windows machine, explained.`
- Keep the core positioning consistent: PulseDesk is a native, local-first, lightweight Windows system health dashboard that shows what the machine is doing and why it feels slow.
- Frame the first release around real-time visibility into CPU, RAM, GPU, disk, network, temperatures, and system bottlenecks.
- Prefer language that is professional and approachable for developers, IT admins, and power users. Avoid overly gamer-centric, vendor-specific, or unnecessarily nerdy branding.
- When drafting alternative short copy, prefer variants aligned with these messages: `Know what your PC is doing.`, `Real-time system health for Windows.`, `A cleaner way to understand your PC performance.`
- When proposing a broader repo layout, prefer this structure unless the user asks otherwise:

```text
PulseDesk.sln
src/
  PulseDesk.App/
  PulseDesk.Telemetry/
  PulseDesk.Analysis/
  PulseDesk.Storage/
  PulseDesk.Tray/
tests/
  PulseDesk.Telemetry.Tests/
  PulseDesk.Analysis.Tests/
docs/
  architecture.md
  product-spec.md
```