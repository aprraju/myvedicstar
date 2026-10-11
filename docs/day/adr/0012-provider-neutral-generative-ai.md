# ADR-0012: Provider-neutral generative AI, limited to explanation and classification

- Status: Accepted
- Date: 2026-10-01
- Design: [Find a Good Time §11](../architecture/find-a-good-time.md#11-generative-ai-integration)

## Context

The product wants generative AI in Find a Good Time, with a developer-configurable provider (Claude, ChatGPT,
"Copilot"). The same requirements make the backend rules engine authoritative for times and statuses, and require
the same result on every screen. LLM output is non-deterministic and can invent times.

## Decision

1. **The AI never decides** status, times, ranking or Good Time qualification. It may:
   - write the "Why this time?" narrative from the engine's structured result;
   - map "Other" free text to a known activity key (the engine then evaluates that activity).
2. **Platform module `MyVedicDay.Ai.Integration`** exposes a structured-generation port over
   `Microsoft.Extensions.AI.IChatClient`. Providers are chosen by configuration (`Ai:Provider`):
   - `Anthropic`: the official `Anthropic` NuGet package; default model `claude-opus-5-5`, set in configuration (`Ai:Anthropic:Model`);
   - `OpenAI`: `Microsoft.Extensions.AI.OpenAI`;
   - `AzureOpenAI`: Azure OpenAI / Azure AI Foundry with managed identity. This is the Microsoft route the "Copilot"
     requirement refers to (decision D4);
   - `None`.
3. **Guardrails:**
   - input is the structured evaluation, with no PII;
   - output is schema-constrained JSON;
   - every segment id and time in the output must exist in the input;
   - on refusal, timeout, validation failure or an open circuit, the deterministic template text is used;
   - prompts are versioned data;
   - keys come from Key Vault or user-secrets.
4. **Disabled by default.** The feature is complete without AI.

## Consequences

- Switching provider or model is configuration only. Each switch is re-checked with the explanation eval set before
  production.
- Explanation requests are a separate endpoint and cache, so LLM latency or cost never affects recommendations.
