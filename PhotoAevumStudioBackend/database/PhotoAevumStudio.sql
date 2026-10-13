SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

CREATE TABLE dbo.Admin (
    AdminID nvarchar(30) NOT NULL PRIMARY KEY,
    AdminRole nvarchar(15) NOT NULL,
    AdminFirstName nvarchar(255) NOT NULL,
    AdminLastName nvarchar(255) NOT NULL,
    AdminMiddleName nvarchar(255),
    AdminPassword nvarchar(255) NOT NULL
);

CREATE TABLE dbo.Employee (
    EmployeeID nvarchar(30) NOT NULL PRIMARY KEY, 
    EmployeeFirstName nvarchar(255) NOT NULL,
    EmployeeLastName nvarchar(255) NOT NULL,
    EmployeeMiddleName nvarchar(255),
    EmployeeEmail nvarchar(255),
    EmployeePassword nvarchar(255),
    EmployeeStatus nvarchar(20) NOT NULL CONSTRAINT DF_Employee_Status DEFAULT 'Active',
    CONSTRAINT CK_Employee_Status CHECK (EmployeeStatus IN ('Active','Inactive'))
);

CREATE TABLE dbo.Customer (
    CustomerID nvarchar(30) NOT NULL PRIMARY KEY, 
    CustomerFirstName nvarchar(255) NOT NULL,
    CustomerLastName nvarchar(255) NOT NULL,
    CustomerMiddleName nvarchar(255),
    CustomerEmail nvarchar(255) NOT NULL UNIQUE, 
    CustomerPhoneNumber nvarchar(30),
    CustomerPassword nvarchar(255) NOT NULL
);



CREATE TABLE dbo.EmployeeSpecialization (
    SpecializationID nvarchar(30) NOT NULL PRIMARY KEY,
    SpecializationName nvarchar(255) NOT NULL
);

CREATE TABLE dbo.EquipmentType (
    EquipmentTypeID nvarchar(30) NOT NULL PRIMARY KEY,
    EquipmentTypeName nvarchar(255)
);

CREATE TABLE dbo.ComboPackage (
    ComboPackageID nvarchar(30) NOT NULL PRIMARY KEY,
    ComboPackageName nvarchar(255) NOT NULL,
    ComboPackageDescription nvarchar(255) NOT NULL
);

CREATE TABLE dbo.AdditionalServices (
    AdditionalServicesID nvarchar(30) NOT NULL PRIMARY KEY,
    AdditionalServiceName nvarchar(255),
    AdditionalServicesDescription nvarchar(255)
);

CREATE TABLE dbo.Services (
    ServicesID nvarchar(30) NOT NULL PRIMARY KEY,
    ServiceName nvarchar(255) NOT NULL,
    ServicesDescription nvarchar(255) NOT NULL
);

CREATE UNIQUE INDEX UX_Employee_Email ON dbo.Employee(EmployeeEmail) WHERE EmployeeEmail IS NOT NULL;

CREATE TABLE dbo.Equipment (
    EquipmentID nvarchar(30) NOT NULL PRIMARY KEY,
    EquipmentName nvarchar(100) NOT NULL,
    EquipmentTypeID nvarchar(30) NOT NULL,
    EquipmentStatus nvarchar(20) NOT NULL CONSTRAINT DF_Equipment_Status DEFAULT 'Available',
    EquipmentImage varbinary(max) NULL, 
    SerialNo nvarchar(30),
    CONSTRAINT CK_Equipment_Status CHECK (EquipmentStatus IN ('Available','Maintenance','Retired'))
);
CREATE UNIQUE INDEX UX_Equipment_SerialNo ON dbo.Equipment(SerialNo) WHERE SerialNo IS NOT NULL;

