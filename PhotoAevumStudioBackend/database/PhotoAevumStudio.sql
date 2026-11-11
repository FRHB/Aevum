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
CREATE SEQUENCE dbo.Seq_ServiceRateID AS INT START WITH 1 INCREMENT BY 1;
CREATE SEQUENCE dbo.Seq_RateTypeID AS INT START WITH 5 INCREMENT BY 1;
CREATE SEQUENCE dbo.Seq_NegotiationID AS INT START WITH 1 INCREMENT BY 1;
CREATE SEQUENCE dbo.Seq_EventAppointmentID AS INT START WITH 1 INCREMENT BY 1;
CREATE SEQUENCE dbo.Seq_PaymentTransactionID AS INT START WITH 1 INCREMENT BY 1;
CREATE SEQUENCE dbo.Seq_PortfolioID AS INT START WITH 1 INCREMENT BY 1;
CREATE SEQUENCE dbo.Seq_PortfolioCategoryID AS INT START WITH 1 INCREMENT BY 1;
CREATE SEQUENCE dbo.Seq_DepartmentID AS INT START WITH 8 INCREMENT BY 1;
CREATE SEQUENCE dbo.Seq_HomeHighlightID AS INT START WITH 1 INCREMENT BY 1;
GO

/* =====================================================================
   2. USER AND ACCESS MANAGEMENT TABLES
   ===================================================================== */

CREATE TABLE dbo.Departments (
    DepartmentID nvarchar(30) NOT NULL PRIMARY KEY,
    DepartmentName nvarchar(100) NOT NULL UNIQUE,
    DepartmentDescription nvarchar(500) NULL,
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_Departments_CreatedAt DEFAULT SYSUTCDATETIME()
);

IF OBJECT_ID(N'dbo.Departments', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.Departments WHERE DepartmentID = 'DEP-0001')
        INSERT INTO dbo.Departments (DepartmentID, DepartmentName, DepartmentDescription) VALUES ('DEP-0001', 'Photography', 'Professional studio and on-location photography services');
    IF NOT EXISTS (SELECT 1 FROM dbo.Departments WHERE DepartmentID = 'DEP-0002')
        INSERT INTO dbo.Departments (DepartmentID, DepartmentName, DepartmentDescription) VALUES ('DEP-0002', 'Videography', 'Cinematic video recording, coverage, and production');
    IF NOT EXISTS (SELECT 1 FROM dbo.Departments WHERE DepartmentID = 'DEP-0003')
        INSERT INTO dbo.Departments (DepartmentID, DepartmentName, DepartmentDescription) VALUES ('DEP-0003', 'Post-Processing & Editing', 'Retouching, color grading, and multimedia post-production');
    IF NOT EXISTS (SELECT 1 FROM dbo.Departments WHERE DepartmentID = 'DEP-0004')
        INSERT INTO dbo.Departments (DepartmentID, DepartmentName, DepartmentDescription) VALUES ('DEP-0004', 'Studio Staff & Operations', 'Studio logistics, client onboarding, and equipment handling');
    IF NOT EXISTS (SELECT 1 FROM dbo.Departments WHERE DepartmentID = 'DEP-0005')
        INSERT INTO dbo.Departments (DepartmentID, DepartmentName, DepartmentDescription) VALUES ('DEP-0005', 'Administration & Studio Management', 'Executive management, finances, scheduling, and staff supervision');
    IF NOT EXISTS (SELECT 1 FROM dbo.Departments WHERE DepartmentID = 'DEP-0006')
        INSERT INTO dbo.Departments (DepartmentID, DepartmentName, DepartmentDescription) VALUES ('DEP-0006', 'Production & Lighting', 'Set design, lighting engineering, and technical floor management');
    IF NOT EXISTS (SELECT 1 FROM dbo.Departments WHERE DepartmentID = 'DEP-0007')
        INSERT INTO dbo.Departments (DepartmentID, DepartmentName, DepartmentDescription) VALUES ('DEP-0007', 'Creative Direction', 'Art direction, concept styling, theme planning, and brand storytelling');
END;

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

CREATE TABLE dbo.EmployeeTimesheets (
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    TimesheetId nvarchar(50) NOT NULL UNIQUE,
    EmployeeStudioId nvarchar(30) NOT NULL,
    EmployeeName nvarchar(255) NOT NULL,
    WorkDate date NOT NULL,
    ClockIn datetime2 NOT NULL,
    ClockOut datetime2 NULL,
    TotalHours float NOT NULL CONSTRAINT DF_EmployeeTimesheets_TotalHours DEFAULT 0.0,
    Status nvarchar(50) NOT NULL CONSTRAINT DF_EmployeeTimesheets_Status DEFAULT 'Open',
    IsDayOffWork bit NOT NULL CONSTRAINT DF_EmployeeTimesheets_IsDayOff DEFAULT 0,
    Notes nvarchar(1000) NULL,
    AdjustedBy nvarchar(100) NULL,
    AdjustmentReason nvarchar(500) NULL,
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_EmployeeTimesheets_CreatedAt DEFAULT SYSUTCDATETIME()
);

CREATE TABLE dbo.Employees (
    EmployeeID nvarchar(30) NOT NULL PRIMARY KEY, 
    EmployeeFirstName nvarchar(255) NOT NULL,
    EmployeeLastName nvarchar(255) NOT NULL,
    EmployeeMiddleName nvarchar(255),
    EmployeeEmail nvarchar(255),
    EmployeePassword nvarchar(255),
    EmployeeStatus nvarchar(50) NOT NULL CONSTRAINT DF_Employee_Status DEFAULT 'Active',
    isFirstLogin bit NOT NULL CONSTRAINT DF_Employee_isFirstLogin DEFAULT 1,
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_Employee_CreatedAt DEFAULT SYSUTCDATETIME(),
    DaysOff nvarchar(50) NOT NULL CONSTRAINT DF_Employees_DaysOff DEFAULT 'Sunday',
    Id int IDENTITY(1,1) NOT NULL,
    IdentityUserId nvarchar(450) NULL,
    Status nvarchar(50) NOT NULL CONSTRAINT DF_Employees_EF_Status DEFAULT 'Active',
    Specializations nvarchar(max) NOT NULL CONSTRAINT DF_Employees_EF_Specializations DEFAULT '[]',
    Address nvarchar(500) NULL,
    BirthYear int NULL,
    DepartmentID nvarchar(30) NULL,
    CONSTRAINT CK_Employee_Status CHECK (EmployeeStatus IN ('Active','Inactive','Clock-In (Duty)','Clock-Out (Off Duty)','Archive'))
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
    DepartmentID nvarchar(30) NULL,
    DepartmentName nvarchar(100) NULL,
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
    ComboPackageImageMimeType nvarchar(100) NULL CONSTRAINT DF_ComboPackage_MimeType DEFAULT 'image/jpeg',
    ImageFileName nvarchar(500) NULL,
    ImageUrl nvarchar(500) NULL
);

CREATE TABLE dbo.AdditionalServices (
    AdditionalServicesID nvarchar(30) NOT NULL PRIMARY KEY,
    AdditionalServiceName nvarchar(255),
    AdditionalServicesDescription nvarchar(1000),
    ServicePrice decimal(18,2) NOT NULL CONSTRAINT DF_AdditionalServices_Price DEFAULT 0.00,
    CategoryName nvarchar(100) NULL,
    AdditionalServiceImage varbinary(max) NULL,
    AdditionalServiceImageMimeType nvarchar(100) NULL CONSTRAINT DF_AdditionalServices_MimeType DEFAULT 'image/jpeg',
    ImageFileName nvarchar(500) NULL,
    ImageUrl nvarchar(500) NULL,
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_AdditionalServices_CreatedAt DEFAULT SYSUTCDATETIME()
);

CREATE TABLE dbo.RateType (
    RateTypeID nvarchar(30) NOT NULL PRIMARY KEY,
    RateTypeName nvarchar(50) NOT NULL,
    RateTypeCode nvarchar(30) NOT NULL UNIQUE,
    RateDescription nvarchar(255) NULL,
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_RateType_CreatedAt DEFAULT SYSUTCDATETIME()
);

IF OBJECT_ID(N'dbo.RateType', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.RateType WHERE RateTypeID = 'RT-0001' OR RateTypeCode = 'per-hour')
        INSERT INTO dbo.RateType (RateTypeID, RateTypeName, RateTypeCode, RateDescription) VALUES ('RT-0001', 'Per Hour', 'per-hour', 'Billed on an hourly rate');
    IF NOT EXISTS (SELECT 1 FROM dbo.RateType WHERE RateTypeID = 'RT-0002' OR RateTypeCode = 'per-picture')
        INSERT INTO dbo.RateType (RateTypeID, RateTypeName, RateTypeCode, RateDescription) VALUES ('RT-0002', 'Per Picture', 'per-picture', 'Billed per edited picture delivered');
    IF NOT EXISTS (SELECT 1 FROM dbo.RateType WHERE RateTypeID = 'RT-0003' OR RateTypeCode = 'per-single-shot')
        INSERT INTO dbo.RateType (RateTypeID, RateTypeName, RateTypeCode, RateDescription) VALUES ('RT-0003', 'Per Single Shot', 'per-single-shot', 'Billed for an individual studio shot');
    IF NOT EXISTS (SELECT 1 FROM dbo.RateType WHERE RateTypeID = 'RT-0004' OR RateTypeCode = 'multi-shot')
        INSERT INTO dbo.RateType (RateTypeID, RateTypeName, RateTypeCode, RateDescription) VALUES ('RT-0004', 'Multi-Shot', 'multi-shot', 'Billed for a multi-shot sequence or burst bundle');
END;

CREATE TABLE dbo.Services (
    ServicesID nvarchar(30) NOT NULL PRIMARY KEY,
    ServiceName nvarchar(255) NOT NULL,
    ServicesDescription nvarchar(1000) NOT NULL,
    ServicePrice decimal(18,2) NOT NULL CONSTRAINT DF_Services_Price DEFAULT 0.00,
    RateType nvarchar(50) NOT NULL CONSTRAINT DF_Services_RateType DEFAULT 'Per Hour',
    ServiceImage varbinary(max) NULL,
    ServiceImageMimeType nvarchar(100) NULL CONSTRAINT DF_Services_MimeType DEFAULT 'image/jpeg',
    ImageFileName nvarchar(500) NULL,
    ImageUrl nvarchar(500) NULL,
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_Services_CreatedAt DEFAULT SYSUTCDATETIME()
);

CREATE TABLE dbo.ServiceRate (
    ServiceRateID nvarchar(30) NOT NULL PRIMARY KEY,
    ServicesID nvarchar(30) NOT NULL,
    RateTypeID nvarchar(30) NOT NULL,
    RateTypeName nvarchar(50) NOT NULL,
    Price decimal(18,2) NOT NULL CONSTRAINT DF_ServiceRate_Price DEFAULT 0.00,
    UnitsIncluded int NOT NULL CONSTRAINT DF_ServiceRate_Units DEFAULT 1,
    RateDescription nvarchar(255) NULL,
    IsDefault bit NOT NULL CONSTRAINT DF_ServiceRate_IsDefault DEFAULT 0,
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_ServiceRate_CreatedAt DEFAULT SYSUTCDATETIME()
);

