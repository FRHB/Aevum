# Project State: [[Photo Aevum Studio]]

> **Master Architecture & State Registry**  
> **Last Updated**: 2026-10-27  
> **Target Framework**: [[.NET 10]] (`net10.0`) | **Language**: [[C# 13]]  
> **Root Workspace**: `C:\Aevum` | **Central Node**: [[Project-State]]

---

## 1. Executive Overview & Solution Topology

[[Photo Aevum Studio]] is a studio management and booking platform built on [[.NET 10]]. It supports end-to-end studio operations including client booking flows, conflict-aware appointment scheduling, gear and equipment tracking, staff assignment and specialization, timesheet and leave logging, and client portfolio galleries.

```
C:\Aevum\
├── PhotoAevumStudioBackend/
│   ├── PhotoAevumStudio.Api/               # ASP.NET Core Web API (Endpoints, JWT, Swagger, Middlewares)
│   ├── PhotoAevumStudio.Application/       # Core domain models, DTOs, FluentValidation rules, interfaces
│   ├── PhotoAevumStudio.Infrastructure/    # EF Core DbContext, ADO.NET SQL services, R2 Storage, Auth
│   └── database/                           # T-SQL schema DDL and Stored Procedures scripts
├── PhotoAevumStudioFrontend/
│   ├── PhotoAevumStudio.Web/               # Blazor Web App (Interactive Server, Pages, Components, Layouts)
│   └── Tests/PhotoAevumStudio.Web.Tests/   # Unit and component integration tests (xUnit)
├── .agyrules                               # Global directives for AI-assisted development
├── Logs/                                   # Chronological weekly dev logs (Dev-Log-YYYY-WeekXX.md)
└── Project-State.md                        # Master architectural registry and current state
```

### Architectural Decisions on Deprecated Frameworks
* **[[.NET MAUI]]**: Originally scaffolded in `PhotoAevumStudio.Maui` along with `PhotoAevumStudio.SharedCore` and `PhotoAevumStudio.Shared.UI`. These were deprecated and removed during Week 41 (commit `a725c18`) to consolidate all client-side interactions within a responsive [[Blazor Web]] application.
* **[[Dapper]] vs. [[Entity Framework Core 10]] & Stored Procedures**: Evaluated for data access. Rather than introducing Dapper as an external dependency, the architecture employs a intentional **hybrid pattern**:
  1. [[ApplicationDbContext]] ([[Entity Framework Core 10]]) for ASP.NET Core Identity, relational entity tracking, migrations, and CRUD operations on core domain models.
  2. Direct [[Microsoft.Data.SqlClient]] ADO.NET connections targeting custom [[Stored Procedures]] (`PhotoAevumStudioStoredProcedures.sql`) for transactional safety, atomic multi-table inserts, custom formatted ID generation, and scheduling conflict validation.

---

## 2. Technology Stack & Dependencies

| Area | Technology / Library | Version | Role & Description |
| :--- | :--- | :--- | :--- |
| **Runtime & SDK** | [[.NET 10]] (`net10.0`) | 10.0.14 | Underlying cross-platform framework |
| **Language** | [[C# 13]] | Latest | Core programming language |
| **Frontend UI** | [[Blazor Web]] | 10.0.13 | Interactive Server components, responsive layouts |
| **Backend API** | [[ASP.NET Core Web API]] | 10.0.14 | RESTful controllers, JWT bearer token endpoints |
| **ORM / Data Access** | [[Entity Framework Core 10]] | 10.0.14 | Object-relational mapping, Identity models |
| **Database Driver** | [[Microsoft.Data.SqlClient]] | 6.0.1 | High-performance ADO.NET execution of stored procedures |
| **Database Engine** | [[SQL Server]] | Remote / Live | Enterprise relational DBMS hosted on MonsterASP.NET |
| **Object Storage** | [[AWSSDK.S3]] / [[Cloudflare R2]] | 4.0.104.6 | S3-compatible cloud storage for photo galleries and assets |
| **Security & Auth** | [[BCrypt.Net-Next]] | 4.2.0 | Cryptographic salt and hash algorithm for user passwords |
| **Token Auth** | [[System.IdentityModel.Tokens.Jwt]] | 8.3.0 | Issuance and validation of stateless JWT bearer tokens |
| **Validation** | [[FluentValidation]] | 11.11.0 | Strongly typed server-side request model validation |
| **Testing** | `xUnit`, `bUnit`, `Moq` | Latest | Unit, domain, and Blazor component testing |

---

## 3. Database Architecture & Schema Rules

The schema is defined in [`PhotoAevumStudio.sql`](file:///C:/Aevum/PhotoAevumStudioBackend/database/PhotoAevumStudio.sql) and complemented by stored procedures in [`PhotoAevumStudioStoredProcedures.sql`](file:///C:/Aevum/PhotoAevumStudioBackend/database/PhotoAevumStudioStoredProcedures.sql).

### Table Inventory

#### 1. Identity & Role Entities
* `dbo.Admin`: System administrators and studio managers (`AdminId`, `FullName`, `Email`, `PasswordHash`, `IsFirstLogin`, `MustChangePassword`).
* `dbo.SystemAdmin`: Superusers with unrestricted platform permissions (`SystemAdminId`, `Username`, `PasswordHash`).
* `dbo.Employees`: Photographers, videographers, editors, assistants (`EmployeeID`, `FirstName`, `LastName`, `Email`, `ContactNumber`, `HireDate`, `Status`, `IsFirstLogin`).
* `dbo.Customer`: Studio clientele (`CustomerID`, `FullName`, `Email`, `ContactNumber`, `Address`, `CreatedAt`).
* `dbo.AspNetUsers` & `dbo.AspNetRoles`: ASP.NET Core Identity integration tables managed by [[ApplicationDbContext]].

#### 2. Staffing & Capabilities
* `dbo.EmployeeSpecialization`: Specialization taxonomy (`SpecializationID`, `SpecializationName`, `Description`).
* `dbo.EmployeeJointSpecialization`: Many-to-many relationship mapping employees to their verified specializations.
* `dbo.EmployeeLeave`: Time-off records and approvals (`LeaveId`, `EmployeeId`, `StartDate`, `EndDate`, `Reason`, `Status`).
* `dbo.EmployeeTimesheet`: Hours logged per project and shift (`TimesheetId`, `EmployeeId`, `EventId`, `HoursWorked`, `DateLogged`).

#### 3. Equipment & Inventory
* `dbo.EquipmentType`: Categories of studio hardware (e.g., Camera Bodies, Lenses, Lighting, Stabilizers).
* `dbo.Equipment`: Physical serialized gear inventory (`EquipmentID`, `EquipmentName`, `TypeID`, `Brand`, `Model`, `SerialNumber`, `Status`, `PurchaseDate`).

#### 4. Service Catalog & Bundles
* `dbo.Services`: Standalone core services offered by the studio (`ServiceID`, `ServiceName`, `Description`, `BasePrice`, `DurationMinutes`).
* `dbo.ServiceCategory`: Logical categorization of services (`CategoryID`, `CategoryName`).
* `dbo.ServiceCategoryJoins`: Relationship mapping between services and categories.
* `dbo.AdditionalServices`: Add-on enhancements (e.g., drone coverage, extra retouched photos, expedited delivery).
* `dbo.ComboPackage`: Pre-bundled packages combining multiple services and add-ons (`PackageID`, `PackageName`, `DiscountRate`, `PackagePrice`).
* `dbo.ComboPackageJoin`: Relational joins linking packages with individual services.

#### 5. Bookings, Appointments & Workflow
* `dbo.EventAppointment`: Master booking and shoot appointment record (`AppointmentID`, `CustomerID`, `PackageID`, `EventDate`, `StartTime`, `EndTime`, `Location`, `Status`, `TotalAmount`).
* `dbo.BookingNegotiation`: Record of custom pricing or scheduling revisions agreed upon with clients.
* `dbo.EventEmployee`: Roster of employees assigned to a specific event.
* `dbo.EventEquipment`: Roster of equipment units checked out for a specific event.
* `dbo.EventLineItem`: Individual line items, add-ons, or special fees tied to an appointment.
* `dbo.HistoryLog`: Comprehensive audit trail for system and user actions (`LogID`, `ActionType`, `EntityName`, `EntityID`, `Timestamp`, `PerformedBy`).

#### 6. Media & Galleries
* `dbo.Photos`: Watermarked previews and delivered high-resolution photographs (`PhotoId`, `BookingId`, `Title`, `Url`, `UploadedAt`).
* `dbo.Portfolios`: Public showcase albums (`PortfolioId`, `Title`, `Description`, `CategoryId`, `CoverImageUrl`).
* `dbo.PortfolioCategories`: Portfolio groupings (e.g., Weddings, Portraits, Commercial, Events).
* `dbo.PortfolioImages`: Individual images attached to showcase portfolios.

---

## 4. Stored Procedures & Business Rules

All high-impact transactions must use the verified stored procedures in [`PhotoAevumStudioStoredProcedures.sql`](file:///C:/Aevum/PhotoAevumStudioBackend/database/PhotoAevumStudioStoredProcedures.sql):

1. **Custom Formatted Identifiers**:
   * `dbo.sp_GetNextServiceID` & `dbo.sp_GetNextCategoryID`: Generates zero-padded alphanumeric IDs (e.g., `SRV-0001`, `CAT-0001`).
   * `dbo.sp_GetNextEquipmentTypeID` & `dbo.sp_GetNextEquipmentIdPreview`: Generates unique equipment hardware identifiers.
   * `dbo.sp_GetNextAdditionalServiceID` & `dbo.sp_GetNextComboPackageId`: Alphanumeric sequencing for packages and add-ons.
2. **Scheduling & Conflict Prevention**:
   * `dbo.sp_CreateEventAppointment`: Validates customer status and reserves the date-time envelope.
   * `dbo.sp_AssignEmployeeToEvent`: Prevents double-booking staff across overlapping events and respects active leaves in `dbo.EmployeeLeave`.
   * `dbo.sp_AssignEquipmentToEvent`: Prevents double-assignment of serialized equipment items across active reservations.
3. **First-Time Login Security**:
   * `dbo.sp_CompleteAdminFirstLogin` & `dbo.sp_CompleteEmployeeFirstLogin`: Atomically updates the initial temporary password hash and flips `IsFirstLogin` / `MustChangePassword` flags to `FALSE`.

---

## 5. Security & Authentication Architecture

* **Role-Based Access Control ([[AppRoles]])**:
  * `SystemAdmin`: Full system maintenance, database schema migrations, and user provisioning.
  * `Admin`: Daily studio operations, booking approvals, pricing management, and equipment assignments.
  * `Employee`: Personalized schedule viewing, equipment checklists, assignment confirmation, and timesheet submission.
  * `Customer`: Self-service booking flow, appointment status tracking, profile management, and private gallery viewing.
* **Authentication Pipeline**:
  * [[SqlAuthService]] evaluates login attempts by scanning `dbo.SystemAdmin`, `dbo.Admin`, `dbo.Employees`, and `dbo.Customer` using [[BCrypt.Net-Next]] password verification.
  * [[AuthController]] issues signed [[JWT Bearer]] tokens encoding the user identifier and role claim.
  * [[AevumAuthenticationStateProvider]] in the Blazor frontend maintains user state, checks token validity, and routes users to appropriate dashboard portals.
  * [[AuthHttpHandler]] automatically decorates outbound HTTP requests with the authorization header.

---

## 6. Media Storage Strategy

* **Provider**: [[R2StorageService]] implements `IStorageProvider` via [[AWSSDK.S3]].
* **Endpoint**: Cloudflare R2 S3-compatible API.
* **Separation of Assets**:
  * `thumbnails/`: Optimized low-resolution web previews.
  * `watermarked/`: Client proofing gallery photographs before final delivery.
  * `deliverables/`: Full-resolution master assets accessible via temporary pre-signed S3 download URLs.
  * `portfolio/`: Publicly cached showcase imagery served through the studio CDN.

---

## 7. Deployment, DevOps & Environment Configuration

* **Hosting Environment**: Production instance hosted on [[MonsterASP.NET]].
* **Continuous Deployment**: Automated GitHub Actions workflow located in [`.github/workflows/deploy.yml`](file:///C:/Aevum/.github/workflows/deploy.yml). Deploys compiled `.NET 10` Web API and Blazor web outputs via secure FTP.
* **Secrets Management**:
  * Local Development: Stored in `dotnet user-secrets` under ID `fd9f2f0d-5dc2-45ec-adcf-622565bb6148`.
  * Production: Injected via MonsterASP.NET environment variables and GitHub Repository Secrets (`FTP_SERVER`, `FTP_USERNAME`, `FTP_PASSWORD`, `DB_CONNECTION_STRING`, `JWT_SECRET`, `R2_ACCESS_KEY`, `R2_SECRET_KEY`).

---

## 8. Current Feature State & Active Roadmap

* [x] Core Authentication and Role-based Dashboards ([[AdminDashboard]], [[StaffDashboard]], [[CustomerDashboard]]).
* [x] Equipment and Inventory tracking ([[Equipments.razor]], [[EquipmentTypes.razor]], [[GearChecklist.razor]]).
* [x] Service Catalog and Multi-Service Bundling ([[MainServices.razor]], [[CategoryServices.razor]], [[PackageServices.razor]]).
* [x] Conflict-Aware Scheduling Engine ([[ConflictAwareScheduler.razor]], [[Schedules.razor]]).
* [x] Employee Specializations and First-Time Credential Wizards ([[AddEmployeeWizard.razor]], [[Specialization.razor]]).
* [x] Employee Timesheets and Leave Management ([[TimesheetController]], [[LeavesController]]).
* [x] Cloudflare R2 Uploads and Portfolio Management ([[Portfolio.razor]], [[PortfolioImages.razor]], [[UploadController]]).
* [ ] Final customer payment gateway integration (Stripe / PayMongo).
* [ ] Automated SMS / Email notification dispatch upon booking confirmation.

---
*Back to: [[Project-State]]*
