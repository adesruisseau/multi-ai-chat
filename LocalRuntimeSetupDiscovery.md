# Local Runtime Setup Discovery

Updated: 2026-05-21

## Purpose

This note captures discovery work around whether Piper, Kokoro, and Ollama could be installed or managed from inside Agent Group Chat, or whether they are better handled by an external installer.

The goal is to preserve the findings for later design work on the Setup Guide and future packaging.

## Current App Assumptions

- Piper is currently treated as a local executable plus model files.
- Kokoro is currently treated as a separate local HTTP service that exposes an OpenAI-compatible speech endpoint.
- Ollama is currently treated as an external local service available at `http://localhost:11434/api/chat`.

Relevant local anchors:

- `MainWindow.Tts.cs` launches Piper directly with `ProcessStartInfo` and reads Piper voice models from disk.
- `ConnectionSettingsStore.cs` seeds default Piper paths in LocalAppData and seeds the default Ollama endpoint.
- `Kokoro-FastAPI/start-cpu.ps1` installs Python dependencies with `uv`, downloads the Kokoro model, and launches the FastAPI server on port `8880`.

## Findings By Runtime

### Piper

Piper is the easiest candidate for an in-app install flow.

Why:

- The app already expects a `piper.exe` path and a models directory.
- Runtime usage is simple: launch the executable, pass a model path, stream raw PCM back, and play it.
- A minimal installer flow could download:
  - `piper.exe`
  - one or more `.onnx` voice models
  - their matching `.json` metadata files
- Those assets can live in LocalAppData without changing the app architecture much.

Practical result:

- A Setup Guide button such as `Install Piper` is realistic.
- The app could also offer `Download starter voice` and `Verify Piper` actions.

Risks:

- Piper voice assets can be large.
- The original `rhasspy/piper` repository is archived, so source and distribution strategy should be chosen carefully.

### Kokoro

Kokoro is possible to orchestrate from the app, but it is much heavier than Piper.

Why:

- The current Windows startup path depends on `uv`.
- The startup flow installs Python dependencies.
- The startup flow downloads model files.
- The startup flow depends on an eSpeak NG DLL path being available.
- The runtime is a separate FastAPI service, not just a single local binary.

Practical result:

- A true `Install Kokoro` button inside the app would need to manage:
  - prerequisite detection
  - dependency installation
  - model download
  - service startup and shutdown
  - health checks against the `http://127.0.0.1:8880/v1/audio/speech` endpoint
- This is feasible, but it is closer to building a small service manager than just adding a download button.

Best interpretation:

- Kokoro is a good fit for a guided setup experience.
- Kokoro is not yet a great fit for the first "simple one-click install" milestone unless the app is willing to own Python-side lifecycle and failure handling.

### Ollama

Ollama is medium difficulty, but it should be treated as an external install rather than something the app tries to repackage manually.

Why:

- The app only needs the local HTTP API once Ollama is installed.
- Official Windows install already exists through `OllamaSetup.exe` or the published PowerShell install command.
- Model acquisition is a separate step after install, typically through `ollama run` or `ollama pull`.

Practical result:

- The Setup Guide can reasonably offer:
  - `Install Ollama`
  - `Verify Ollama`
  - `Pull recommended model`
- The cleanest implementation is to hand off to the official installer, then guide the user through model pull and connectivity checks.

## Inside-The-App Setup vs Installer Bundling

### Option A: Install From Inside The App

Best for:

- Piper first
- guided verification for Ollama
- status checks for Kokoro

Advantages:

- Smooth onboarding inside the existing Setup Guide
- The app can detect status and explain next actions in one place
- Easier to iterate than a full installer redesign

Disadvantages:

- More runtime orchestration inside the WPF app
- More edge cases around downloads, retries, permissions, antivirus, and partial installs
- Kokoro setup becomes substantially more complex because of Python, `uv`, models, and service management

### Option B: Put It In The Installer

Best for:

- preflight checks
- optional downloads during setup
- shipping a more polished distribution to coworkers

Advantages:

- Better first-run experience for non-technical users
- Installer is a more natural home for prerequisites and optional runtime bundles
- Easier to present component choices before the app opens

Disadvantages:

- Larger packaging effort
- Harder iteration loop during development
- Bundling large models directly into the installer can inflate size quickly
- Kokoro still needs thought around Python environment ownership unless it is converted into a more self-contained distribution

### Option C: Hybrid Approach

This is the strongest current recommendation.

Suggested split:

- Piper: support true in-app install first
- Ollama: launch the official installer, then verify and optionally pull a recommended model from inside the app
- Kokoro: start with detection, verification, and guided instructions; defer full embedded install until the app is ready to own service lifecycle and prerequisites

## Recommended Sequence

### Phase 1: Detection And Status UI

Add Setup Guide status cards for:

- Piper installed or missing
- Kokoro service reachable or unreachable
- Ollama installed or missing

Each card should support `Detect`, `Test`, and `Learn More` style actions.

### Phase 2: Piper One-Click Install

Implement the simplest true install flow first:

- download `piper.exe`
- download one starter voice model and metadata
- save into LocalAppData
- refresh detected voices
- mark the setup step complete

### Phase 3: Ollama Guided Install

Add a guided flow that:

- launches the official Windows installer
- waits for or re-checks API availability
- optionally runs `ollama pull` for a recommended starter model
- updates the local model catalog suggestions in the app

### Phase 4: Kokoro Guided Or Managed Install

Start with:

- detection
- verification
- local instructions

Only later decide whether the app should fully own:

- `uv` installation
- Python environment setup
- model download
- service startup and restart
- eSpeak dependency handling

## Product Direction Recommendation

If the near-term goal is easier onboarding for coworkers, do not start with full Kokoro embedding.

The highest-value path is:

1. Add install-status cards to the Setup Guide.
2. Make Piper a real one-click install.
3. Make Ollama a guided external install plus model pull.
4. Treat Kokoro as guided setup first, full embedding later.

That sequence improves onboarding quickly without turning the WPF app into a full general-purpose dependency installer on day one.

## Source Notes

Local repo references used during discovery:

- `MainWindow.Tts.cs`
- `ConnectionSettingsStore.cs`
- `Kokoro-FastAPI/start-cpu.ps1`
- `Kokoro-FastAPI/README.md`

External references used during discovery:

- Ollama official quickstart and repository install notes
- Piper repository and release/distribution notes
