# Voice Concierge — The Meridian Casino & Resort

A voice concierge for a Las Vegas casino & resort. LiveKit agent answers spoken
guest questions from a seeded knowledge base; React admin panel manages FAQs,
voices, and a test playground.

Backend + agent: **100% C#/.NET 10**. Admin: React + Vite + TS.

## Stack

| Layer | Tech |
|---|---|
| API | ASP.NET 10 Minimal API |
| Agent | C# `BackgroundService` + SIPSorcery (pure-C# LiveKit client) |
| DB | Postgres 18 + pgvector |
| STT | Groq Whisper-large-v3 (fallback: local Whisper.net) |
| LLM | Groq Llama 3.3 70B |
| TTS | Microsoft Edge TTS (free, no key) |
| Voice transport | LiveKit Cloud |
| Admin UI | React 18 + Vite + TS |

## Prerequisites

- Docker + Compose v2 — that's it.

## Launch

```bash
docker compose up --build -d
```

The repo ships with a working `.env` (evaluation credentials for LiveKit Cloud
+ Groq). To swap in your own keys, edit `.env` directly or create `.env.local`
to override without touching git.

First build is slow (~5–10 min, bakes the Whisper model). After that, restarts are seconds.

## Verify

```bash
curl http://localhost:8080/health
# {"status":"ok","db":"configured","embedding":"ready",...}
```

Open http://localhost:5173 — four tabs:

| Tab | Purpose |
|---|---|
| **Playground** | Click *Start conversation*, talk to the concierge |
| **FAQ** | View / add / edit / delete the knowledge base |
| **Unanswered** | Triage questions the agent couldn't answer; convert or dismiss |
| **Voices** | Pick among James / Sofia / Marcus / Elena, preview each |

If `ADMIN_API_KEY` is set, paste it into the **Admin key** field (top-right).

## Services

| Service | Port | Healthcheck |
|---|---|---|
| `db` (postgres + pgvector) | 5433 | `pg_isready` |
| `voiceconcierge-api` | 8080 | `GET /health` |
| `voiceconcierge-ui` (nginx) | 5173 | `GET /` |
| `voiceconcierge-agent` | 9090 | `GET /healthz` |

## Repo layout

```
src/
├── VoiceConcierge.Api/        ASP.NET API (FAQ search, admin, LiveKit token)
├── VoiceConcierge.Agent/      C# voice worker (VAD → STT → RAG → LLM → TTS)
├── VoiceConcierge.Contracts/  Shared DTOs
└── VoiceConcierge.Ui/         React admin panel
tests/
├── VoiceConcierge.Api.Tests/    integration tests (Testcontainers Postgres)
└── VoiceConcierge.Agent.Tests/  pipeline + audio unit tests
```

Production code follows seqaro vertical-slice + `Configuration.cs` per feature.
Tests mirror the source tree.

## Common ops

```bash
docker compose down              # stop, keep data
docker compose down -v           # stop + wipe DB
dotnet test                      # 96 tests; needs Docker for Postgres testcontainers
```

## Troubleshooting

| Symptom | Fix |
|---|---|
| `set LIVEKIT_API_SECRET in .env` on startup | Missing credential, fill `.env` |
| `embedding: degraded` in `/health` | Model still loading; wait ~30s |
| Playground connects but agent silent | Verify `LIVEKIT_URL` is `wss://…livekit.cloud` (project URL, not dashboard URL) |
| Agent mistranscribes everything | Groq key invalid or rate-limited; check agent logs |
| Voice speed feels off | Tune `ProsodyRate` in `src/VoiceConcierge.Contracts/EdgeTtsClient.cs` (currently `-25%`) and rebuild api + agent |