CREATE TABLE dbo.EventAppointment (
    EventAppointmentID nvarchar(30) NOT NULL PRIMARY KEY,
    EventName nvarchar(255) NOT NULL,
    EventDescription nvarchar(255) NOT NULL,
    CustomerID nvarchar(30) NOT NULL,
    EventDate date NOT NULL,
    EventTime time NOT NULL,
    EventStatus nvarchar(30) NOT NULL CONSTRAINT DF_EventAppointment_Status DEFAULT 'Pending',
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_EventAppointment_Created DEFAULT SYSUTCDATETIME(),
    RowVer rowversion, 
    CONSTRAINT CK_EventAppointment_Status CHECK (EventStatus IN ('Pending','Confirmed','Shot','Editing','Delivered','Cancelled'))
);

CREATE TABLE dbo.BookingNegotiation (
    NegotiationID nvarchar(30) NOT NULL PRIMARY KEY,
    EventAppointmentID nvarchar(30) NOT NULL,
    CustomerID nvarchar(30) NOT NULL,
    EmployeeID nvarchar(30) NOT NULL, 
    NegotiationStatus nvarchar(50)
);

CREATE TABLE dbo.EventEmployee (
    EventEmployeeID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    EventAppointmentID nvarchar(30) NOT NULL,
    EmployeeID nvarchar(30) NOT NULL,
    CONSTRAINT UQ_EventEmployee_Assign UNIQUE (EventAppointmentID, EmployeeID)
);

CREATE TABLE dbo.EventEquipment (
    EventEquipmentID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    EventAppointmentID nvarchar(30) NOT NULL,
    EquipmentID nvarchar(30) NOT NULL,
    CONSTRAINT UQ_EventEquipment_Assign UNIQUE (EventAppointmentID, EquipmentID)
);

CREATE TABLE dbo.EventLineItem (
    EventLineItemID nvarchar(30) NOT NULL PRIMARY KEY,
    EventAppointmentID nvarchar(30) NOT NULL,
    ServicesID nvarchar(30),
    AdditionalServicesID nvarchar(30),
    ComboPackageID nvarchar(30),
    CONSTRAINT CK_EventLineItem_ExactlyOne CHECK (
        (CASE WHEN ServicesID IS NOT NULL THEN 1 ELSE 0 END) +
        (CASE WHEN AdditionalServicesID IS NOT NULL THEN 1 ELSE 0 END) +
        (CASE WHEN ComboPackageID IS NOT NULL THEN 1 ELSE 0 END) = 1)
);

CREATE TABLE dbo.EmployeeJointSpecialization (
    EmployeeJointSpecializationID nvarchar(30) NOT NULL PRIMARY KEY,
    SpecializationID nvarchar(30) NOT NULL, 
    EmployeeID nvarchar(30) NOT NULL,       
    CONSTRAINT UQ_EmpJointSpec UNIQUE (EmployeeID, SpecializationID)
);

CREATE TABLE dbo.ComboPackageJoin (
    ComboPackageJoinID nvarchar(30) NOT NULL PRIMARY KEY,
    ComboPackageID nvarchar(30) NOT NULL,
    AdditionalServicesID nvarchar(30),
    ServicesID nvarchar(30) NOT NULL
);

