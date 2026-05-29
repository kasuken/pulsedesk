# PulseDesk Repository Instructions

- Build on the current stack: `PulseDesk` is a native Windows app using WinUI 3, .NET 10, Windows App SDK, and nullable reference types.
- Keep the product local-first and lightweight. Do not introduce cloud requirements, browser-style architecture, or heavyweight dependencies unless the task explicitly requires them.
- Use `PulseDesk` consistently for product naming in code, UI text, docs, and planning. Follow the branding guidance in `.github/instructions/pulsedesk-branding.instructions.md` for positioning and taglines.
- Treat `PulseDesk/PulseDesk.csproj` as the current app entry point. Make focused changes in the owning XAML or code-behind first instead of spreading small features across unrelated files.
- Prefer native Windows desktop patterns: XAML for presentation, code-behind for small view logic, and extracted services or models once behavior stops being UI-local.
- Keep `App` and `MainWindow` thin. If telemetry, analysis, storage, or tray behavior grows, shape it so it can move cleanly into future projects such as `PulseDesk.Telemetry`, `PulseDesk.Analysis`, `PulseDesk.Storage`, and `PulseDesk.Tray`.
- Preserve existing namespace and file naming consistency. New types should use clear PascalCase names and avoid one-off abbreviations.
- Do not edit generated or build output content under `bin/`, `obj/`, or generated `.g.cs` and `.xbf` files. Change source files instead.
- Only change packaging files such as `app.manifest`, `Package.appxmanifest`, or publish profiles when the task is explicitly about packaging, identity, capabilities, or deployment.
- For UI work, favor readable XAML, theme-aware Windows styling, and controls already used by the app or current dependencies such as CommunityToolkit WinUI controls.
- Remove obviously unused template code or usings when touching a file, but do not perform unrelated cleanup across the repo.
- When adding system health features or user-facing descriptions, keep the scope aligned with CPU, RAM, GPU, disk, network, temperatures, and bottleneck visibility.
- Validate with the narrowest useful check after edits, preferably a focused build of the app project before broader verification.
- For the UI components, must use https://github.com/CommunityToolkit/Windows controls where possible to maintain consistency and reduce custom code.