CREATE TABLE dbo.ServiceCategory ( 
    ServiceCategoryID nvarchar(30) NOT NULL Primary Key,
    ServiceCategoryName nvarchar(255) NOT NULL,
    ServiceCategoryDescription nvarchar(1000) NOT NULL,
    RateTypeID nvarchar(30) NOT NULL CONSTRAINT DF_ServiceCategory_RateTypeID DEFAULT 'RT-0001',
    RateTypeName nvarchar(50) NOT NULL CONSTRAINT DF_ServiceCategory_RateTypeName DEFAULT 'Per Hour'
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
    EventName nvarchar(255) NOT NULL CONSTRAINT DF_EventAppointment_Name DEFAULT 'Photoshoot Event',
    EventDescription nvarchar(255) NULL,
    CustomerID nvarchar(30) NOT NULL,
    EmployeeID nvarchar(30) NULL,
    AssignedEmployeeName nvarchar(100) NULL,
    EventDate date NOT NULL,
    EventTime time NOT NULL,
    EventStatus nvarchar(30) NOT NULL CONSTRAINT DF_EventAppointment_Status DEFAULT 'Pending',
    TotalAmount decimal(18,2) NOT NULL CONSTRAINT DF_EventAppointment_Total DEFAULT 0.00,
    DownpaymentAmount decimal(18,2) NOT NULL CONSTRAINT DF_EventAppointment_Downpayment DEFAULT 0.00,
    FinalBalanceAmount decimal(18,2) NOT NULL CONSTRAINT DF_EventAppointment_Balance DEFAULT 0.00,
    DownpaymentStatus nvarchar(30) NOT NULL CONSTRAINT DF_EventAppointment_DownpaymentStatus DEFAULT 'Pending',
    DownpaymentPaidAt datetime2 NULL,
    HoldExpiresAt datetime2 NULL,
    LockExpiresAt datetime2 NULL,
    QrString nvarchar(1000) NULL,
    XenditInvoiceId nvarchar(100) NULL,
    XenditExternalId nvarchar(100) NULL,
    XenditInvoiceUrl nvarchar(500) NULL,
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_EventAppointment_Created DEFAULT SYSUTCDATETIME(),
    RowVer rowversion, 
    CONSTRAINT CK_EventAppointment_Status CHECK (EventStatus IN ('Pending','Awaiting_Downpayment','Pending Payment','Confirmed','Shot','Editing','Delivered','Cancelled','Expired'))
);

CREATE TABLE dbo.BookingNegotiation (
    NegotiationID nvarchar(30) NOT NULL PRIMARY KEY,
    EventAppointmentID nvarchar(30) NULL,
    CustomerID nvarchar(30) NOT NULL,
    EmployeeID nvarchar(30) NULL, 
    AssignedEmployeeName nvarchar(100) NULL,
    ConsultationDate date NULL,
    ConsultationTime time NULL,
    MeetingLink nvarchar(500) NULL,
    InquiryNotes nvarchar(1000) NULL,
    NegotiationStatus nvarchar(50) NOT NULL CONSTRAINT DF_BookingNegotiation_Status DEFAULT 'Pending_Call',
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_BookingNegotiation_Created DEFAULT SYSUTCDATETIME()
);

