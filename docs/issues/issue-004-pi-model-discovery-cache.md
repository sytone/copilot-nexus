# Issue 004: Repeated Pi model discovery caused startup probe churn

## Symptoms

- Service startup validation intermittently timed out during rapid health/model probing.
- Runtime logs showed repeated model-catalog discovery attempts in a short window.
- In loop runs, this could surface as delayed starts or `nexus failed start validation` failures.

## Root cause

- Model listing triggered a fresh RPC discovery each time, which launches and negotiates a short-lived Pi RPC process.
- Startup probes can call model-related endpoints repeatedly before warm state is established.
- Concurrent requests had no shared cache/serialization, amplifying RPC load and latency.

## Fix

- Added in-memory model-catalog caching in `PiRpcClientService` with a 30-second TTL.
- Added a `SemaphoreSlim` guard so concurrent callers share a single refresh path.
- Reset model cache state on service stop to keep lifecycle behavior deterministic.
- Preserved fallback catalog behavior when discovery fails.

## Expected result

- Startup probes no longer trigger repeated expensive model discovery bursts.
- Service readiness checks stabilize under rapid publish/start/restart loops.
- Model endpoint behavior remains resilient with fallback models on transient RPC failures.
