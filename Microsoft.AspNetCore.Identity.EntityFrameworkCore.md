# [[Microsoft.AspNetCore.Identity.EntityFrameworkCore]]

> **Node Type**: Core Security & Identity Framework Node  
> **Package**: `Microsoft.AspNetCore.Identity.EntityFrameworkCore`  
> **Target Framework**: [[.NET 10]] (`net10.0`)  
> **Central Node**: [[Project-State]]

---

## 1. Architectural Purpose & Overview

[[Microsoft.AspNetCore.Identity.EntityFrameworkCore]] provides the foundational identity, user management, and role-based membership system for [[Photo Aevum Studio]].

Configured inside [[ApplicationDbContext.cs]], it manages standard identity schemas:
* `dbo.AspNetUsers`: Authentication credentials, concurrency stamps, and security identifiers tied to [[ApplicationUser]].
* `dbo.AspNetRoles`: System authorization tiers mapped to [[AppRoles]] (`SystemAdmin`, `Admin`, `Employee`, `Customer`).
* `dbo.AspNetUserRoles` & claims tables: Multi-role resolution and user claim persistence.

## 2. Security Pipeline Integration

* **Authentication & Token Issuance**: Interfaced by [[AuthController.cs]] and [[SqlAuthService.cs]] to validate user logins, verify [[BCrypt.Net-Next]] hashes, and issue signed stateless [[JWT Bearer]] tokens.
* **Client Authentication State**: Read by [[AevumAuthenticationStateProvider.cs]] in the Blazor frontend to manage authentication cascades and authorize UI views.
* **Separation of Concerns**: While Identity handles user credentials and role claims, extended business profiles reside in dedicated tables (`dbo.Employees`, `dbo.Customer`, `dbo.Admin`, `dbo.SystemAdmin`).

## 3. Key Relationships & Dependencies

* Database Context: [[ApplicationDbContext.cs]]
* User Entity: [[ApplicationUser.cs]]
* Roles: [[AppRoles.cs]]
* Authentication Services: [[SqlAuthService.cs]], [[AuthController.cs]], [[AevumAuthenticationStateProvider.cs]]
* Reference: [[Project-State#5-security--authentication-architecture]]

---
*Back to: [[Project-State]]*