CREATE TABLE dbo.HistoryLog (
    HistoryLogID        bigint        IDENTITY(1,1) NOT NULL CONSTRAINT PK_HistoryLog PRIMARY KEY,
    ActionType          nvarchar(20)  NOT NULL,
    EntityName          nvarchar(128) NOT NULL,
    EntityID            nvarchar(30)  NULL,
    ActionDetails       nvarchar(max) NULL,
    EventAppointmentID  nvarchar(30)  NULL,
    NegotiationID       nvarchar(30)  NULL,
    CustomerID          nvarchar(30)  NULL,
    EmployeeID          nvarchar(30)  NULL,
    ChangedByType       nvarchar(10)  NOT NULL,
    ChangedByCustomerID nvarchar(30)  NULL,
    ChangedByEmployeeID nvarchar(30)  NULL,
    ChangedByAdminID    nvarchar(30)  NULL,
    ChangedBy AS COALESCE(ChangedByCustomerID, ChangedByEmployeeID, ChangedByAdminID) PERSISTED,
    LoggedAt            datetime2     NOT NULL CONSTRAINT DF_HistoryLog_LoggedAt DEFAULT SYSUTCDATETIME(),

    CONSTRAINT CK_HistoryLog_ActionType
        CHECK (ActionType IN ('INSERT','UPDATE','DELETE','NEGOTIATE','STATUS_CHANGE')),
    CONSTRAINT CK_HistoryLog_ChangedByType
        CHECK (ChangedByType IN ('Customer','Employee','Admin','System')),
    CONSTRAINT CK_HistoryLog_Actor CHECK (
        (ChangedByType = 'Customer' AND ChangedByCustomerID IS NOT NULL AND ChangedByEmployeeID IS NULL     AND ChangedByAdminID IS NULL)
     OR (ChangedByType = 'Employee' AND ChangedByEmployeeID IS NOT NULL AND ChangedByCustomerID IS NULL     AND ChangedByAdminID IS NULL)
     OR (ChangedByType = 'Admin'    AND ChangedByAdminID    IS NOT NULL AND ChangedByCustomerID IS NULL     AND ChangedByEmployeeID IS NULL)
     OR (ChangedByType = 'System'   AND ChangedByCustomerID IS NULL     AND ChangedByEmployeeID IS NULL     AND ChangedByAdminID IS NULL)),
    CONSTRAINT CK_HistoryLog_AdminScope CHECK (
        ChangedByType <> 'Admin'
        OR EntityName IN (N'Equipment', N'EquipmentType',
                          N'Employee', N'EmployeeSpecialization', N'EmployeeJointSpecialization',
                          N'Services', N'AdditionalServices', N'ComboPackage', N'ComboPackageJoin'))
);
GO

/* =====================================================================
   3. FOREIGN KEY RELATIONSHIPS
   ===================================================================== */
ALTER TABLE dbo.Equipment ADD CONSTRAINT FK_Equipment_EquipmentType FOREIGN KEY (EquipmentTypeID) REFERENCES dbo.EquipmentType(EquipmentTypeID);
ALTER TABLE dbo.BookingNegotiation ADD CONSTRAINT FK_BookingNegotiation_Employee FOREIGN KEY (EmployeeID) REFERENCES dbo.Employee(EmployeeID);
ALTER TABLE dbo.BookingNegotiation ADD CONSTRAINT FK_BookingNegotiation_Customer FOREIGN KEY (CustomerID) REFERENCES dbo.Customer(CustomerID);
ALTER TABLE dbo.BookingNegotiation ADD CONSTRAINT FK_BookingNegotiation_EventAppointment FOREIGN KEY (EventAppointmentID) REFERENCES dbo.EventAppointment(EventAppointmentID);
ALTER TABLE dbo.EventEmployee ADD CONSTRAINT FK_EventEmployee_Employee FOREIGN KEY (EmployeeID) REFERENCES dbo.Employee(EmployeeID);
ALTER TABLE dbo.EventEquipment ADD CONSTRAINT FK_EventEquipment_Equipment FOREIGN KEY (EquipmentID) REFERENCES dbo.Equipment(EquipmentID);
ALTER TABLE dbo.EventAppointment ADD CONSTRAINT FK_EventAppointment_Customer FOREIGN KEY (CustomerID) REFERENCES dbo.Customer(CustomerID);
ALTER TABLE dbo.EventLineItem ADD CONSTRAINT FK_EventLineItem_AdditionalServices FOREIGN KEY (AdditionalServicesID) REFERENCES dbo.AdditionalServices(AdditionalServicesID);
ALTER TABLE dbo.EventLineItem ADD CONSTRAINT FK_EventLineItem_Services FOREIGN KEY (ServicesID) REFERENCES dbo.Services(ServicesID);
ALTER TABLE dbo.EmployeeJointSpecialization ADD CONSTRAINT FK_EmpJointSpec_Employee FOREIGN KEY (EmployeeID) REFERENCES dbo.Employee(EmployeeID);
ALTER TABLE dbo.EmployeeJointSpecialization ADD CONSTRAINT FK_EmpJointSpec_Specialization FOREIGN KEY (SpecializationID) REFERENCES dbo.EmployeeSpecialization(SpecializationID);
ALTER TABLE dbo.ComboPackageJoin ADD CONSTRAINT FK_ComboPackageJoin_Services FOREIGN KEY (ServicesID) REFERENCES dbo.Services(ServicesID);
ALTER TABLE dbo.ComboPackageJoin ADD CONSTRAINT FK_ComboPackageJoin_AdditionalServices FOREIGN KEY (AdditionalServicesID) REFERENCES dbo.AdditionalServices(AdditionalServicesID);
ALTER TABLE dbo.ComboPackageJoin ADD CONSTRAINT FK_ComboPackageJoin_ComboPackage FOREIGN KEY (ComboPackageID) REFERENCES dbo.ComboPackage(ComboPackageID);
ALTER TABLE dbo.EventLineItem ADD CONSTRAINT FK_EventLineItem_EventAppointment FOREIGN KEY (EventAppointmentID) REFERENCES dbo.EventAppointment(EventAppointmentID) ON DELETE CASCADE;
ALTER TABLE dbo.EventLineItem ADD CONSTRAINT FK_EventLineItem_ComboPackage FOREIGN KEY (ComboPackageID) REFERENCES dbo.ComboPackage(ComboPackageID);
ALTER TABLE dbo.EventEmployee ADD CONSTRAINT FK_EventEmployee_EventAppointment FOREIGN KEY (EventAppointmentID) REFERENCES dbo.EventAppointment(EventAppointmentID) ON DELETE CASCADE;
ALTER TABLE dbo.EventEquipment ADD CONSTRAINT FK_EventEquipment_EventAppointment FOREIGN KEY (EventAppointmentID) REFERENCES dbo.EventAppointment(EventAppointmentID) ON DELETE CASCADE;

