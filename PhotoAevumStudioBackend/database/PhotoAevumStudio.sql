SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

/* =====================================================================
   1. SEQUENCE GENERATORS (Atomic Custom IDs)
   ===================================================================== */
CREATE SEQUENCE dbo.Seq_CustomerID AS INT START WITH 1 INCREMENT BY 1;
CREATE SEQUENCE dbo.Seq_EmployeeID AS INT START WITH 1 INCREMENT BY 1;
CREATE SEQUENCE dbo.Seq_AdminID AS INT START WITH 1 INCREMENT BY 1;
CREATE SEQUENCE dbo.EquipmentIdSequence AS INT START WITH 1 INCREMENT BY 1 MINVALUE 1 NO CYCLE CACHE 10;
CREATE SEQUENCE dbo.Seq_EquipmentType AS INT START WITH 1 INCREMENT BY 1;
CREATE SEQUENCE dbo.Seq_AdditionalServicesID AS INT START WITH 1 INCREMENT BY 1;
CREATE SEQUENCE dbo.Seq_ComboPackageID AS INT START WITH 1 INCREMENT BY 1;
CREATE SEQUENCE dbo.Seq_ServicesID AS INT START WITH 1 INCREMENT BY 1;
CREATE SEQUENCE dbo.Seq_ServiceCategoryID AS INT START WITH 1 INCREMENT BY 1;
GO

/* =====================================================================
   2. USER AND ACCESS MANAGEMENT TABLES
   ===================================================================== */

CREATE TABLE dbo.Admin (
    AdminID nvarchar(30) NOT NULL PRIMARY KEY,
    AdminRole nvarchar(15) NOT NULL,
    AdminFirstName nvarchar(255) NOT NULL,
    AdminLastName nvarchar(255) NOT NULL,
    AdminMiddleName nvarchar(255),
    Email nvarchar(255) NULL,
    AdminPassword nvarchar(255) NOT NULL,
    isFirstLogin bit NOT NULL CONSTRAINT DF_Admin_isFirstLogin DEFAULT 1,
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_Admin_CreatedAt DEFAULT SYSUTCDATETIME()
);

CREATE TABLE dbo.SystemAdmin (
    SystemAdminID nvarchar(30) NOT NULL PRIMARY KEY,
    FirstName nvarchar(255) NOT NULL,
    LastName nvarchar(255) NOT NULL,
    MiddleName nvarchar(255) NULL,
    PasswordHash nvarchar(255) NOT NULL,
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_SystemAdmin_CreatedAt DEFAULT SYSUTCDATETIME()
);

CREATE TABLE dbo.Employees (
    EmployeeID nvarchar(30) NOT NULL PRIMARY KEY, 
    EmployeeFirstName nvarchar(255) NOT NULL,
    EmployeeLastName nvarchar(255) NOT NULL,
    EmployeeMiddleName nvarchar(255),
    EmployeeEmail nvarchar(255),
    EmployeePassword nvarchar(255),
    EmployeeStatus nvarchar(20) NOT NULL CONSTRAINT DF_Employee_Status DEFAULT 'Active',
    isFirstLogin bit NOT NULL CONSTRAINT DF_Employee_isFirstLogin DEFAULT 1,
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_Employee_CreatedAt DEFAULT SYSUTCDATETIME(),
    DaysOff nvarchar(50) NOT NULL CONSTRAINT DF_Employees_DaysOff DEFAULT 'Sunday',
    Id int IDENTITY(1,1) NOT NULL,
    IdentityUserId nvarchar(450) NULL,
    Status nvarchar(50) NOT NULL CONSTRAINT DF_Employees_EF_Status DEFAULT 'Active',
    Specializations nvarchar(max) NOT NULL CONSTRAINT DF_Employees_EF_Specializations DEFAULT '[]',
    CONSTRAINT CK_Employee_Status CHECK (EmployeeStatus IN ('Active','Inactive'))
);

