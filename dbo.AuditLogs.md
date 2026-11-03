# [[dbo.AuditLogs]]

> **Node Type**: Database Entity / Audit Trail  
> **DBMS**: [[SQL Server]] (Hosted on [[MonsterASP.NET]])  
> **ORM / Access**: [[ApplicationDbContext]] / [[AuditLoggingMiddleware.cs]]  
> **Central Node**: [[Project-State]]

---

## 1. Architectural Purpose & Overview

`dbo.AuditLogs` is a dedicated database table designed to record system operations, administrative actions, and security-relevant modifications across [[Photo Aevum Studio]].

Populated via [[AuditLoggingMiddleware.cs]], it captures request telemetry, operator identity, target entities, timestamps, and state transitions to satisfy regulatory and diagnostic traceability.

## 2. Historical Archival & Data Lifecycle

To prevent table bloat and preserve low query latencies on production [[SQL Server]]:
* Managed by [[DataArchiveService.cs]] (implementing [[IDataArchiveService.cs]]).
* Inactive records older than 1 year are serialized to JSON, streamed to the [[Cloudflare R2]] `archive` bucket (`{year}/AuditLogs-{timestamp}.json`), indexed in `dbo.ArchiveIndex` ([[ArchiveIndex.cs]]), and hard-deleted from `dbo.AuditLogs`.
* Works in parallel with `dbo.HistoryLog` for studio transaction auditing.

## 3. Key Relationships & Dependencies

* Middleware: [[AuditLoggingMiddleware.cs]]
* Database Context: [[ApplicationDbContext.cs]]
* Archival Service: [[DataArchiveService.cs]]
* Object Storage: [[Cloudflare R2]] (`archive` bucket)
* Related Tables: `dbo.HistoryLog`, `dbo.ArchiveIndex`
* Reference: [[Project-State#3-database-architecture--schema-rules]]

---
*Back to: [[Project-State]]*
