# [[Employee]]

> **Node Type**: Core Domain Entity & Staff Management Node  
> **Database Table**: `dbo.Employees`  
> **Role Mapping**: [[AppRoles]] (`Employee`, `Office Clerk`)  
> **Central Node**: [[Project-State]]

---

## 1. Architectural Purpose & Overview

The [[Employee]] domain entity models all internal studio personnel—including photographers, videographers, editors, assistants, and office clerks—operating within [[Photo Aevum Studio]].

It encapsulates personnel metadata (`EmployeeID`, `FirstName`, `LastName`, `Email`, `ContactNumber`, `HireDate`, `Status`, `IsFirstLogin`), role assignments, and capabilities required for studio scheduling and project execution.

## 2. Core Workflows & Architectural Rules

1. **Specialization Taxonomy**:
   * Linked via `dbo.EmployeeJointSpecialization` to `dbo.EmployeeSpecialization`.
   * Staff specializing as `Office Clerk` are exclusively authorized to manage client consultations and deal negotiations in [[BookingNegotiations.razor]].
2. **Onboarding & First-Time Login**:
   * Provisioned by Administrators via [[AddEmployeeWizard.razor]] with a temporary password hash.
   * On initial login, forced redirection to [[ChangePassword.razor]] triggers [[sp_CompleteEmployeeFirstLogin]], updating credentials and clearing `IsFirstLogin`.
3. **Scheduling & Timesheets**:
   * Assigned to shoot sessions via [[ConflictAwareScheduler.razor]] and stored procedures (`sp_AssignEmployeeToEvent`).
   * Tracks daily shifts via `dbo.EmployeeTimesheet` ([[Timesheet.razor]]) and leave requests via `dbo.EmployeeLeave`.
4. **Snapshot Hard Delete Pattern**:
   * When removed via `dbo.spDeleteEmployee`, foreign keys on `dbo.EventAppointment`, `dbo.BookingNegotiation`, and `dbo.PaymentTransactions` set `EmployeeID` to `NULL` while preserving historical records through the snapshotted `AssignedEmployeeName` column.

## 3. Key Relationships & Dependencies

* Backend Service: [[EmployeeService.cs]] / [[IEmployeeService.cs]]
* Controller: [[EmployeesController.cs]]
* UI Components: [[Employee.razor]], [[AddEmployeeWizard.razor]], [[StaffDashboard.razor]], [[Specialization.razor]]
* Database Tables: `dbo.Employees`, `dbo.EmployeeSpecialization`, `dbo.EmployeeJointSpecialization`, `dbo.EmployeeTimesheet`, `dbo.EmployeeLeave`
* Reference: [[Project-State#1-identity--role-entities]]

---
*Back to: [[Project-State]]*
