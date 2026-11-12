# Caching Business Requirements

## Module scope
This module covers the shared caching behavior used across the combined system, including local and database-backed storage for neutral calculation results and repeated lookups.

## Business objectives
The system shall improve response times and reduce repeated computation while preserving result correctness and protecting against stale or unsafe cached data.

## Functional requirements
### 1. Cache layers
1. The system shall use a bounded in-memory L1 cache as the first-level local cache.
2. The system shall optionally use a PostgreSQL-backed L2 cache for durable, repeatable read results.
3. The system shall maintain a typed key strategy and versioned cache model.

### 2. Cache validity and invalidation
1. The system shall derive cache validity from calculation inputs, location and timezone context, and engine version.
2. The system shall support invalidation based on rule version, calculation version, and date boundaries such as local midnight.
3. The system shall ensure that stale data does not persist across incompatible calculation or convention changes.

### 3. Safe computation reuse
1. The system shall allow neutral daily Panchang and related results to be cached for reuse across requests.
2. The system shall support cache reuse for related evaluation workflows without caching user-specific security or profile data.
3. The system shall treat cache misses as a safe fallback rather than a service failure.

### 4. Performance and reliability
1. The system shall prevent cache stampedes for simultaneous cold misses within a process.
2. The system shall fail gracefully when cache storage is unavailable or slow.
3. The system shall use bounded payload sizes and memory limits to avoid unbounded resource growth.

## Business rules
1. Cache entries shall be safe to reuse only when they are derived from stable inputs and versioned calculation semantics.
2. Security-sensitive data such as tokens, user profiles, authorization decisions, and account state shall not be stored in the shared cache.
3. Cache failures shall not replace valid business results or reduce the correctness of the calculation flow.

## Non-functional requirements
1. The cache system shall support monitoring and telemetry for lookup, factory, serialization, and backend operations.
2. The system shall preserve existing API response contracts even when the cache is unavailable.
3. The cache implementation shall remain separate from domain core logic and remain behind a service abstraction.

## External dependencies
- PostgreSQL database for L2 cache storage when enabled
- .NET hybrid caching libraries and metadata services
- calculation versioning and validation services
- telemetry and observability stack