CREATE TABLE dbo.Customer (
    CustomerID nvarchar(30) NOT NULL PRIMARY KEY, 
    CustomerFirstName nvarchar(255) NOT NULL,
    CustomerLastName nvarchar(255) NOT NULL,
    CustomerMiddleName nvarchar(255),
    CustomerEmail nvarchar(255) NOT NULL UNIQUE, 
    CustomerPhoneNumber nvarchar(30),
    CustomerPassword nvarchar(255) NOT NULL,
    CustomerStatus nvarchar(20) NOT NULL CONSTRAINT DF_Customer_Status DEFAULT 'Active',
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_Customer_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_Customer_Status CHECK (CustomerStatus IN ('Active','Inactive'))
);

CREATE UNIQUE INDEX UX_Admin_Email ON dbo.Admin(Email) WHERE Email IS NOT NULL;
CREATE UNIQUE INDEX UX_Employee_Email ON dbo.Employees(EmployeeEmail) WHERE EmployeeEmail IS NOT NULL;

/* =====================================================================
   3. CATALOG AND INVENTORY TABLES
   ===================================================================== */

CREATE TABLE dbo.EmployeeSpecialization (
    SpecializationID nvarchar(30) NOT NULL PRIMARY KEY,
    SpecializationName nvarchar(255) NOT NULL,
    SpecializationDescription nvarchar(1000) NULL,
    Specialization nvarchar(255) NOT NULL,
    Description nvarchar(1000) NULL,
    ColorTag nvarchar(50) NOT NULL CONSTRAINT DF_EmployeeSpecialization_ColorTag DEFAULT 'Blue',
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_EmployeeSpecialization_CreatedAt DEFAULT SYSUTCDATETIME()
);

CREATE TABLE dbo.EquipmentType (
    EquipmentTypeID nvarchar(30) NOT NULL PRIMARY KEY,
    EquipmentTypeName nvarchar(255)
);

CREATE TABLE dbo.ComboPackage (
    ComboPackageID nvarchar(30) NOT NULL PRIMARY KEY,
    ComboPackageName nvarchar(255) NOT NULL,
    ComboPackageDescription nvarchar(1000) NOT NULL,
    ComboPackagePrice decimal(18,2) NOT NULL CONSTRAINT DF_ComboPackage_Price DEFAULT 0.00,
    ComboPackageImage varbinary(max) NULL,
    ComboPackageImageMimeType nvarchar(100) NULL CONSTRAINT DF_ComboPackage_MimeType DEFAULT 'image/jpeg'
);

CREATE TABLE dbo.AdditionalServices (
    AdditionalServicesID nvarchar(30) NOT NULL PRIMARY KEY,
    AdditionalServiceName nvarchar(255),
    AdditionalServicesDescription nvarchar(1000),
    ServicePrice decimal(18,2) NOT NULL CONSTRAINT DF_AdditionalServices_Price DEFAULT 0.00,
    CategoryName nvarchar(100) NULL,
    AdditionalServiceImage varbinary(max) NULL,
    AdditionalServiceImageMimeType nvarchar(100) NULL CONSTRAINT DF_AdditionalServices_MimeType DEFAULT 'image/jpeg',
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_AdditionalServices_CreatedAt DEFAULT SYSUTCDATETIME()
);

CREATE TABLE dbo.Services (
    ServicesID nvarchar(30) NOT NULL PRIMARY KEY,
    ServiceName nvarchar(255) NOT NULL,
    ServicesDescription nvarchar(1000) NOT NULL,
    ServicePrice decimal(18,2) NOT NULL CONSTRAINT DF_Services_Price DEFAULT 0.00,
    ServiceImage varbinary(max) NULL,
    ServiceImageMimeType nvarchar(100) NULL CONSTRAINT DF_Services_MimeType DEFAULT 'image/jpeg',
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_Services_CreatedAt DEFAULT SYSUTCDATETIME()
);

CREATE TABLE dbo.ServiceCategory ( 
    ServiceCategoryID nvarchar(30) NOT NULL Primary Key,
    ServiceCategoryName nvarchar(255) NOT NULL,
    ServiceCategoryDescription nvarchar(1000) NOT NULL
);

