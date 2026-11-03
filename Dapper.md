# [[Dapper]]

> **Node Type**: Evaluated Technology / Data Access Architecture  
> **Status**: Evaluated & Bypassed (Hybrid Pattern Adopted)  
> **Target Framework**: [[.NET 10]] (`net10.0`)  
> **Central Node**: [[Project-State]]

---

## 1. Architectural Purpose & Overview

[[Dapper]] is a high-performance micro-ORM for .NET that was formally evaluated during Week 41 (Phase 3 Refactoring) for executing high-throughput queries against [[SQL Server]].

Rather than introducing Dapper as a separate third-party NuGet dependency, the [[Photo Aevum Studio]] architecture intentionally committed to a **hybrid data access pattern**:
1. **[[Entity Framework Core 10]]** via [[ApplicationDbContext]] handles relational entity tracking, ASP.NET Core Identity (`dbo.AspNetUsers`, `dbo.AspNetRoles`), migrations, and baseline domain entity CRUD operations.
2. **Direct [[Microsoft.Data.SqlClient]]** ADO.NET connections execute custom [[Stored Procedures]] (`PhotoAevumStudioStoredProcedures.sql`) for transactional safety, atomic multi-table inserts, zero-padded identifier generation, and scheduling conflict validation.

## 2. Key Relationships & Dependencies

* Evaluated alongside: [[Entity Framework Core 10]]
* Replacement / Direct Pattern: [[Microsoft.Data.SqlClient]] with [[Stored Procedures]]
* Related Services: [[StudioManagementService.cs]], [[EmployeeService.cs]], [[ScheduleBookingService.cs]]
* Reference: [[Project-State#1-executive-overview--solution-topology]]

---
*Back to: [[Project-State]]*
