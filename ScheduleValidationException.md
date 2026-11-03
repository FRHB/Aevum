# [[ScheduleValidationException]]

> **Node Type**: Domain Exception / Scheduling Validation Node  
> **Namespace**: `PhotoAevumStudio.Application.Scheduling`  
> **Target Framework**: [[.NET 10]] (`net10.0`)  
> **Central Node**: [[Project-State]]

---

## 1. Architectural Purpose & Overview

`ScheduleValidationException` is a strongly typed domain exception in [[Photo Aevum Studio]] declared as:
```csharp
public sealed class ScheduleValidationException(string message) : Exception(message);
```

Its primary architectural purpose is to encapsulate business rule failures during scheduling, booking, or consultation dispatching into user-safe validation errors that can be exposed directly to the UI without leaking internal database or infrastructure details.

## 2. Scheduling & Office Clerk Dispatch Pipeline

1. **Database Validation**:
   * Custom stored procedures (such as `dbo.sp_CreateEventAppointment` or consultation assignment routines in [[PhotoAevumStudioStoredProcedures.sql]]) raise `RAISERROR 50000` when validation fails—such as when an event time slot conflicts, or when no qualified `Office Clerk` is available to handle a booking negotiation.
2. **Translation in Service Layer**:
   * In [[ScheduleBookingService.cs]], caught `SqlException` instances carrying error 50000 are inspected and translated directly into a strongly typed `ScheduleValidationException`.
3. **HTTP API Handling**:
   * [[ScheduleController.cs]] intercepts `ScheduleValidationException` and serializes it into an HTTP 400 Bad Request response containing a user-friendly validation payload.
4. **UI Notification**:
   * Blazor wizard modals ([[CustomerBookingFlow.razor]], [[BookScheduleWizardModal.razor]], [[BookingNegotiations.razor]]) consume the error response and display toast/inline alerts to guide user correction.

## 3. Key Relationships & Dependencies

* Model Declaration: `PhotoAevumStudio.Application/Scheduling/ScheduleModels.cs`
* Service Layer: [[ScheduleBookingService.cs]] / [[IScheduleBookingService.cs]]
* API Controller: [[ScheduleController.cs]]
* UI Consumers: [[CustomerBookingFlow.razor]], [[BookScheduleWizardModal.razor]], [[BookingNegotiations.razor]]
* Database Rules: [[PhotoAevumStudioStoredProcedures.sql]]
* Reference: [[Project-State#5-security--authentication-architecture]]

---
*Back to: [[Project-State]]*