CREATE TABLE dbo.ServiceCategoryJoins (
    ServiceCategoryJoinID nvarchar(30) NOT NULL PRIMARY KEY,
    ServiceCategoryID nvarchar(30) NOT NULL,
    ServicesID nvarchar(30) NULL,
    AdditionalServicesID nvarchar(30) NULL,
    ComboPackageID nvarchar(30) NULL,
    CONSTRAINT CK_ServiceCategoryJoins_ExactlyOne CHECK (
        (CASE WHEN ServicesID IS NOT NULL THEN 1 ELSE 0 END) +
        (CASE WHEN AdditionalServicesID IS NOT NULL THEN 1 ELSE 0 END) +
        (CASE WHEN ComboPackageID IS NOT NULL THEN 1 ELSE 0 END) = 1
    )
);

CREATE TABLE dbo.Equipment (
    EquipmentID nvarchar(30) NOT NULL PRIMARY KEY,
    EquipmentName nvarchar(100) NOT NULL,
    EquipmentDescription nvarchar(1000) NULL,
    EquipmentTypeID nvarchar(30) NOT NULL,
    EquipmentStatus nvarchar(20) NOT NULL CONSTRAINT DF_Equipment_Status DEFAULT 'Available',
    EquipmentImage varbinary(max) NULL, 
    ImagePath nvarchar(500) NULL,
    SerialNo nvarchar(30),
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_Equipment_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_Equipment_Status CHECK (EquipmentStatus IN ('Available','Maintenance','Retired'))
);
CREATE UNIQUE INDEX UX_Equipment_SerialNo ON dbo.Equipment(SerialNo) WHERE SerialNo IS NOT NULL;

/* =====================================================================
   4. BOOKINGS AND APPOINTMENTS TABLES
   ===================================================================== */

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
    ServicesID nvarchar(30) NULL
);

/* =====================================================================
   5. AUDIT & HISTORY LOGGING TABLE
   ===================================================================== */

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
    ChangedByType       nvarchar(20)  NOT NULL,
    ChangedByCustomerID nvarchar(30)  NULL,
    ChangedByEmployeeID nvarchar(30)  NULL,
    ChangedByAdminID    nvarchar(30)  NULL,
    ChangedBy AS COALESCE(ChangedByCustomerID, ChangedByEmployeeID, ChangedByAdminID) PERSISTED,
    LoggedAt            datetime2     NOT NULL CONSTRAINT DF_HistoryLog_LoggedAt DEFAULT SYSUTCDATETIME(),

    CONSTRAINT CK_HistoryLog_ActionType
        CHECK (ActionType IN ('INSERT','CREATE','UPDATE','DELETE','NEGOTIATE','STATUS_CHANGE','LOGIN','LOGOUT','PASSWORD_CHANGE','PASSWORD_RESET','IMAGE_UPLOAD')),
    CONSTRAINT CK_HistoryLog_ChangedByType
        CHECK (ChangedByType IN ('Customer','Employee','Admin','SystemAdmin','System')),
    CONSTRAINT CK_HistoryLog_Actor CHECK (
        (ChangedByType = 'Customer' AND (ChangedByCustomerID IS NULL OR (ChangedByEmployeeID IS NULL AND ChangedByAdminID IS NULL)))
     OR (ChangedByType = 'Employee' AND (ChangedByEmployeeID IS NULL OR (ChangedByCustomerID IS NULL AND ChangedByAdminID IS NULL)))
     OR (ChangedByType IN ('Admin', 'SystemAdmin') AND (ChangedByAdminID IS NULL OR (ChangedByCustomerID IS NULL AND ChangedByEmployeeID IS NULL)))
     OR (ChangedByType = 'System'   AND ChangedByCustomerID IS NULL AND ChangedByEmployeeID IS NULL AND ChangedByAdminID IS NULL))
);
GO

/* =====================================================================
   6. FOREIGN KEY RELATIONSHIPS
   ===================================================================== */