ALTER TABLE dbo.HistoryLog ADD CONSTRAINT FK_HistoryLog_EventAppointment FOREIGN KEY (EventAppointmentID) REFERENCES dbo.EventAppointment(EventAppointmentID);
ALTER TABLE dbo.HistoryLog ADD CONSTRAINT FK_HistoryLog_BookingNegotiation FOREIGN KEY (NegotiationID) REFERENCES dbo.BookingNegotiation(NegotiationID);
ALTER TABLE dbo.HistoryLog ADD CONSTRAINT FK_HistoryLog_Customer FOREIGN KEY (CustomerID) REFERENCES dbo.Customer(CustomerID);
ALTER TABLE dbo.HistoryLog ADD CONSTRAINT FK_HistoryLog_Employee FOREIGN KEY (EmployeeID) REFERENCES dbo.Employee(EmployeeID);
ALTER TABLE dbo.HistoryLog ADD CONSTRAINT FK_HistoryLog_ChangedByCustomer FOREIGN KEY (ChangedByCustomerID) REFERENCES dbo.Customer(CustomerID);
ALTER TABLE dbo.HistoryLog ADD CONSTRAINT FK_HistoryLog_ChangedByEmployee FOREIGN KEY (ChangedByEmployeeID) REFERENCES dbo.Employee(EmployeeID);
ALTER TABLE dbo.HistoryLog ADD CONSTRAINT FK_HistoryLog_ChangedByAdmin FOREIGN KEY (ChangedByAdminID) REFERENCES dbo.Admin(AdminID);
GO

/* =====================================================================
   4. INDEXES FOR COMMON AUDIT LOOKUPS
   ===================================================================== */
CREATE INDEX IX_HistoryLog_EventAppointment ON dbo.HistoryLog (EventAppointmentID, LoggedAt DESC);
CREATE INDEX IX_HistoryLog_Entity           ON dbo.HistoryLog (EntityName, EntityID, LoggedAt DESC);
CREATE INDEX IX_HistoryLog_ChangedBy        ON dbo.HistoryLog (ChangedBy, LoggedAt DESC);
CREATE INDEX IX_HistoryLog_LoggedAt         ON dbo.HistoryLog (LoggedAt DESC);
GO