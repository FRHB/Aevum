/* =====================================================================
   Book Schedule wizard tables (idempotent - safe to run repeatedly).
   Executed at API startup by SchedulingSchema.EnsureAsync.
   ===================================================================== */

IF OBJECT_ID(N'dbo.Appointment', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Appointment (
        AppointmentID  int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Appointment PRIMARY KEY,
        CustomerID     nvarchar(450) NOT NULL,
        CustomerEmail  nvarchar(255) NOT NULL,
        CustomerName   nvarchar(255) NOT NULL,
        SlotDate       date          NOT NULL,
        SlotStart      time(0)       NOT NULL,
        SlotEnd        time(0)       NOT NULL,
        EventLocation  nvarchar(300) NULL,
        Platform       nvarchar(20)  NOT NULL,
        MeetingLink    nvarchar(500) NOT NULL,
        Status         nvarchar(20)  NOT NULL CONSTRAINT DF_Appointment_Status DEFAULT 'Confirmed',
        TotalPrice     money         NOT NULL,
        CreatedAt      datetime2     NOT NULL CONSTRAINT DF_Appointment_CreatedAt DEFAULT SYSUTCDATETIME(),
        RowVer         rowversion    NOT NULL,
        CONSTRAINT CK_Appointment_Platform CHECK (Platform IN ('Zoom','GoogleMeet','MicrosoftTeams')),
        CONSTRAINT CK_Appointment_Status   CHECK (Status IN ('Confirmed','Cancelled')),
        CONSTRAINT CK_Appointment_Slot     CHECK (SlotEnd > SlotStart)
    );
END;

/* Hard guarantee: one active (non-cancelled) client per slot.
   Even if application-level locking were bypassed, a second insert fails with error 2601. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Appointment_Slot' AND object_id = OBJECT_ID(N'dbo.Appointment'))
    CREATE UNIQUE INDEX UX_Appointment_Slot ON dbo.Appointment (SlotDate, SlotStart) WHERE Status <> 'Cancelled';

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Appointment_Customer' AND object_id = OBJECT_ID(N'dbo.Appointment'))
    CREATE INDEX IX_Appointment_Customer ON dbo.Appointment (CustomerID, SlotDate DESC);

IF OBJECT_ID(N'dbo.AppointmentService', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AppointmentService (
        AppointmentServiceID int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AppointmentService PRIMARY KEY,
        AppointmentID        int           NOT NULL,
        ServicesID           nvarchar(30)  NOT NULL,
        ServiceName          nvarchar(255) NOT NULL,  -- snapshot at booking time
        ServicePrice         money         NOT NULL,  -- snapshot at booking time
        CONSTRAINT FK_AppointmentService_Appointment FOREIGN KEY (AppointmentID)
            REFERENCES dbo.Appointment (AppointmentID) ON DELETE CASCADE,
        CONSTRAINT UQ_AppointmentService UNIQUE (AppointmentID, ServicesID)
    );
END;

IF OBJECT_ID(N'dbo.BookingOverview', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BookingOverview (
        OverviewDate      date       NOT NULL CONSTRAINT PK_BookingOverview PRIMARY KEY,
        TotalAppointments int        NOT NULL CONSTRAINT DF_BookingOverview_Total DEFAULT 0,
        TotalRevenue      money      NOT NULL CONSTRAINT DF_BookingOverview_Revenue DEFAULT 0,
        LastUpdatedAt     datetime2  NOT NULL CONSTRAINT DF_BookingOverview_Updated DEFAULT SYSUTCDATETIME(),
        RowVer            rowversion NOT NULL
    );
END;