ALTER TABLE dbo.Equipment ADD CONSTRAINT FK_Equipment_EquipmentType FOREIGN KEY (EquipmentTypeID) REFERENCES dbo.EquipmentType(EquipmentTypeID);
ALTER TABLE dbo.BookingNegotiation ADD CONSTRAINT FK_BookingNegotiation_Employee FOREIGN KEY (EmployeeID) REFERENCES dbo.Employees(EmployeeID);
ALTER TABLE dbo.BookingNegotiation ADD CONSTRAINT FK_BookingNegotiation_Customer FOREIGN KEY (CustomerID) REFERENCES dbo.Customer(CustomerID);
ALTER TABLE dbo.BookingNegotiation ADD CONSTRAINT FK_BookingNegotiation_EventAppointment FOREIGN KEY (EventAppointmentID) REFERENCES dbo.EventAppointment(EventAppointmentID);
ALTER TABLE dbo.EventEmployee ADD CONSTRAINT FK_EventEmployee_Employee FOREIGN KEY (EmployeeID) REFERENCES dbo.Employees(EmployeeID);
ALTER TABLE dbo.EventEquipment ADD CONSTRAINT FK_EventEquipment_Equipment FOREIGN KEY (EquipmentID) REFERENCES dbo.Equipment(EquipmentID);
ALTER TABLE dbo.EventAppointment ADD CONSTRAINT FK_EventAppointment_Customer FOREIGN KEY (CustomerID) REFERENCES dbo.Customer(CustomerID);
ALTER TABLE dbo.EventLineItem ADD CONSTRAINT FK_EventLineItem_AdditionalServices FOREIGN KEY (AdditionalServicesID) REFERENCES dbo.AdditionalServices(AdditionalServicesID);
ALTER TABLE dbo.EventLineItem ADD CONSTRAINT FK_EventLineItem_Services FOREIGN KEY (ServicesID) REFERENCES dbo.Services(ServicesID);
ALTER TABLE dbo.EmployeeJointSpecialization ADD CONSTRAINT FK_EmpJointSpec_Employee FOREIGN KEY (EmployeeID) REFERENCES dbo.Employees(EmployeeID) ON DELETE CASCADE;
ALTER TABLE dbo.EmployeeJointSpecialization ADD CONSTRAINT FK_EmpJointSpec_Specialization FOREIGN KEY (SpecializationID) REFERENCES dbo.EmployeeSpecialization(SpecializationID) ON DELETE CASCADE;
ALTER TABLE dbo.ComboPackageJoin ADD CONSTRAINT FK_ComboPackageJoin_Services FOREIGN KEY (ServicesID) REFERENCES dbo.Services(ServicesID);
ALTER TABLE dbo.ComboPackageJoin ADD CONSTRAINT FK_ComboPackageJoin_AdditionalServices FOREIGN KEY (AdditionalServicesID) REFERENCES dbo.AdditionalServices(AdditionalServicesID);
ALTER TABLE dbo.ComboPackageJoin ADD CONSTRAINT FK_ComboPackageJoin_ComboPackage FOREIGN KEY (ComboPackageID) REFERENCES dbo.ComboPackage(ComboPackageID) ON DELETE CASCADE;
ALTER TABLE dbo.EventLineItem ADD CONSTRAINT FK_EventLineItem_EventAppointment FOREIGN KEY (EventAppointmentID) REFERENCES dbo.EventAppointment(EventAppointmentID) ON DELETE CASCADE;
ALTER TABLE dbo.EventLineItem ADD CONSTRAINT FK_EventLineItem_ComboPackage FOREIGN KEY (ComboPackageID) REFERENCES dbo.ComboPackage(ComboPackageID);
ALTER TABLE dbo.EventEmployee ADD CONSTRAINT FK_EventEmployee_EventAppointment FOREIGN KEY (EventAppointmentID) REFERENCES dbo.EventAppointment(EventAppointmentID) ON DELETE CASCADE;
ALTER TABLE dbo.EventEquipment ADD CONSTRAINT FK_EventEquipment_EventAppointment FOREIGN KEY (EventAppointmentID) REFERENCES dbo.EventAppointment(EventAppointmentID) ON DELETE CASCADE;
ALTER TABLE dbo.ServiceCategoryJoins ADD CONSTRAINT FK_ServiceCategoryJoins_ServiceCategory FOREIGN KEY (ServiceCategoryID) REFERENCES dbo.ServiceCategory(ServiceCategoryID) ON DELETE CASCADE;
ALTER TABLE dbo.ServiceCategoryJoins ADD CONSTRAINT FK_ServiceCategoryJoins_Services FOREIGN KEY (ServicesID) REFERENCES dbo.Services(ServicesID) ON DELETE CASCADE;
ALTER TABLE dbo.ServiceCategoryJoins ADD CONSTRAINT FK_ServiceCategoryJoins_AdditionalServices FOREIGN KEY (AdditionalServicesID) REFERENCES dbo.AdditionalServices(AdditionalServicesID) ON DELETE CASCADE;
ALTER TABLE dbo.ServiceCategoryJoins ADD CONSTRAINT FK_ServiceCategoryJoins_ComboPackage FOREIGN KEY (ComboPackageID) REFERENCES dbo.ComboPackage(ComboPackageID) ON DELETE CASCADE;
ALTER TABLE dbo.HistoryLog ADD CONSTRAINT FK_HistoryLog_EventAppointment FOREIGN KEY (EventAppointmentID) REFERENCES dbo.EventAppointment(EventAppointmentID) ON DELETE SET NULL;
ALTER TABLE dbo.HistoryLog ADD CONSTRAINT FK_HistoryLog_BookingNegotiation FOREIGN KEY (NegotiationID) REFERENCES dbo.BookingNegotiation(NegotiationID) ON DELETE SET NULL;
ALTER TABLE dbo.HistoryLog ADD CONSTRAINT FK_HistoryLog_Customer FOREIGN KEY (CustomerID) REFERENCES dbo.Customer(CustomerID);
ALTER TABLE dbo.HistoryLog ADD CONSTRAINT FK_HistoryLog_Employee FOREIGN KEY (EmployeeID) REFERENCES dbo.Employees(EmployeeID);
ALTER TABLE dbo.HistoryLog ADD CONSTRAINT FK_HistoryLog_ChangedByCustomer FOREIGN KEY (ChangedByCustomerID) REFERENCES dbo.Customer(CustomerID) ON DELETE SET NULL;
ALTER TABLE dbo.HistoryLog ADD CONSTRAINT FK_HistoryLog_ChangedByEmployee FOREIGN KEY (ChangedByEmployeeID) REFERENCES dbo.Employees(EmployeeID) ON DELETE SET NULL;
ALTER TABLE dbo.HistoryLog ADD CONSTRAINT FK_HistoryLog_ChangedByAdmin FOREIGN KEY (ChangedByAdminID) REFERENCES dbo.Admin(AdminID) ON DELETE SET NULL;
GO