CREATE TABLE dbo.EventEmployee (
    EventEmployeeID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    EventAppointmentID nvarchar(30) NOT NULL,
    EmployeeID nvarchar(30) NULL,
    AssignedEmployeeName nvarchar(100) NULL,
    IsTeamLeader bit NOT NULL CONSTRAINT DF_EventEmployee_IsTeamLeader DEFAULT 0,
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

CREATE TABLE dbo.ArchiveIndex (
    ArchiveId     int           IDENTITY(1,1) NOT NULL CONSTRAINT PK_ArchiveIndex PRIMARY KEY,
    EntityName    nvarchar(128) NOT NULL,
    BucketName    nvarchar(100) NOT NULL CONSTRAINT DF_ArchiveIndex_Bucket DEFAULT 'archive',
    R2Key         nvarchar(500) NOT NULL,
    FileUri       nvarchar(1000) NOT NULL,
    RecordCount   int           NOT NULL CONSTRAINT DF_ArchiveIndex_RecordCount DEFAULT 0,
    DateRangeFrom datetime2     NULL,
    DateRangeTo   datetime2     NULL,
    ArchivedAt    datetime2     NOT NULL CONSTRAINT DF_ArchiveIndex_ArchivedAt DEFAULT SYSUTCDATETIME()
);
GO

CREATE TABLE dbo.StudioFAQs (
    FaqId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StudioFAQs PRIMARY KEY,
    Question NVARCHAR(500) NOT NULL,
    Answer NVARCHAR(MAX) NOT NULL,
    Category NVARCHAR(100) NOT NULL CONSTRAINT DF_StudioFAQs_Category DEFAULT 'General',
    DisplayOrder INT NOT NULL CONSTRAINT DF_StudioFAQs_DisplayOrder DEFAULT 0,
    IsActive BIT NOT NULL CONSTRAINT DF_StudioFAQs_IsActive DEFAULT 1,
    CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_StudioFAQs_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2 NOT NULL CONSTRAINT DF_StudioFAQs_UpdatedAt DEFAULT SYSUTCDATETIME()
);
GO

/* =====================================================================
   6. OVERTIME CHARGES & PAYMENT TRANSACTIONS TABLES
   ===================================================================== */

CREATE TABLE dbo.EventOvertimeCharges (
    OvertimeChargeID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    EventAppointmentID nvarchar(30) NOT NULL,
    ExtraHours decimal(5,2) NOT NULL CONSTRAINT DF_EventOvertime_Hours DEFAULT 0.00,
    HourlyRate decimal(18,2) NOT NULL CONSTRAINT DF_EventOvertime_Rate DEFAULT 0.00,
    SurchargeAmount decimal(18,2) NOT NULL CONSTRAINT DF_EventOvertime_Surcharge DEFAULT 0.00,
    TotalOvertimeFee AS (ExtraHours * HourlyRate + SurchargeAmount) PERSISTED,
    ReasonNotes nvarchar(500) NULL,
    Notes nvarchar(500) NULL,
    LoggedByEmployeeID nvarchar(30) NULL,
    AssignedEmployeeName nvarchar(100) NULL,
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_EventOvertime_CreatedAt DEFAULT SYSUTCDATETIME()
);

CREATE TABLE dbo.PaymentTransactions (
    PaymentTransactionID nvarchar(50) NOT NULL PRIMARY KEY,
    EventAppointmentID nvarchar(30) NOT NULL,
    PaymentType nvarchar(50) NOT NULL CONSTRAINT DF_PaymentTransactions_Type DEFAULT 'Downpayment_25', -- 'Downpayment_25', 'Final_Balance', 'Overtime'
    PaymentMethod nvarchar(50) NOT NULL CONSTRAINT DF_PaymentTransactions_Method DEFAULT 'Xendit_Card', -- 'Xendit_GCash', 'Xendit_Maya', 'Xendit_Card', 'Cash'
    Amount decimal(18,2) NOT NULL CONSTRAINT DF_PaymentTransactions_Amount DEFAULT 0.00,
    XenditPaymentId nvarchar(100) NULL,
    XenditInvoiceId nvarchar(100) NULL,
    XenditPaymentChannel nvarchar(100) NULL,
    Status nvarchar(30) NOT NULL CONSTRAINT DF_PaymentTransactions_Status DEFAULT 'Pending', -- 'Pending', 'Paid', 'Failed', 'Expired'
    ExternalReference nvarchar(100) NULL,
    CustomerPaymentStatus nvarchar(40) NULL,
    EmployeeRemittanceStatus nvarchar(40) NULL,
    AdminApproverID nvarchar(100) NULL,
    ApprovalTimestamp datetime2 NULL,
    ReceivedByEmployeeID nvarchar(30) NULL, -- For cash collected by Team Leader
    AssignedEmployeeName nvarchar(100) NULL,
    PaidAt datetime2 NULL,
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_PaymentTransactions_CreatedAt DEFAULT SYSUTCDATETIME()
);
GO

/* =====================================================================
   7. PORTFOLIO & SHOWCASE TABLES
   ===================================================================== */

CREATE TABLE dbo.PortfolioCategory (
    CategoryID nvarchar(50) NOT NULL CONSTRAINT PK_PortfolioCategory PRIMARY KEY,
    CategoryName nvarchar(120) NOT NULL,
    Description nvarchar(400) NULL,
    ParentSection nvarchar(30) NOT NULL CONSTRAINT DF_PortfolioCategory_Parent DEFAULT 'Photography',
    ColorTag nvarchar(20) NOT NULL CONSTRAINT DF_PortfolioCategory_Color DEFAULT 'Blue',
    IsActive bit NOT NULL CONSTRAINT DF_PortfolioCategory_Active DEFAULT 1,
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_PortfolioCategory_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt datetime2 NOT NULL CONSTRAINT DF_PortfolioCategory_UpdatedAt DEFAULT SYSUTCDATETIME()
);

CREATE TABLE dbo.PortfolioCategoryService (
    CategoryID nvarchar(50) NOT NULL,
    ServiceName nvarchar(120) NOT NULL,
    CONSTRAINT PK_PortfolioCategoryService PRIMARY KEY (CategoryID, ServiceName)
);

CREATE TABLE dbo.Portfolios (
    PortfolioId nvarchar(50) NOT NULL PRIMARY KEY,
    PortfolioName nvarchar(255) NOT NULL,
    PortfolioDescription nvarchar(1000) NULL,
    CategoryID nvarchar(50) NOT NULL,
    IsFeatured bit NOT NULL CONSTRAINT DF_Portfolios_Featured DEFAULT 0,
    HighlightImageFileName nvarchar(255) NULL,
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_Portfolios_CreatedAt DEFAULT SYSUTCDATETIME()
);

CREATE TABLE dbo.PortfolioImages (
    Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    PortfolioId nvarchar(50) NOT NULL,
    FileName nvarchar(500) NOT NULL,
    ImageUrl nvarchar(500) NULL,
    ContentType nvarchar(100) NULL CONSTRAINT DF_PortfolioImages_ContentType DEFAULT 'image/jpeg',
    ImageData varbinary(max) NULL,
    UploadedAt datetime2 NOT NULL CONSTRAINT DF_PortfolioImages_UploadedAt DEFAULT SYSUTCDATETIME()
);

CREATE TABLE dbo.HomeHighlights (
    HighlightID nvarchar(30) NOT NULL PRIMARY KEY,
    Title nvarchar(150) NOT NULL,
    Subtitle nvarchar(255) NULL,
    ImageUrl nvarchar(500) NOT NULL,
    ImageFileName nvarchar(500) NULL,
    ImageData varbinary(max) NULL,
    ContentType nvarchar(100) NULL CONSTRAINT DF_HomeHighlights_ContentType DEFAULT 'image/jpeg',
    DisplayOrder int NOT NULL CONSTRAINT DF_HomeHighlights_Order DEFAULT 0,
    IsActive bit NOT NULL CONSTRAINT DF_HomeHighlights_Active DEFAULT 1,
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_HomeHighlights_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt datetime2 NOT NULL CONSTRAINT DF_HomeHighlights_UpdatedAt DEFAULT SYSUTCDATETIME()
);
GO

/* =====================================================================
   8. FOREIGN KEY RELATIONSHIPS
   ===================================================================== */
ALTER TABLE dbo.Equipment ADD CONSTRAINT FK_Equipment_EquipmentType FOREIGN KEY (EquipmentTypeID) REFERENCES dbo.EquipmentType(EquipmentTypeID);
ALTER TABLE dbo.BookingNegotiation ADD CONSTRAINT FK_BookingNegotiation_Employee FOREIGN KEY (EmployeeID) REFERENCES dbo.Employees(EmployeeID) ON DELETE SET NULL;
ALTER TABLE dbo.BookingNegotiation ADD CONSTRAINT FK_BookingNegotiation_Customer FOREIGN KEY (CustomerID) REFERENCES dbo.Customer(CustomerID);
ALTER TABLE dbo.BookingNegotiation ADD CONSTRAINT FK_BookingNegotiation_EventAppointment FOREIGN KEY (EventAppointmentID) REFERENCES dbo.EventAppointment(EventAppointmentID) ON DELETE SET NULL;
ALTER TABLE dbo.EventEmployee ADD CONSTRAINT FK_EventEmployee_Employee FOREIGN KEY (EmployeeID) REFERENCES dbo.Employees(EmployeeID) ON DELETE SET NULL;
ALTER TABLE dbo.EventEquipment ADD CONSTRAINT FK_EventEquipment_Equipment FOREIGN KEY (EquipmentID) REFERENCES dbo.Equipment(EquipmentID);
ALTER TABLE dbo.EventAppointment ADD CONSTRAINT FK_EventAppointment_Customer FOREIGN KEY (CustomerID) REFERENCES dbo.Customer(CustomerID);
ALTER TABLE dbo.EventAppointment ADD CONSTRAINT FK_EventAppointment_Employee FOREIGN KEY (EmployeeID) REFERENCES dbo.Employees(EmployeeID) ON DELETE SET NULL;
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
ALTER TABLE dbo.HistoryLog ADD CONSTRAINT FK_HistoryLog_Employee FOREIGN KEY (EmployeeID) REFERENCES dbo.Employees(EmployeeID) ON DELETE SET NULL;
ALTER TABLE dbo.HistoryLog ADD CONSTRAINT FK_HistoryLog_ChangedByCustomer FOREIGN KEY (ChangedByCustomerID) REFERENCES dbo.Customer(CustomerID) ON DELETE SET NULL;
ALTER TABLE dbo.HistoryLog ADD CONSTRAINT FK_HistoryLog_ChangedByEmployee FOREIGN KEY (ChangedByEmployeeID) REFERENCES dbo.Employees(EmployeeID) ON DELETE SET NULL;
ALTER TABLE dbo.HistoryLog ADD CONSTRAINT FK_HistoryLog_ChangedByAdmin FOREIGN KEY (ChangedByAdminID) REFERENCES dbo.Admin(AdminID) ON DELETE SET NULL;
ALTER TABLE dbo.ServiceRate ADD CONSTRAINT FK_ServiceRate_Services FOREIGN KEY (ServicesID) REFERENCES dbo.Services(ServicesID) ON DELETE CASCADE;
ALTER TABLE dbo.ServiceRate ADD CONSTRAINT FK_ServiceRate_RateType FOREIGN KEY (RateTypeID) REFERENCES dbo.RateType(RateTypeID);
ALTER TABLE dbo.Employees ADD CONSTRAINT FK_Employees_Department FOREIGN KEY (DepartmentID) REFERENCES dbo.Departments(DepartmentID) ON DELETE SET NULL;
ALTER TABLE dbo.EmployeeSpecialization ADD CONSTRAINT FK_EmployeeSpecialization_Department FOREIGN KEY (DepartmentID) REFERENCES dbo.Departments(DepartmentID) ON DELETE SET NULL;
ALTER TABLE dbo.ServiceCategory ADD CONSTRAINT FK_ServiceCategory_RateType FOREIGN KEY (RateTypeID) REFERENCES dbo.RateType(RateTypeID);
ALTER TABLE dbo.EmployeeTimesheets ADD CONSTRAINT FK_EmployeeTimesheets_Employees FOREIGN KEY (EmployeeStudioId) REFERENCES dbo.Employees(EmployeeID) ON DELETE CASCADE;
ALTER TABLE dbo.PortfolioCategoryService ADD CONSTRAINT FK_PortfolioCategoryService_Category FOREIGN KEY (CategoryID) REFERENCES dbo.PortfolioCategory(CategoryID) ON DELETE CASCADE;
ALTER TABLE dbo.Portfolios ADD CONSTRAINT FK_Portfolios_PortfolioCategory FOREIGN KEY (CategoryID) REFERENCES dbo.PortfolioCategory(CategoryID);
ALTER TABLE dbo.PortfolioImages ADD CONSTRAINT FK_PortfolioImages_Portfolios FOREIGN KEY (PortfolioId) REFERENCES dbo.Portfolios(PortfolioId) ON DELETE CASCADE;
ALTER TABLE dbo.EventOvertimeCharges ADD CONSTRAINT FK_EventOvertime_EventAppointment FOREIGN KEY (EventAppointmentID) REFERENCES dbo.EventAppointment(EventAppointmentID) ON DELETE CASCADE;
ALTER TABLE dbo.EventOvertimeCharges ADD CONSTRAINT FK_EventOvertime_Employee FOREIGN KEY (LoggedByEmployeeID) REFERENCES dbo.Employees(EmployeeID) ON DELETE SET NULL;
ALTER TABLE dbo.PaymentTransactions ADD CONSTRAINT FK_PaymentTransactions_EventAppointment FOREIGN KEY (EventAppointmentID) REFERENCES dbo.EventAppointment(EventAppointmentID) ON DELETE CASCADE;
ALTER TABLE dbo.PaymentTransactions ADD CONSTRAINT FK_PaymentTransactions_Employee FOREIGN KEY (ReceivedByEmployeeID) REFERENCES dbo.Employees(EmployeeID) ON DELETE SET NULL;
GO

/* =====================================================================
   9. INDEXES FOR COMMON AUDIT, CATALOG, BOOKING & PORTFOLIO LOOKUPS
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
CREATE INDEX IX_EmployeeTimesheets_EmpDate ON dbo.EmployeeTimesheets (EmployeeStudioId, WorkDate);
CREATE INDEX IX_EmployeeTimesheets_ClockIn ON dbo.EmployeeTimesheets (ClockIn DESC);
CREATE INDEX IX_ServiceRate_Services       ON dbo.ServiceRate (ServicesID);
CREATE INDEX IX_ServiceRate_RateType       ON dbo.ServiceRate (RateTypeID);
CREATE INDEX IX_Portfolios_CategoryID      ON dbo.Portfolios (CategoryID);
CREATE INDEX IX_PortfolioImages_PortfolioId ON dbo.PortfolioImages (PortfolioId);
CREATE INDEX IX_HomeHighlights_DisplayOrder ON dbo.HomeHighlights (IsActive, DisplayOrder, CreatedAt DESC);
CREATE INDEX IX_PaymentTransactions_Event  ON dbo.PaymentTransactions (EventAppointmentID, CreatedAt DESC);
CREATE INDEX IX_EventOvertime_Event        ON dbo.EventOvertimeCharges (EventAppointmentID);
CREATE INDEX IX_BookingNegotiation_Customer ON dbo.BookingNegotiation (CustomerID, ConsultationDate DESC);
CREATE INDEX IX_BookingNegotiation_Employee ON dbo.BookingNegotiation (EmployeeID, ConsultationDate DESC);
CREATE INDEX IX_ArchiveIndex_Entity_ArchivedAt ON dbo.ArchiveIndex (EntityName, ArchivedAt DESC);
CREATE INDEX IX_Employees_Status_Dept ON dbo.Employees (EmployeeStatus, DepartmentID);
CREATE INDEX IX_EmployeeJointSpecialization_Emp_Spec ON dbo.EmployeeJointSpecialization (EmployeeID, SpecializationID);
CREATE INDEX IX_EventAppointment_Date_Status ON dbo.EventAppointment (EventDate, EventStatus);
CREATE INDEX IX_PaymentTransactions_Remittance ON dbo.PaymentTransactions (EmployeeRemittanceStatus, PaymentType);
GO

/* =====================================================================
   10. SNAPSHOT HARD DELETE SCHEMA ALTERATIONS
   (Applies changes non-destructively via ALTER statements)
   ===================================================================== */
-- Appointments (dbo.EventAppointment)
IF COL_LENGTH(N'dbo.EventAppointment', N'AssignedEmployeeName') IS NULL
    ALTER TABLE dbo.EventAppointment ADD AssignedEmployeeName NVARCHAR(100) NULL;

IF COL_LENGTH(N'dbo.EventAppointment', N'EmployeeID') IS NULL
    ALTER TABLE dbo.EventAppointment ADD EmployeeID NVARCHAR(30) NULL;
ELSE
    ALTER TABLE dbo.EventAppointment ALTER COLUMN EmployeeID NVARCHAR(30) NULL;

IF OBJECT_ID(N'dbo.FK_EventAppointment_Employee', N'F') IS NOT NULL
    ALTER TABLE dbo.EventAppointment DROP CONSTRAINT FK_EventAppointment_Employee;

ALTER TABLE dbo.EventAppointment ADD CONSTRAINT FK_EventAppointment_Employee 
    FOREIGN KEY (EmployeeID) REFERENCES dbo.Employees(EmployeeID) ON DELETE SET NULL;

-- Booking Negotiation transactions
IF COL_LENGTH(N'dbo.BookingNegotiation', N'AssignedEmployeeName') IS NULL
    ALTER TABLE dbo.BookingNegotiation ADD AssignedEmployeeName NVARCHAR(100) NULL;

ALTER TABLE dbo.BookingNegotiation ALTER COLUMN EmployeeID NVARCHAR(30) NULL;

IF OBJECT_ID(N'dbo.FK_BookingNegotiation_Employee', N'F') IS NOT NULL
    ALTER TABLE dbo.BookingNegotiation DROP CONSTRAINT FK_BookingNegotiation_Employee;

ALTER TABLE dbo.BookingNegotiation ADD CONSTRAINT FK_BookingNegotiation_Employee 
    FOREIGN KEY (EmployeeID) REFERENCES dbo.Employees(EmployeeID) ON DELETE SET NULL;

-- Appointments table (if present in custom/test environments)
IF OBJECT_ID(N'dbo.Appointments', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.Appointments', N'AssignedEmployeeName') IS NULL
        ALTER TABLE dbo.Appointments ADD AssignedEmployeeName NVARCHAR(100) NULL;

    IF COL_LENGTH(N'dbo.Appointments', N'EmployeeID') IS NOT NULL
        ALTER TABLE dbo.Appointments ALTER COLUMN EmployeeID NVARCHAR(30) NULL;

    IF OBJECT_ID(N'dbo.FK_Appointments_Employee', N'F') IS NOT NULL
        ALTER TABLE dbo.Appointments DROP CONSTRAINT FK_Appointments_Employee;

    IF OBJECT_ID(N'dbo.FK_Appointments_Employees', N'F') IS NOT NULL
        ALTER TABLE dbo.Appointments DROP CONSTRAINT FK_Appointments_Employees;

    IF COL_LENGTH(N'dbo.Appointments', N'EmployeeID') IS NOT NULL
        ALTER TABLE dbo.Appointments ADD CONSTRAINT FK_Appointments_Employee 
            FOREIGN KEY (EmployeeID) REFERENCES dbo.Employees(EmployeeID) ON DELETE SET NULL;
END;

-- EventEmployee junction transactions
IF COL_LENGTH(N'dbo.EventEmployee', N'AssignedEmployeeName') IS NULL
    ALTER TABLE dbo.EventEmployee ADD AssignedEmployeeName NVARCHAR(100) NULL;

ALTER TABLE dbo.EventEmployee ALTER COLUMN EmployeeID NVARCHAR(30) NULL;

IF OBJECT_ID(N'dbo.FK_EventEmployee_Employee', N'F') IS NOT NULL
    ALTER TABLE dbo.EventEmployee DROP CONSTRAINT FK_EventEmployee_Employee;

ALTER TABLE dbo.EventEmployee ADD CONSTRAINT FK_EventEmployee_Employee 
    FOREIGN KEY (EmployeeID) REFERENCES dbo.Employees(EmployeeID) ON DELETE SET NULL;

-- HistoryLog audit records
IF OBJECT_ID(N'dbo.FK_HistoryLog_Employee', N'F') IS NOT NULL
    ALTER TABLE dbo.HistoryLog DROP CONSTRAINT FK_HistoryLog_Employee;

ALTER TABLE dbo.HistoryLog ADD CONSTRAINT FK_HistoryLog_Employee 
    FOREIGN KEY (EmployeeID) REFERENCES dbo.Employees(EmployeeID) ON DELETE SET NULL;

-- EventOvertimeCharges transaction table
IF COL_LENGTH(N'dbo.EventOvertimeCharges', N'AssignedEmployeeName') IS NULL
    ALTER TABLE dbo.EventOvertimeCharges ADD AssignedEmployeeName NVARCHAR(100) NULL;

IF COL_LENGTH(N'dbo.EventOvertimeCharges', N'Notes') IS NULL
BEGIN
    ALTER TABLE dbo.EventOvertimeCharges ADD Notes NVARCHAR(500) NULL;
    IF COL_LENGTH(N'dbo.EventOvertimeCharges', N'ReasonNotes') IS NOT NULL
        EXEC(N'UPDATE dbo.EventOvertimeCharges SET Notes = ReasonNotes WHERE Notes IS NULL AND ReasonNotes IS NOT NULL;');
END;

ALTER TABLE dbo.EventOvertimeCharges ALTER COLUMN LoggedByEmployeeID NVARCHAR(30) NULL;

IF OBJECT_ID(N'dbo.FK_EventOvertime_Employee', N'F') IS NOT NULL
    ALTER TABLE dbo.EventOvertimeCharges DROP CONSTRAINT FK_EventOvertime_Employee;

ALTER TABLE dbo.EventOvertimeCharges ADD CONSTRAINT FK_EventOvertime_Employee 
    FOREIGN KEY (LoggedByEmployeeID) REFERENCES dbo.Employees(EmployeeID) ON DELETE SET NULL;

-- PaymentTransactions transaction table
IF COL_LENGTH(N'dbo.PaymentTransactions', N'AssignedEmployeeName') IS NULL
    ALTER TABLE dbo.PaymentTransactions ADD AssignedEmployeeName NVARCHAR(100) NULL;

ALTER TABLE dbo.PaymentTransactions ALTER COLUMN ReceivedByEmployeeID NVARCHAR(30) NULL;

IF OBJECT_ID(N'dbo.FK_PaymentTransactions_Employee', N'F') IS NOT NULL
    ALTER TABLE dbo.PaymentTransactions DROP CONSTRAINT FK_PaymentTransactions_Employee;

ALTER TABLE dbo.PaymentTransactions ADD CONSTRAINT FK_PaymentTransactions_Employee 
    FOREIGN KEY (ReceivedByEmployeeID) REFERENCES dbo.Employees(EmployeeID) ON DELETE SET NULL;

-- Departments table & seed data
IF OBJECT_ID(N'dbo.Departments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Departments (
        DepartmentID nvarchar(30) NOT NULL PRIMARY KEY,
        DepartmentName nvarchar(100) NOT NULL UNIQUE,
        DepartmentDescription nvarchar(500) NULL,
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_Departments_CreatedAt DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'dbo.Departments', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.Departments WHERE DepartmentID = 'DEP-0001')
        INSERT INTO dbo.Departments (DepartmentID, DepartmentName, DepartmentDescription) VALUES ('DEP-0001', 'Photography', 'Professional studio and on-location photography services');
    IF NOT EXISTS (SELECT 1 FROM dbo.Departments WHERE DepartmentID = 'DEP-0002')
        INSERT INTO dbo.Departments (DepartmentID, DepartmentName, DepartmentDescription) VALUES ('DEP-0002', 'Videography', 'Cinematic video recording, coverage, and production');
    IF NOT EXISTS (SELECT 1 FROM dbo.Departments WHERE DepartmentID = 'DEP-0003')
        INSERT INTO dbo.Departments (DepartmentID, DepartmentName, DepartmentDescription) VALUES ('DEP-0003', 'Post-Processing & Editing', 'Retouching, color grading, and multimedia post-production');
    IF NOT EXISTS (SELECT 1 FROM dbo.Departments WHERE DepartmentID = 'DEP-0004')
        INSERT INTO dbo.Departments (DepartmentID, DepartmentName, DepartmentDescription) VALUES ('DEP-0004', 'Studio Staff & Operations', 'Studio logistics, client onboarding, and equipment handling');
    IF NOT EXISTS (SELECT 1 FROM dbo.Departments WHERE DepartmentID = 'DEP-0005')
        INSERT INTO dbo.Departments (DepartmentID, DepartmentName, DepartmentDescription) VALUES ('DEP-0005', 'Administration & Studio Management', 'Executive management, finances, scheduling, and staff supervision');
    IF NOT EXISTS (SELECT 1 FROM dbo.Departments WHERE DepartmentID = 'DEP-0006')
        INSERT INTO dbo.Departments (DepartmentID, DepartmentName, DepartmentDescription) VALUES ('DEP-0006', 'Production & Lighting', 'Set design, lighting engineering, and technical floor management');
    IF NOT EXISTS (SELECT 1 FROM dbo.Departments WHERE DepartmentID = 'DEP-0007')
        INSERT INTO dbo.Departments (DepartmentID, DepartmentName, DepartmentDescription) VALUES ('DEP-0007', 'Creative Direction', 'Art direction, concept styling, theme planning, and brand storytelling');
END;

-- Employees schema updates (Address, BirthYear, DepartmentID, EmployeeStatus length & check constraint)
IF OBJECT_ID(N'dbo.Employees', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.Employees', N'Address') IS NULL
        ALTER TABLE dbo.Employees ADD Address nvarchar(500) NULL;

    IF COL_LENGTH(N'dbo.Employees', N'BirthYear') IS NULL
        ALTER TABLE dbo.Employees ADD BirthYear int NULL;

    IF COL_LENGTH(N'dbo.Employees', N'DepartmentID') IS NULL
        ALTER TABLE dbo.Employees ADD DepartmentID nvarchar(30) NULL;

    ALTER TABLE dbo.Employees ALTER COLUMN EmployeeStatus nvarchar(50) NOT NULL;

    IF OBJECT_ID(N'dbo.CK_Employee_Status', N'C') IS NOT NULL
        ALTER TABLE dbo.Employees DROP CONSTRAINT CK_Employee_Status;

    ALTER TABLE dbo.Employees ADD CONSTRAINT CK_Employee_Status 
        CHECK (EmployeeStatus IN ('Active','Inactive','Clock-In (Duty)','Clock-Out (Off Duty)','Archive'));

    IF OBJECT_ID(N'dbo.FK_Employees_Department', N'F') IS NOT NULL
        ALTER TABLE dbo.Employees DROP CONSTRAINT FK_Employees_Department;

    ALTER TABLE dbo.Employees ADD CONSTRAINT FK_Employees_Department 
        FOREIGN KEY (DepartmentID) REFERENCES dbo.Departments(DepartmentID) ON DELETE SET NULL;
END;

-- EmployeeSpecialization schema updates (DepartmentID, DepartmentName, FK)
IF OBJECT_ID(N'dbo.EmployeeSpecialization', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.EmployeeSpecialization', N'DepartmentID') IS NULL
        ALTER TABLE dbo.EmployeeSpecialization ADD DepartmentID nvarchar(30) NULL;

    IF COL_LENGTH(N'dbo.EmployeeSpecialization', N'DepartmentName') IS NULL
        ALTER TABLE dbo.EmployeeSpecialization ADD DepartmentName nvarchar(100) NULL;

    IF OBJECT_ID(N'dbo.FK_EmployeeSpecialization_Department', N'F') IS NOT NULL
        ALTER TABLE dbo.EmployeeSpecialization DROP CONSTRAINT FK_EmployeeSpecialization_Department;

    ALTER TABLE dbo.EmployeeSpecialization ADD CONSTRAINT FK_EmployeeSpecialization_Department 
        FOREIGN KEY (DepartmentID) REFERENCES dbo.Departments(DepartmentID) ON DELETE SET NULL;

    -- Ensure default specializations are linked to their respective departments
    IF NOT EXISTS (SELECT 1 FROM dbo.EmployeeSpecialization WHERE SpecializationID = 'SP-998')
    BEGIN
        INSERT INTO dbo.EmployeeSpecialization (
            SpecializationID, SpecializationName, SpecializationDescription,
            Specialization, Description, DepartmentID, DepartmentName, ColorTag, CreatedAt
        )
        VALUES (
            'SP-998', 'Senior Photographer', 'Photography',
            'Senior Photographer', 'Photography', 'DEP-0001', 'Photography', 'Blue', SYSUTCDATETIME()
        );
    END
    ELSE
    BEGIN
        UPDATE dbo.EmployeeSpecialization
        SET DepartmentID = 'DEP-0001', DepartmentName = 'Photography'
        WHERE SpecializationID = 'SP-998' AND (DepartmentID IS NULL OR DepartmentID = '');
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.EmployeeSpecialization WHERE SpecializationID = 'SP-999')
    BEGIN
        INSERT INTO dbo.EmployeeSpecialization (
            SpecializationID, SpecializationName, SpecializationDescription,
            Specialization, Description, DepartmentID, DepartmentName, ColorTag, CreatedAt
        )
        VALUES (
            'SP-999', 'Office Clerk', 'Studio Staff & Operations',
            'Office Clerk', 'Studio Staff & Operations', 'DEP-0004', 'Studio Staff & Operations', 'Green', SYSUTCDATETIME()
        );
    END
    ELSE
    BEGIN
        UPDATE dbo.EmployeeSpecialization
        SET DepartmentID = 'DEP-0004', DepartmentName = 'Studio Staff & Operations'
        WHERE SpecializationID = 'SP-999' AND (DepartmentID IS NULL OR DepartmentID = '');
    END;
END;

-- ServiceCategory schema updates (RateTypeID, RateTypeName, FK)
IF OBJECT_ID(N'dbo.ServiceCategory', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.ServiceCategory', N'RateTypeID') IS NULL
        ALTER TABLE dbo.ServiceCategory ADD RateTypeID nvarchar(30) NOT NULL CONSTRAINT DF_ServiceCategory_RateTypeID DEFAULT 'RT-0001';

    IF COL_LENGTH(N'dbo.ServiceCategory', N'RateTypeName') IS NULL
        ALTER TABLE dbo.ServiceCategory ADD RateTypeName nvarchar(50) NOT NULL CONSTRAINT DF_ServiceCategory_RateTypeName DEFAULT 'Per Hour';

    IF OBJECT_ID(N'dbo.FK_ServiceCategory_RateType', N'F') IS NOT NULL
        ALTER TABLE dbo.ServiceCategory DROP CONSTRAINT FK_ServiceCategory_RateType;

    ALTER TABLE dbo.ServiceCategory ADD CONSTRAINT FK_ServiceCategory_RateType 
        FOREIGN KEY (RateTypeID) REFERENCES dbo.RateType(RateTypeID);
END;

-- AspNetUsers schema updates for ApplicationUser (Address, BirthYear, DepartmentId)
IF OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.AspNetUsers', N'Address') IS NULL
        ALTER TABLE dbo.AspNetUsers ADD Address nvarchar(500) NULL;

    IF COL_LENGTH(N'dbo.AspNetUsers', N'BirthYear') IS NULL
        ALTER TABLE dbo.AspNetUsers ADD BirthYear int NULL;

    IF COL_LENGTH(N'dbo.AspNetUsers', N'DepartmentId') IS NULL
        ALTER TABLE dbo.AspNetUsers ADD DepartmentId nvarchar(30) NULL;
END;
GO

-- Pending Payment lock + Xendit QR (EventAppointment.LockExpiresAt / QrString)
IF OBJECT_ID(N'dbo.EventAppointment', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.EventAppointment', N'LockExpiresAt') IS NULL
        ALTER TABLE dbo.EventAppointment ADD LockExpiresAt datetime2 NULL;
END;
GO

IF OBJECT_ID(N'dbo.EventAppointment', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.EventAppointment', N'QrString') IS NULL
        ALTER TABLE dbo.EventAppointment ADD QrString nvarchar(1000) NULL;
END;
GO

IF OBJECT_ID(N'dbo.EventAppointment', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.check_constraints
                   WHERE name = N'CK_EventAppointment_Status' AND definition LIKE N'%Pending Payment%')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_EventAppointment_Status')
        ALTER TABLE dbo.EventAppointment DROP CONSTRAINT CK_EventAppointment_Status;

    ALTER TABLE dbo.EventAppointment ADD CONSTRAINT CK_EventAppointment_Status
        CHECK (EventStatus IN ('Pending','Awaiting_Downpayment','Pending Payment','Confirmed','Shot','Editing','Delivered','Cancelled','Expired'));
END;
GO

IF OBJECT_ID(N'dbo.EventAppointment', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_EventAppointment_Status_LockExpiresAt' AND object_id = OBJECT_ID(N'dbo.EventAppointment'))
    CREATE INDEX IX_EventAppointment_Status_LockExpiresAt ON dbo.EventAppointment (EventStatus, LockExpiresAt);
GO
/* =====================================================================
   HANGFIRE BACKGROUND JOB STORAGE SCHEMA (Version 9)
   ===================================================================== */

-- This file is part of Hangfire. Copyright © 2013-2014 Hangfire OÜ.
-- 
-- Hangfire is free software: you can redistribute it and/or modify
-- it under the terms of the GNU Lesser General Public License as 
-- published by the Free Software Foundation, either version 3 
-- of the License, or any later version.
-- 
-- Hangfire is distributed in the hope that it will be useful,
-- but WITHOUT ANY WARRANTY; without even the implied warranty of
-- MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
-- GNU Lesser General Public License for more details.
-- 
-- You should have received a copy of the GNU Lesser General Public 
-- License along with Hangfire. If not, see <http://www.gnu.org/licenses/>.

SET NOCOUNT ON
SET XACT_ABORT ON
DECLARE @TARGET_SCHEMA_VERSION INT;
DECLARE @DISABLE_HEAVY_MIGRATIONS BIT;
SET @TARGET_SCHEMA_VERSION = 9;
--SET @DISABLE_HEAVY_MIGRATIONS = 1;

PRINT 'Installing Hangfire SQL objects...';

BEGIN TRANSACTION;

-- Acquire exclusive lock to prevent deadlocks caused by schema creation / version update
DECLARE @SchemaLockResult INT;
EXEC @SchemaLockResult = sp_getapplock @Resource = 'HangFire:SchemaLock', @LockMode = 'Exclusive'

-- Create the database schema if it doesn't exists
IF NOT EXISTS (SELECT [schema_id] FROM [sys].[schemas] WHERE [name] = 'HangFire')
BEGIN
    EXEC (N'CREATE SCHEMA [HangFire]');
    PRINT 'Created database schema [HangFire]';
END
ELSE
    PRINT 'Database schema [HangFire] already exists';
    
DECLARE @SCHEMA_ID int;
SELECT @SCHEMA_ID = [schema_id] FROM [sys].[schemas] WHERE [name] = 'HangFire';

-- Create the [HangFire].Schema table if not exists
IF NOT EXISTS(SELECT [object_id] FROM [sys].[tables] 
    WHERE [name] = 'Schema' AND [schema_id] = @SCHEMA_ID)
BEGIN
    CREATE TABLE [HangFire].[Schema](
        [Version] [int] NOT NULL,
        CONSTRAINT [PK_HangFire_Schema] PRIMARY KEY CLUSTERED ([Version] ASC)
    );
    PRINT 'Created table [HangFire].[Schema]';
END
ELSE
    PRINT 'Table [HangFire].[Schema] already exists';
    
DECLARE @CURRENT_SCHEMA_VERSION int;
SELECT @CURRENT_SCHEMA_VERSION = [Version] FROM [HangFire].[Schema];

PRINT 'Current Hangfire schema version: ' + CASE WHEN @CURRENT_SCHEMA_VERSION IS NULL THEN 'none' ELSE CONVERT(nvarchar, @CURRENT_SCHEMA_VERSION) END;

IF @CURRENT_SCHEMA_VERSION IS NOT NULL AND @CURRENT_SCHEMA_VERSION > @TARGET_SCHEMA_VERSION
BEGIN
    ROLLBACK TRANSACTION;
    PRINT 'Hangfire current database schema version ' + CAST(@CURRENT_SCHEMA_VERSION AS NVARCHAR) +
          ' is newer than the configured SqlServerStorage schema version ' + CAST(@TARGET_SCHEMA_VERSION AS NVARCHAR) +
          '. Will not apply any migrations.';
    RETURN;
END

-- Install [HangFire] schema objects
IF @CURRENT_SCHEMA_VERSION IS NULL
BEGIN
    IF @DISABLE_HEAVY_MIGRATIONS = 1
    BEGIN
        SET @DISABLE_HEAVY_MIGRATIONS = 0;
        PRINT 'Enabling HEAVY_MIGRATIONS, because we are installing objects from scratch';
    END

    PRINT 'Installing schema version 1';
        
    -- Create job tables
    CREATE TABLE [HangFire].[Job] (
        [Id] [int] IDENTITY(1,1) NOT NULL,
		[StateId] [int] NULL,
		[StateName] [nvarchar](20) NULL, -- To speed-up queries.
        [InvocationData] [nvarchar](max) NOT NULL,
        [Arguments] [nvarchar](max) NOT NULL,
        [CreatedAt] [datetime] NOT NULL,
        [ExpireAt] [datetime] NULL,

        CONSTRAINT [PK_HangFire_Job] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
    PRINT 'Created table [HangFire].[Job]';

	CREATE NONCLUSTERED INDEX [IX_HangFire_Job_StateName] ON [HangFire].[Job] ([StateName] ASC);
	PRINT 'Created index [IX_HangFire_Job_StateName]';
        
    -- Job history table
        
    CREATE TABLE [HangFire].[State] (
        [Id] [int] IDENTITY(1,1) NOT NULL,
        [JobId] [int] NOT NULL,
		[Name] [nvarchar](20) NOT NULL,
		[Reason] [nvarchar](100) NULL,
        [CreatedAt] [datetime] NOT NULL,
        [Data] [nvarchar](max) NULL,
            
        CONSTRAINT [PK_HangFire_State] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
    PRINT 'Created table [HangFire].[State]';

    ALTER TABLE [HangFire].[State] ADD CONSTRAINT [FK_HangFire_State_Job] FOREIGN KEY([JobId])
        REFERENCES [HangFire].[Job] ([Id])
        ON UPDATE CASCADE
        ON DELETE CASCADE;
    PRINT 'Created constraint [FK_HangFire_State_Job]';
        
    CREATE NONCLUSTERED INDEX [IX_HangFire_State_JobId] ON [HangFire].[State] ([JobId] ASC);
    PRINT 'Created index [IX_HangFire_State_JobId]';
        
    -- Job parameters table
        
    CREATE TABLE [HangFire].[JobParameter](
        [Id] [int] IDENTITY(1,1) NOT NULL,
        [JobId] [int] NOT NULL,
        [Name] [nvarchar](40) NOT NULL,
        [Value] [nvarchar](max) NULL,
            
        CONSTRAINT [PK_HangFire_JobParameter] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
    PRINT 'Created table [HangFire].[JobParameter]';

    ALTER TABLE [HangFire].[JobParameter] ADD CONSTRAINT [FK_HangFire_JobParameter_Job] FOREIGN KEY([JobId])
        REFERENCES [HangFire].[Job] ([Id])
        ON UPDATE CASCADE
        ON DELETE CASCADE;
    PRINT 'Created constraint [FK_HangFire_JobParameter_Job]';
        
    CREATE NONCLUSTERED INDEX [IX_HangFire_JobParameter_JobIdAndName] ON [HangFire].[JobParameter] (
        [JobId] ASC,
        [Name] ASC
    );
    PRINT 'Created index [IX_HangFire_JobParameter_JobIdAndName]';
        
    -- Job queue table
        
    CREATE TABLE [HangFire].[JobQueue](
        [Id] [int] IDENTITY(1,1) NOT NULL,
        [JobId] [int] NOT NULL,
        [Queue] [nvarchar](20) NOT NULL,
        [FetchedAt] [datetime] NULL,
            
        CONSTRAINT [PK_HangFire_JobQueue] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
    PRINT 'Created table [HangFire].[JobQueue]';
        
    CREATE NONCLUSTERED INDEX [IX_HangFire_JobQueue_JobIdAndQueue] ON [HangFire].[JobQueue] (
        [JobId] ASC,
        [Queue] ASC
    );
    PRINT 'Created index [IX_HangFire_JobQueue_JobIdAndQueue]';
        
    CREATE NONCLUSTERED INDEX [IX_HangFire_JobQueue_QueueAndFetchedAt] ON [HangFire].[JobQueue] (
        [Queue] ASC,
        [FetchedAt] ASC
    );
    PRINT 'Created index [IX_HangFire_JobQueue_QueueAndFetchedAt]';
        
    -- Servers table
        
    CREATE TABLE [HangFire].[Server](
        [Id] [nvarchar](200) NOT NULL,
        [Data] [nvarchar](max) NULL,
        [LastHeartbeat] [datetime] NULL,
            
        CONSTRAINT [PK_HangFire_Server] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
    PRINT 'Created table [HangFire].[Server]';
        
    -- Extension tables
        
    CREATE TABLE [HangFire].[Hash](
        [Id] [int] IDENTITY(1,1) NOT NULL,
        [Key] [nvarchar](100) NOT NULL,
        [Name] [nvarchar](40) NOT NULL,
        [StringValue] [nvarchar](max) NULL,
        [IntValue] [int] NULL,
        [ExpireAt] [datetime] NULL,
            
        CONSTRAINT [PK_HangFire_Hash] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
    PRINT 'Created table [HangFire].[Hash]';
        
    CREATE UNIQUE NONCLUSTERED INDEX [UX_HangFire_Hash_KeyAndName] ON [HangFire].[Hash] (
        [Key] ASC,
        [Name] ASC
    );
    PRINT 'Created index [UX_HangFire_Hash_KeyAndName]';
        
    CREATE TABLE [HangFire].[List](
        [Id] [int] IDENTITY(1,1) NOT NULL,
        [Key] [nvarchar](100) NOT NULL,
        [Value] [nvarchar](max) NULL,
        [ExpireAt] [datetime] NULL,
            
        CONSTRAINT [PK_HangFire_List] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
    PRINT 'Created table [HangFire].[List]';
        
    CREATE TABLE [HangFire].[Set](
        [Id] [int] IDENTITY(1,1) NOT NULL,
        [Key] [nvarchar](100) NOT NULL,
        [Score] [float] NOT NULL,
        [Value] [nvarchar](256) NOT NULL,
        [ExpireAt] [datetime] NULL,
            
        CONSTRAINT [PK_HangFire_Set] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
    PRINT 'Created table [HangFire].[Set]';
        
    CREATE UNIQUE NONCLUSTERED INDEX [UX_HangFire_Set_KeyAndValue] ON [HangFire].[Set] (
        [Key] ASC,
        [Value] ASC
    );
    PRINT 'Created index [UX_HangFire_Set_KeyAndValue]';
        
    CREATE TABLE [HangFire].[Value](
        [Id] [int] IDENTITY(1,1) NOT NULL,
        [Key] [nvarchar](100) NOT NULL,
        [StringValue] [nvarchar](max) NULL,
        [IntValue] [int] NULL,
        [ExpireAt] [datetime] NULL,
            
        CONSTRAINT [PK_HangFire_Value] PRIMARY KEY CLUSTERED (
            [Id] ASC
        )
    );
    PRINT 'Created table [HangFire].[Value]';
        
    CREATE UNIQUE NONCLUSTERED INDEX [UX_HangFire_Value_Key] ON [HangFire].[Value] (
        [Key] ASC
    );
    PRINT 'Created index [UX_HangFire_Value_Key]';

	CREATE TABLE [HangFire].[Counter](
		[Id] [int] IDENTITY(1,1) NOT NULL,
		[Key] [nvarchar](100) NOT NULL,
		[Value] [tinyint] NOT NULL,
		[ExpireAt] [datetime] NULL,

		CONSTRAINT [PK_HangFire_Counter] PRIMARY KEY CLUSTERED ([Id] ASC)
	);
	PRINT 'Created table [HangFire].[Counter]';

	CREATE NONCLUSTERED INDEX [IX_HangFire_Counter_Key] ON [HangFire].[Counter] ([Key] ASC)
	INCLUDE ([Value]);
	PRINT 'Created index [IX_HangFire_Counter_Key]';

	SET @CURRENT_SCHEMA_VERSION = 1;
END

IF @CURRENT_SCHEMA_VERSION = 1
BEGIN
	PRINT 'Installing schema version 2';

	-- https://github.com/odinserj/HangFire/issues/83

	DROP INDEX [IX_HangFire_Counter_Key] ON [HangFire].[Counter];

	ALTER TABLE [HangFire].[Counter] ALTER COLUMN [Value] SMALLINT NOT NULL;

	CREATE NONCLUSTERED INDEX [IX_HangFire_Counter_Key] ON [HangFire].[Counter] ([Key] ASC)
	INCLUDE ([Value]);
	PRINT 'Index [IX_HangFire_Counter_Key] re-created';

	DROP TABLE [HangFire].[Value];
	DROP TABLE [HangFire].[Hash];
	PRINT 'Dropped tables [HangFire].[Value] and [HangFire].[Hash]'

	DELETE FROM [HangFire].[Server] WHERE [LastHeartbeat] IS NULL;
	ALTER TABLE [HangFire].[Server] ALTER COLUMN [LastHeartbeat] DATETIME NOT NULL;

	SET @CURRENT_SCHEMA_VERSION = 2;
END

IF @CURRENT_SCHEMA_VERSION = 2
BEGIN
	PRINT 'Installing schema version 3';

	DROP INDEX [IX_HangFire_JobQueue_JobIdAndQueue] ON [HangFire].[JobQueue];
	PRINT 'Dropped index [IX_HangFire_JobQueue_JobIdAndQueue]';

	CREATE TABLE [HangFire].[Hash](
		[Id] [int] IDENTITY(1,1) NOT NULL,
		[Key] [nvarchar](100) NOT NULL,
		[Field] [nvarchar](100) NOT NULL,
		[Value] [nvarchar](max) NULL,
		[ExpireAt] [datetime2](7) NULL,
		
		CONSTRAINT [PK_HangFire_Hash] PRIMARY KEY CLUSTERED ([Id] ASC)
	);
	PRINT 'Created table [HangFire].[Hash]';

	CREATE UNIQUE NONCLUSTERED INDEX [UX_HangFire_Hash_Key_Field] ON [HangFire].[Hash] (
		[Key] ASC,
		[Field] ASC
	);
	PRINT 'Created index [UX_HangFire_Hash_Key_Field]';

	SET @CURRENT_SCHEMA_VERSION = 3;
END

IF @CURRENT_SCHEMA_VERSION = 3
BEGIN
	PRINT 'Installing schema version 4';

	CREATE TABLE [HangFire].[AggregatedCounter] (
		[Id] [int] IDENTITY(1,1) NOT NULL,
		[Key] [nvarchar](100) NOT NULL,
		[Value] [bigint] NOT NULL,
		[ExpireAt] [datetime] NULL,

		CONSTRAINT [PK_HangFire_CounterAggregated] PRIMARY KEY CLUSTERED ([Id] ASC)
	);
	PRINT 'Created table [HangFire].[AggregatedCounter]';

	CREATE UNIQUE NONCLUSTERED INDEX [UX_HangFire_CounterAggregated_Key] ON [HangFire].[AggregatedCounter] (
		[Key] ASC
	) INCLUDE ([Value]);
	PRINT 'Created index [UX_HangFire_CounterAggregated_Key]';

	CREATE NONCLUSTERED INDEX [IX_HangFire_Hash_ExpireAt] ON [HangFire].[Hash] ([ExpireAt])
	INCLUDE ([Id]);

	CREATE NONCLUSTERED INDEX [IX_HangFire_Job_ExpireAt] ON [HangFire].[Job] ([ExpireAt])
	INCLUDE ([Id]);

	CREATE NONCLUSTERED INDEX [IX_HangFire_List_ExpireAt] ON [HangFire].[List] ([ExpireAt])
	INCLUDE ([Id]);

	CREATE NONCLUSTERED INDEX [IX_HangFire_Set_ExpireAt] ON [HangFire].[Set] ([ExpireAt])
	INCLUDE ([Id]);

	PRINT 'Created indexes for [ExpireAt] columns';

	CREATE NONCLUSTERED INDEX [IX_HangFire_Hash_Key] ON [HangFire].[Hash] ([Key] ASC)
	INCLUDE ([ExpireAt]);
	PRINT 'Created index [IX_HangFire_Hash_Key]';

	CREATE NONCLUSTERED INDEX [IX_HangFire_List_Key] ON [HangFire].[List] ([Key] ASC)
	INCLUDE ([ExpireAt], [Value]);
	PRINT 'Created index [IX_HangFire_List_Key]';

	CREATE NONCLUSTERED INDEX [IX_HangFire_Set_Key] ON [HangFire].[Set] ([Key] ASC)
	INCLUDE ([ExpireAt], [Value]);
	PRINT 'Created index [IX_HangFire_Set_Key]';

	SET @CURRENT_SCHEMA_VERSION = 4;
END

IF @CURRENT_SCHEMA_VERSION = 4
BEGIN
	PRINT 'Installing schema version 5';

	DROP INDEX [IX_HangFire_JobQueue_QueueAndFetchedAt] ON [HangFire].[JobQueue];
	PRINT 'Dropped index [IX_HangFire_JobQueue_QueueAndFetchedAt] to modify the [HangFire].[JobQueue].[Queue] column';

	ALTER TABLE [HangFire].[JobQueue] ALTER COLUMN [Queue] NVARCHAR (50) NOT NULL;
	PRINT 'Modified [HangFire].[JobQueue].[Queue] length to 50';

	CREATE NONCLUSTERED INDEX [IX_HangFire_JobQueue_QueueAndFetchedAt] ON [HangFire].[JobQueue] (
        [Queue] ASC,
        [FetchedAt] ASC
    );
    PRINT 'Re-created index [IX_HangFire_JobQueue_QueueAndFetchedAt]';

	ALTER TABLE [HangFire].[Server] DROP CONSTRAINT [PK_HangFire_Server]
    PRINT 'Dropped constraint [PK_HangFire_Server] to modify the [HangFire].[Server].[Id] column';

	ALTER TABLE [HangFire].[Server] ALTER COLUMN [Id] NVARCHAR (200) NOT NULL;
	PRINT 'Modified [HangFire].[Server].[Id] length to 200';

	ALTER TABLE [HangFire].[Server] ADD  CONSTRAINT [PK_HangFire_Server] PRIMARY KEY CLUSTERED
	(
		[Id] ASC
	);
	PRINT 'Re-created constraint [PK_HangFire_Server]';

	SET @CURRENT_SCHEMA_VERSION = 5;
END

IF @CURRENT_SCHEMA_VERSION = 5 AND @DISABLE_HEAVY_MIGRATIONS = 1
BEGIN
    PRINT 'Migration process STOPPED at schema version ' + CAST(@CURRENT_SCHEMA_VERSION AS NVARCHAR) +
          '. WILL NOT upgrade to schema version ' + CAST(@TARGET_SCHEMA_VERSION AS NVARCHAR) +
          ', because @DISABLE_HEAVY_MIGRATIONS option is set.';
END
ELSE IF @CURRENT_SCHEMA_VERSION = 5
BEGIN
	PRINT 'Installing schema version 6';

	-- First, we will drop all the secondary indexes on the HangFire.Set table, because we will
	-- modify that table, and unknown indexes may be added there (see https://github.com/HangfireIO/Hangfire/issues/844).
	-- So, we'll drop all of them, and then re-create the required index with a well-known name.

	DECLARE @dropIndexSql NVARCHAR(MAX) = N'';
	SELECT @dropIndexSql += N'DROP INDEX ' + QUOTENAME(SCHEMA_NAME(o.[schema_id])) + '.' + QUOTENAME(o.name) + '.' + QUOTENAME(i.name) + ';'
	FROM sys.indexes AS i
	INNER JOIN sys.tables AS o
	ON i.[object_id] = o.[object_id]
	WHERE i.is_primary_key = 0
	AND i.index_id <> 0
	AND o.is_ms_shipped = 0
	AND SCHEMA_NAME(o.[schema_id]) = 'HangFire'
	AND o.name = 'Set';

	EXEC sp_executesql @dropIndexSql;
	PRINT 'Dropped all secondary indexes on the [Set] table';

	-- Next, we'll remove the unnecessary indexes. They were unnecessary in the previous schema,
	-- and are unnecessary in the new schema as well. We'll not re-create them.

	DROP INDEX [IX_HangFire_Hash_Key] ON [HangFire].[Hash];
	PRINT 'Dropped unnecessary index [IX_HangFire_Hash_Key]';

	-- Next, all the indexes that cover expiration will be filtered, to include only non-null values. This
	-- will prevent unnecessary index modifications – we are seeking these indexes only for non-null
	-- expiration time. Also, they include the Id column by a mistake. So we'll re-create them later in the
	-- migration.

	DROP INDEX [IX_HangFire_Hash_ExpireAt] ON [HangFire].[Hash];
	PRINT 'Dropped index [IX_HangFire_Hash_ExpireAt]';

	DROP INDEX [IX_HangFire_Job_ExpireAt] ON [HangFire].[Job];
	PRINT 'Dropped index [IX_HangFire_Job_ExpireAt]';

	DROP INDEX [IX_HangFire_List_ExpireAt] ON [HangFire].[List];
	PRINT 'Dropped index [IX_HangFire_List_ExpireAt]';

	-- IX_HangFire_Job_StateName index can also be optimized, since we are querying it only with a
	-- non-null state name. This will decrease the number of operations, when creating a background job.
	-- It will be recreated later in the migration.

	DROP INDEX [IX_HangFire_Job_StateName] ON [HangFire].Job;
	PRINT 'Dropped index [IX_HangFire_Job_StateName]';

	-- Dropping foreign key constraints based on the JobId column, because we need to modify the underlying
	-- column type of the clustered index to BIGINT. We'll recreate them later in the migration.

	ALTER TABLE [HangFire].[JobParameter] DROP CONSTRAINT [FK_HangFire_JobParameter_Job];
	PRINT 'Dropped constraint [FK_HangFire_JobParameter_Job]';

	ALTER TABLE [HangFire].[State] DROP CONSTRAINT [FK_HangFire_State_Job];
	PRINT 'Dropped constraint [FK_HangFire_State_Job]';

	-- We are going to create composite clustered indexes that are more natural for the following tables,
	-- so the following indexes will be unnecessary. Natural sorting will keep related data close to each
	-- other, and simplify the index modifications by the cost of fragmentation and additional page splits.

	DROP INDEX [UX_HangFire_CounterAggregated_Key] ON [HangFire].[AggregatedCounter];
	PRINT 'Dropped index [UX_HangFire_CounterAggregated_Key]';

	DROP INDEX [IX_HangFire_Counter_Key] ON [HangFire].[Counter];
	PRINT 'Dropped index [IX_HangFire_Counter_Key]';

	DROP INDEX [IX_HangFire_JobParameter_JobIdAndName] ON [HangFire].[JobParameter];
	PRINT 'Dropped index [IX_HangFire_JobParameter_JobIdAndName]';

	DROP INDEX [IX_HangFire_JobQueue_QueueAndFetchedAt] ON [HangFire].[JobQueue];
	PRINT 'Dropped index [IX_HangFire_JobQueue_QueueAndFetchedAt]';

	DROP INDEX [UX_HangFire_Hash_Key_Field] ON [HangFire].[Hash];
	PRINT 'Dropped index [UX_HangFire_Hash_Key_Field]';

	DROP INDEX [IX_HangFire_List_Key] ON [HangFire].[List];
	PRINT 'Dropped index [IX_HangFire_List_Key]';

	DROP INDEX [IX_HangFire_State_JobId] ON [HangFire].[State];
	PRINT 'Dropped index [IX_HangFire_State_JobId]';

	-- Then, we need to drop the primary key constraints, to modify id columns to the BIGINT type. Some of them
	-- will be re-created later in the migration. But some of them would be removed forever, because their
	-- uniqueness property sometimes unnecessary.

	ALTER TABLE [HangFire].[AggregatedCounter] DROP CONSTRAINT [PK_HangFire_CounterAggregated];
	PRINT 'Dropped constraint [PK_HangFire_CounterAggregated]';

	ALTER TABLE [HangFire].[Counter] DROP CONSTRAINT [PK_HangFire_Counter];
	PRINT 'Dropped constraint [PK_HangFire_Counter]';

	ALTER TABLE [HangFire].[Hash] DROP CONSTRAINT [PK_HangFire_Hash];
	PRINT 'Dropped constraint [PK_HangFire_Hash]';

	ALTER TABLE [HangFire].[Job] DROP CONSTRAINT [PK_HangFire_Job];
	PRINT 'Dropped constraint [PK_HangFire_Job]';

	ALTER TABLE [HangFire].[JobParameter] DROP CONSTRAINT [PK_HangFire_JobParameter];
	PRINT 'Dropped constraint [PK_HangFire_JobParameter]';

	ALTER TABLE [HangFire].[JobQueue] DROP CONSTRAINT [PK_HangFire_JobQueue];
	PRINT 'Dropped constraint [PK_HangFire_JobQueue]';

	ALTER TABLE [HangFire].[List] DROP CONSTRAINT [PK_HangFire_List];
	PRINT 'Dropped constraint [PK_HangFire_List]';

	ALTER TABLE [HangFire].[Set] DROP CONSTRAINT [PK_HangFire_Set];
	PRINT 'Dropped constraint [PK_HangFire_Set]';

	ALTER TABLE [HangFire].[State] DROP CONSTRAINT [PK_HangFire_State];
	PRINT 'Dropped constraint [PK_HangFire_State]';

	-- We are removing identity columns of the following tables completely, their clustered
	-- index will be based on natural values. So, instead of modifying them to BIGINT, we
	-- are dropping them.

	ALTER TABLE [HangFire].[AggregatedCounter] DROP COLUMN [Id];
	PRINT 'Dropped [AggregatedCounter].[Id] column, we will cluster on [Key] column with uniqufier';

	ALTER TABLE [HangFire].[Counter] DROP COLUMN [Id];
	PRINT 'Dropped [Counter].[Id] column, we will cluster on [Key] column';

	ALTER TABLE [HangFire].[Hash] DROP COLUMN [Id];
	PRINT 'Dropped [Hash].[Id] column, we will cluster on [Key]/[Field] columns';

	ALTER TABLE [HangFire].[Set] DROP COLUMN [Id];
	PRINT 'Dropped [Set].[Id] column, we will cluster on [Key]/[Value] columns';

	ALTER TABLE [HangFire].[JobParameter] DROP COLUMN [Id];
	PRINT 'Dropped [JobParameter].[Id] column, we will cluster on [JobId]/[Name] columns';

	-- Then we need to modify all the remaining Id columns to be of type BIGINT.

	ALTER TABLE [HangFire].[List] ALTER COLUMN [Id] BIGINT NOT NULL;
	PRINT 'Modified [List].[Id] type to BIGINT';

	ALTER TABLE [HangFire].[Job] ALTER COLUMN [Id] BIGINT NOT NULL;
	PRINT 'Modified [Job].[Id] type to BIGINT';

	ALTER TABLE [HangFire].[Job] ALTER COLUMN [StateId] BIGINT NULL;
	PRINT 'Modified [Job].[StateId] type to BIGINT';

	ALTER TABLE [HangFire].[JobParameter] ALTER COLUMN [JobId] BIGINT NOT NULL;
	PRINT 'Modified [JobParameter].[JobId] type to BIGINT';

	ALTER TABLE [HangFire].[JobQueue] ALTER COLUMN [JobId] BIGINT NOT NULL;
	PRINT 'Modified [JobQueue].[JobId] type to BIGINT';

	ALTER TABLE [HangFire].[JobQueue] ALTER COLUMN [Id] BIGINT NOT NULL;
	PRINT 'Modified [JobQueue].[Id] type to BIGINT';

	ALTER TABLE [HangFire].[State] ALTER COLUMN [Id] BIGINT NOT NULL;
	PRINT 'Modified [State].[Id] type to BIGINT';

	ALTER TABLE [HangFire].[State] ALTER COLUMN [JobId] BIGINT NOT NULL;
	PRINT 'Modified [State].[JobId] type to BIGINT';

	ALTER TABLE [HangFire].[Counter] ALTER COLUMN [Value] INT NOT NULL;
	PRINT 'Modified [Counter].[Value] type to INT';

	-- Adding back all the Primary Key constraints or clustered indexes where PKs aren't appropriate.

	ALTER TABLE [HangFire].[AggregatedCounter] ADD CONSTRAINT [PK_HangFire_CounterAggregated] PRIMARY KEY CLUSTERED (
		[Key] ASC
	);
	PRINT 'Re-created constraint [PK_HangFire_CounterAggregated]';

	CREATE CLUSTERED INDEX [CX_HangFire_Counter] ON [HangFire].[Counter] ([Key]);
	PRINT 'Created clustered index [CX_HangFire_Counter]';

	ALTER TABLE [HangFire].[Hash] ADD CONSTRAINT [PK_HangFire_Hash] PRIMARY KEY CLUSTERED (
		[Key] ASC,
		[Field] ASC
	);
	PRINT 'Re-created constraint [PK_HangFire_Hash]';

	ALTER TABLE [HangFire].[Job] ADD CONSTRAINT [PK_HangFire_Job] PRIMARY KEY CLUSTERED ([Id] ASC);
	PRINT 'Re-created constraint [PK_HangFire_Job]';
	
	ALTER TABLE [HangFire].[JobParameter] ADD CONSTRAINT [PK_HangFire_JobParameter] PRIMARY KEY CLUSTERED (
		[JobId] ASC,
		[Name] ASC
	);
	PRINT 'Re-created constraint [PK_HangFire_JobParameter]';

	ALTER TABLE [HangFire].[JobQueue] ADD CONSTRAINT [PK_HangFire_JobQueue] PRIMARY KEY CLUSTERED (
		[Queue] ASC,
		[Id] ASC
	);
	PRINT 'Re-created constraint [PK_HangFire_JobQueue]';

	ALTER TABLE [HangFire].[List] ADD CONSTRAINT [PK_HangFire_List] PRIMARY KEY CLUSTERED (
		[Key] ASC,
		[Id] ASC
	);
	PRINT 'Re-created constraint [PK_HangFire_List]';

	ALTER TABLE [HangFire].[Set] ADD CONSTRAINT [PK_HangFire_Set] PRIMARY KEY CLUSTERED (
		[Key] ASC,
		[Value] ASC
	);
	PRINT 'Re-created constraint [PK_HangFire_Set]';

	ALTER TABLE [HangFire].[State] ADD CONSTRAINT [PK_HangFire_State] PRIMARY KEY CLUSTERED (
		[JobId] ASC,
		[Id]
	);
	PRINT 'Re-created constraint [PK_HangFire_State]';

	-- Creating secondary, nonclustered indexes

	CREATE NONCLUSTERED INDEX [IX_HangFire_Job_StateName] ON [HangFire].[Job] ([StateName])
	WHERE [StateName] IS NOT NULL;
	PRINT 'Re-created index [IX_HangFire_Job_StateName]';

	CREATE NONCLUSTERED INDEX [IX_HangFire_Set_Score] ON [HangFire].[Set] ([Score])
	WHERE [Score] IS NOT NULL;
	PRINT 'Created index [IX_HangFire_Set_Score]';

	CREATE NONCLUSTERED INDEX [IX_HangFire_Server_LastHeartbeat] ON [HangFire].[Server] ([LastHeartbeat]);
	PRINT 'Created index [IX_HangFire_Server_LastHeartbeat]';

	-- Creating filtered indexes for ExpireAt columns

	CREATE NONCLUSTERED INDEX [IX_HangFire_AggregatedCounter_ExpireAt] ON [HangFire].[AggregatedCounter] ([ExpireAt])
	WHERE [ExpireAt] IS NOT NULL;
	PRINT 'Created index [IX_HangFire_AggregatedCounter_ExpireAt]';

	CREATE NONCLUSTERED INDEX [IX_HangFire_Hash_ExpireAt] ON [HangFire].[Hash] ([ExpireAt])
	WHERE [ExpireAt] IS NOT NULL;
	PRINT 'Re-created index [IX_HangFire_Hash_ExpireAt]';

	CREATE NONCLUSTERED INDEX [IX_HangFire_Job_ExpireAt] ON [HangFire].[Job] ([ExpireAt])
	INCLUDE ([StateName])
	WHERE [ExpireAt] IS NOT NULL;
	PRINT 'Re-created index [IX_HangFire_Job_ExpireAt]';

	CREATE NONCLUSTERED INDEX [IX_HangFire_List_ExpireAt] ON [HangFire].[List] ([ExpireAt])
	WHERE [ExpireAt] IS NOT NULL;
	PRINT 'Re-created index [IX_HangFire_List_ExpireAt]';

	CREATE NONCLUSTERED INDEX [IX_HangFire_Set_ExpireAt] ON [HangFire].[Set] ([ExpireAt])
	WHERE [ExpireAt] IS NOT NULL;
	PRINT 'Re-created index [IX_HangFire_Set_ExpireAt]';

	-- Restoring foreign keys

	ALTER TABLE [HangFire].[State] ADD CONSTRAINT [FK_HangFire_State_Job] FOREIGN KEY([JobId])
		REFERENCES [HangFire].[Job] ([Id])
		ON UPDATE CASCADE
		ON DELETE CASCADE;
	PRINT 'Re-created constraint [FK_HangFire_State_Job]';

	ALTER TABLE [HangFire].[JobParameter] ADD CONSTRAINT [FK_HangFire_JobParameter_Job] FOREIGN KEY([JobId])
		REFERENCES [HangFire].[Job] ([Id])
		ON UPDATE CASCADE
		ON DELETE CASCADE;
	PRINT 'Re-created constraint [FK_HangFire_JobParameter_Job]';

	SET @CURRENT_SCHEMA_VERSION = 6;
END

IF @CURRENT_SCHEMA_VERSION = 6
BEGIN
	PRINT 'Installing schema version 7';

	DROP INDEX [IX_HangFire_Set_Score] ON [HangFire].[Set];
	PRINT 'Dropped index [IX_HangFire_Set_Score]';

	CREATE NONCLUSTERED INDEX [IX_HangFire_Set_Score] ON [HangFire].[Set] ([Key], [Score]);
	PRINT 'Created index [IX_HangFire_Set_Score] with the proper composite key';

	SET @CURRENT_SCHEMA_VERSION = 7;
END

IF @CURRENT_SCHEMA_VERSION = 7 AND @DISABLE_HEAVY_MIGRATIONS = 1
BEGIN
	PRINT 'Migration process STOPPED at schema version ' + CAST(@CURRENT_SCHEMA_VERSION AS NVARCHAR) +
		'. WILL NOT upgrade to schema version ' + CAST(@TARGET_SCHEMA_VERSION AS NVARCHAR) +
		', because @DISABLE_HEAVY_MIGRATIONS option is set.';
END
ELSE IF @CURRENT_SCHEMA_VERSION = 7
BEGIN
	PRINT 'Installing schema version 8';

	ALTER TABLE [HangFire].[Server] DROP CONSTRAINT [PK_HangFire_Server]
	PRINT 'Dropped constraint [PK_HangFire_Server] to modify the [HangFire].[Server].[Id] column';

	ALTER TABLE [HangFire].[Server] ALTER COLUMN [Id] NVARCHAR (200) NOT NULL;
	PRINT 'Modified [HangFire].[Server].[Id] length to 200';

	ALTER TABLE [HangFire].[Server] ADD  CONSTRAINT [PK_HangFire_Server] PRIMARY KEY CLUSTERED ([Id] ASC);
	PRINT 'Re-created constraint [PK_HangFire_Server]';

	-- Nothing complicated - we just collecting all the secondary indexes and primary key names to delete them.
	-- We should expect nothing here, because custom columns and indexes can be applied for the [Counter] table
	-- to make replication work on Microsoft Azure, like in the issue below.
	-- https://github.com/HangfireIO/Hangfire/issues/1500
	DECLARE @dropIndexSql2 NVARCHAR(MAX) = N'';
	SELECT @dropIndexSql2 += N'DROP INDEX ' + QUOTENAME(SCHEMA_NAME(o.[schema_id])) + '.' + QUOTENAME(o.name) + '.' + QUOTENAME(i.name) + ';'
	FROM sys.indexes AS i
	INNER JOIN sys.tables AS o
	ON i.[object_id] = o.[object_id]
	WHERE i.is_primary_key = 0
	AND i.index_id <> 0
	AND o.is_ms_shipped = 0
	AND SCHEMA_NAME(o.[schema_id]) = 'HangFire'
	AND o.name = 'Counter';

	SELECT @dropIndexSql2 += N'ALTER TABLE' + QUOTENAME(SCHEMA_NAME(o.[schema_id])) + '.' + QUOTENAME(o.name) + ' DROP CONSTRAINT ' + QUOTENAME(c.name) + ';'
	FROM sys.key_constraints c
	INNER JOIN sys.tables AS o
	ON c.[parent_object_id] = o.[object_id]
	WHERE o.is_ms_shipped = 0
	AND SCHEMA_NAME(o.[schema_id]) = 'HangFire'
	AND o.name = 'Counter'

	EXEC sp_executesql @dropIndexSql2;
	PRINT 'Dropped all indexes on the [HangFire].[Counter] table';

	-- [Counter].[Id] column can already be added to make replication work as written above, so we will re-create it
	-- to ensure it is in the expected format.
	PRINT 'Checking for existence of the [HangFire].[Counter].[Id] column';
	IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Counter' AND COLUMN_NAME = 'Id' AND TABLE_SCHEMA='HangFire')
	BEGIN
		ALTER TABLE [HangFire].[Counter] DROP COLUMN [Id];
		PRINT 'Dropped [HangFire].[Counter].[Id] column';
	END

	ALTER TABLE [HangFire].[Counter] ADD [Id] BIGINT IDENTITY(1, 1);
	PRINT 'Created [HangFire].[Counter].[Id] column';

	ALTER TABLE [HangFire].[Counter] ADD  CONSTRAINT [PK_HangFire_Counter] PRIMARY KEY CLUSTERED (
		[Key] ASC,
		[Id] ASC
	);
	PRINT 'Created clustered primary key PK_HangFire_Counter ([Key], [Id])';

	-- SqlServerStorageOptions.UseIgnoreDupKeyOption will yield much better results with INSERT/UPDATE operators
	-- instead of MERGE for [Set] and [Hash] tables. This change is also compatible with older clients, since
	-- MERGE operator is used there, so those clients are forward-compatible with these changes.
	ALTER TABLE [HangFire].[Set] REBUILD WITH (IGNORE_DUP_KEY = ON);
	PRINT 'Enabled IGNORE_DUP_KEY option for [HangFire].[Set] table';

	ALTER TABLE [HangFire].[Hash] REBUILD WITH (IGNORE_DUP_KEY = ON);
	PRINT 'Enabled IGNORE_DUP_KEY option for [HangFire].[Hash] table';

	ALTER TABLE [HangFire].[JobQueue] DROP CONSTRAINT [PK_HangFire_JobQueue];
	PRINT 'Dropped constraint [PK_HangFire_JobQueue] to modify the [HangFire].[JobQueue].[Id] column';

	ALTER TABLE [HangFire].[JobQueue] ALTER COLUMN [Id] BIGINT NOT NULL;
	PRINT 'Changed [HangFire].[JobQueue].[Id] column type to BIGINT';

	ALTER TABLE [HangFire].[JobQueue] ADD CONSTRAINT [PK_HangFire_JobQueue] PRIMARY KEY CLUSTERED (
		[Queue] ASC,
		[Id] ASC
	);
	PRINT 'Re-created constraint [PK_HangFire_JobQueue]';

	SET @CURRENT_SCHEMA_VERSION = 8;
END

IF @CURRENT_SCHEMA_VERSION = 8
BEGIN
	PRINT 'Installing schema version 9';

	CREATE NONCLUSTERED INDEX [IX_HangFire_State_CreatedAt] ON [HangFire].[State] ([CreatedAt] ASC)

	SET @CURRENT_SCHEMA_VERSION = 9;
END

/*IF @CURRENT_SCHEMA_VERSION = 9
BEGIN
	PRINT 'Installing schema version 10';

	 Insert migration here

	SET @CURRENT_SCHEMA_VERSION = 10;
END*/

UPDATE [HangFire].[Schema] SET [Version] = @CURRENT_SCHEMA_VERSION
IF @@ROWCOUNT = 0 
	INSERT INTO [HangFire].[Schema] ([Version]) VALUES (@CURRENT_SCHEMA_VERSION)        

PRINT 'Hangfire database schema installed';

COMMIT TRANSACTION;
PRINT 'Hangfire SQL objects installed';


GO