/* =====================================================================
   7. INDEXES FOR COMMON AUDIT, CATALOG & BOOKING LOOKUPS
   ===================================================================== */
CREATE INDEX IX_HistoryLog_EventAppointment ON dbo.HistoryLog (EventAppointmentID, LoggedAt DESC);
CREATE INDEX IX_HistoryLog_Entity           ON dbo.HistoryLog (EntityName, EntityID, LoggedAt DESC);
CREATE INDEX IX_HistoryLog_ChangedBy        ON dbo.HistoryLog (ChangedBy, LoggedAt DESC);
CREATE INDEX IX_HistoryLog_LoggedAt         ON dbo.HistoryLog (LoggedAt DESC);
CREATE INDEX IX_EventAppointment_Customer   ON dbo.EventAppointment (CustomerID, EventDate DESC);
CREATE INDEX IX_EventAppointment_Schedule   ON dbo.EventAppointment (EventDate, EventTime);
CREATE INDEX IX_EventLineItem_Event         ON dbo.EventLineItem (EventAppointmentID);
CREATE INDEX IX_ServiceCategoryJoins_Cat    ON dbo.ServiceCategoryJoins (ServiceCategoryID);
CREATE INDEX IX_ComboPackageJoin_Package    ON dbo.ComboPackageJoin (ComboPackageID);
CREATE INDEX IX_Equipment_EquipmentType     ON dbo.Equipment (EquipmentTypeID);
GO