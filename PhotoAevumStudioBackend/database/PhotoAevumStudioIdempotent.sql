SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

/* =====================================================================
   1. SEQUENCE GENERATORS (Atomic Custom IDs - Safe Idempotent Blocks)
   ===================================================================== */
IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = N'Seq_CustomerID' AND schema_id = SCHEMA_ID(N'dbo'))
    CREATE SEQUENCE dbo.Seq_CustomerID AS INT START WITH 1 INCREMENT BY 1;

IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = N'Seq_EmployeeID' AND schema_id = SCHEMA_ID(N'dbo'))
    CREATE SEQUENCE dbo.Seq_EmployeeID AS INT START WITH 1 INCREMENT BY 1;

IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = N'Seq_AdminID' AND schema_id = SCHEMA_ID(N'dbo'))
    CREATE SEQUENCE dbo.Seq_AdminID AS INT START WITH 1 INCREMENT BY 1;

IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = N'EquipmentIdSequence' AND schema_id = SCHEMA_ID(N'dbo'))
    CREATE SEQUENCE dbo.EquipmentIdSequence AS INT START WITH 1 INCREMENT BY 1 MINVALUE 1 NO CYCLE CACHE 10;

IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = N'Seq_EquipmentType' AND schema_id = SCHEMA_ID(N'dbo'))
    CREATE SEQUENCE dbo.Seq_EquipmentType AS INT START WITH 1 INCREMENT BY 1;

IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = N'Seq_AdditionalServicesID' AND schema_id = SCHEMA_ID(N'dbo'))
    CREATE SEQUENCE dbo.Seq_AdditionalServicesID AS INT START WITH 1 INCREMENT BY 1;

IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = N'Seq_ComboPackageID' AND schema_id = SCHEMA_ID(N'dbo'))
    CREATE SEQUENCE dbo.Seq_ComboPackageID AS INT START WITH 1 INCREMENT BY 1;

IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = N'Seq_ServicesID' AND schema_id = SCHEMA_ID(N'dbo'))
    CREATE SEQUENCE dbo.Seq_ServicesID AS INT START WITH 1 INCREMENT BY 1;

IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = N'Seq_ServiceCategoryID' AND schema_id = SCHEMA_ID(N'dbo'))
    CREATE SEQUENCE dbo.Seq_ServiceCategoryID AS INT START WITH 1 INCREMENT BY 1;

IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = N'Seq_ServiceRateID' AND schema_id = SCHEMA_ID(N'dbo'))
    CREATE SEQUENCE dbo.Seq_ServiceRateID AS INT START WITH 1 INCREMENT BY 1;

IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = N'Seq_RateTypeID' AND schema_id = SCHEMA_ID(N'dbo'))
    CREATE SEQUENCE dbo.Seq_RateTypeID AS INT START WITH 5 INCREMENT BY 1;

IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = N'Seq_NegotiationID' AND schema_id = SCHEMA_ID(N'dbo'))
    CREATE SEQUENCE dbo.Seq_NegotiationID AS INT START WITH 1 INCREMENT BY 1;

IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = N'Seq_EventAppointmentID' AND schema_id = SCHEMA_ID(N'dbo'))
    CREATE SEQUENCE dbo.Seq_EventAppointmentID AS INT START WITH 1 INCREMENT BY 1;

IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = N'Seq_PaymentTransactionID' AND schema_id = SCHEMA_ID(N'dbo'))
    CREATE SEQUENCE dbo.Seq_PaymentTransactionID AS INT START WITH 1 INCREMENT BY 1;

IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = N'Seq_PortfolioID' AND schema_id = SCHEMA_ID(N'dbo'))
    CREATE SEQUENCE dbo.Seq_PortfolioID AS INT START WITH 1 INCREMENT BY 1;

IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = N'Seq_PortfolioCategoryID' AND schema_id = SCHEMA_ID(N'dbo'))
    CREATE SEQUENCE dbo.Seq_PortfolioCategoryID AS INT START WITH 1 INCREMENT BY 1;
GO

/* =====================================================================
   2. USER AND ACCESS MANAGEMENT TABLES
   ===================================================================== */

IF OBJECT_ID(N'dbo.Admin', N'U') IS NULL
BEGIN
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
END;

IF OBJECT_ID(N'dbo.SystemAdmin', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SystemAdmin (
        SystemAdminID nvarchar(30) NOT NULL PRIMARY KEY,
        FirstName nvarchar(255) NOT NULL,
        LastName nvarchar(255) NOT NULL,
        MiddleName nvarchar(255) NULL,
        PasswordHash nvarchar(255) NOT NULL,
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_SystemAdmin_CreatedAt DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'dbo.EmployeeTimesheets', N'U') IS NULL
BEGIN
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
END;

IF OBJECT_ID(N'dbo.Employees', N'U') IS NULL
BEGIN
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
END;

IF OBJECT_ID(N'dbo.Customer', N'U') IS NULL
BEGIN
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
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Admin_Email' AND object_id = OBJECT_ID(N'dbo.Admin'))
BEGIN
    CREATE UNIQUE INDEX UX_Admin_Email ON dbo.Admin(Email) WHERE Email IS NOT NULL;
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Employee_Email' AND object_id = OBJECT_ID(N'dbo.Employees'))
BEGIN
    CREATE UNIQUE INDEX UX_Employee_Email ON dbo.Employees(EmployeeEmail) WHERE EmployeeEmail IS NOT NULL;
END;
GO

/* =====================================================================
   3. CATALOG AND INVENTORY TABLES
   ===================================================================== */

IF OBJECT_ID(N'dbo.EmployeeSpecialization', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.EmployeeSpecialization (
        SpecializationID nvarchar(30) NOT NULL PRIMARY KEY,
        SpecializationName nvarchar(255) NOT NULL,
        SpecializationDescription nvarchar(1000) NULL,
        Specialization nvarchar(255) NOT NULL,
        Description nvarchar(1000) NULL,
        ColorTag nvarchar(50) NOT NULL CONSTRAINT DF_EmployeeSpecialization_ColorTag DEFAULT 'Blue',
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_EmployeeSpecialization_CreatedAt DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'dbo.EquipmentType', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.EquipmentType (
        EquipmentTypeID nvarchar(30) NOT NULL PRIMARY KEY,
        EquipmentTypeName nvarchar(255)
    );
END;

IF OBJECT_ID(N'dbo.ComboPackage', N'U') IS NULL
BEGIN
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
END;

IF OBJECT_ID(N'dbo.ComboPackage', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.ComboPackage', N'ImageFileName') IS NULL
        ALTER TABLE dbo.ComboPackage ADD ImageFileName nvarchar(500) NULL;
    IF COL_LENGTH(N'dbo.ComboPackage', N'ImageUrl') IS NULL
        ALTER TABLE dbo.ComboPackage ADD ImageUrl nvarchar(500) NULL;
END;

IF OBJECT_ID(N'dbo.AdditionalServices', N'U') IS NULL
BEGIN
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
END;

IF OBJECT_ID(N'dbo.AdditionalServices', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.AdditionalServices', N'ImageFileName') IS NULL
        ALTER TABLE dbo.AdditionalServices ADD ImageFileName nvarchar(500) NULL;
    IF COL_LENGTH(N'dbo.AdditionalServices', N'ImageUrl') IS NULL
        ALTER TABLE dbo.AdditionalServices ADD ImageUrl nvarchar(500) NULL;
END;

IF OBJECT_ID(N'dbo.RateType', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RateType (
        RateTypeID nvarchar(30) NOT NULL PRIMARY KEY,
        RateTypeName nvarchar(50) NOT NULL,
        RateTypeCode nvarchar(30) NOT NULL UNIQUE,
        RateDescription nvarchar(255) NULL,
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_RateType_CreatedAt DEFAULT SYSUTCDATETIME()
    );
END;

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

IF OBJECT_ID(N'dbo.Services', N'U') IS NULL
BEGIN
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
END;

IF OBJECT_ID(N'dbo.Services', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.Services', N'ImageFileName') IS NULL
        ALTER TABLE dbo.Services ADD ImageFileName nvarchar(500) NULL;
    IF COL_LENGTH(N'dbo.Services', N'ImageUrl') IS NULL
        ALTER TABLE dbo.Services ADD ImageUrl nvarchar(500) NULL;
END;

IF OBJECT_ID(N'dbo.ServiceRate', N'U') IS NULL
BEGIN
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
END;

IF OBJECT_ID(N'dbo.ServiceCategory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ServiceCategory ( 
        ServiceCategoryID nvarchar(30) NOT NULL Primary Key,
        ServiceCategoryName nvarchar(255) NOT NULL,
        ServiceCategoryDescription nvarchar(1000) NOT NULL
    );
END;

IF OBJECT_ID(N'dbo.ServiceCategoryJoins', N'U') IS NULL
BEGIN
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
END;

IF OBJECT_ID(N'dbo.Equipment', N'U') IS NULL
BEGIN
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
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Equipment_SerialNo' AND object_id = OBJECT_ID(N'dbo.Equipment'))
BEGIN
    CREATE UNIQUE INDEX UX_Equipment_SerialNo ON dbo.Equipment(SerialNo) WHERE SerialNo IS NOT NULL;
END;
GO

/* =====================================================================
   4. BOOKINGS AND APPOINTMENTS TABLES
   ===================================================================== */

IF OBJECT_ID(N'dbo.EventAppointment', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.EventAppointment (
        EventAppointmentID nvarchar(30) NOT NULL PRIMARY KEY,
        EventName nvarchar(255) NOT NULL CONSTRAINT DF_EventAppointment_Name DEFAULT 'Photoshoot Event',
        EventDescription nvarchar(255) NULL,
        CustomerID nvarchar(30) NOT NULL,
        EventDate date NOT NULL,
        EventTime time NOT NULL,
        EventStatus nvarchar(30) NOT NULL CONSTRAINT DF_EventAppointment_Status DEFAULT 'Pending',
        TotalAmount decimal(18,2) NOT NULL CONSTRAINT DF_EventAppointment_Total DEFAULT 0.00,
        DownpaymentAmount decimal(18,2) NOT NULL CONSTRAINT DF_EventAppointment_Downpayment DEFAULT 0.00,
        FinalBalanceAmount decimal(18,2) NOT NULL CONSTRAINT DF_EventAppointment_Balance DEFAULT 0.00,
        DownpaymentStatus nvarchar(30) NOT NULL CONSTRAINT DF_EventAppointment_DownpaymentStatus DEFAULT 'Pending',
        DownpaymentPaidAt datetime2 NULL,
        HoldExpiresAt datetime2 NULL,
        XenditInvoiceId nvarchar(100) NULL,
        XenditExternalId nvarchar(100) NULL,
        XenditInvoiceUrl nvarchar(500) NULL,
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_EventAppointment_Created DEFAULT SYSUTCDATETIME(),
        RowVer rowversion, 
        CONSTRAINT CK_EventAppointment_Status CHECK (EventStatus IN ('Pending','Awaiting_Downpayment','Confirmed','Shot','Editing','Delivered','Cancelled','Expired'))
    );
END;

IF OBJECT_ID(N'dbo.BookingNegotiation', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BookingNegotiation (
        NegotiationID nvarchar(30) NOT NULL PRIMARY KEY,
        EventAppointmentID nvarchar(30) NULL,
        CustomerID nvarchar(30) NOT NULL,
        EmployeeID nvarchar(30) NOT NULL, 
        ConsultationDate date NULL,
        ConsultationTime time NULL,
        MeetingLink nvarchar(500) NULL,
        InquiryNotes nvarchar(1000) NULL,
        NegotiationStatus nvarchar(50) NOT NULL CONSTRAINT DF_BookingNegotiation_Status DEFAULT 'Pending_Call',
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_BookingNegotiation_Created DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'dbo.EventEmployee', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.EventEmployee (
        EventEmployeeID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EventAppointmentID nvarchar(30) NOT NULL,
        EmployeeID nvarchar(30) NOT NULL,
        IsTeamLeader bit NOT NULL CONSTRAINT DF_EventEmployee_IsTeamLeader DEFAULT 0,
        CONSTRAINT UQ_EventEmployee_Assign UNIQUE (EventAppointmentID, EmployeeID)
    );
END;

IF OBJECT_ID(N'dbo.EventEquipment', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.EventEquipment (
        EventEquipmentID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EventAppointmentID nvarchar(30) NOT NULL,
        EquipmentID nvarchar(30) NOT NULL,
        CONSTRAINT UQ_EventEquipment_Assign UNIQUE (EventAppointmentID, EquipmentID)
    );
END;

IF OBJECT_ID(N'dbo.EventLineItem', N'U') IS NULL
BEGIN
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
END;

IF OBJECT_ID(N'dbo.EmployeeJointSpecialization', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.EmployeeJointSpecialization (
        EmployeeJointSpecializationID nvarchar(30) NOT NULL PRIMARY KEY,
        SpecializationID nvarchar(30) NOT NULL, 
        EmployeeID nvarchar(30) NOT NULL,       
        CONSTRAINT UQ_EmpJointSpec UNIQUE (EmployeeID, SpecializationID)
    );
END;

IF OBJECT_ID(N'dbo.ComboPackageJoin', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ComboPackageJoin (
        ComboPackageJoinID nvarchar(30) NOT NULL PRIMARY KEY,
        ComboPackageID nvarchar(30) NOT NULL,
        AdditionalServicesID nvarchar(30),
        ServicesID nvarchar(30) NULL
    );
END;
GO

/* =====================================================================
   5. AUDIT & HISTORY LOGGING TABLE
   ===================================================================== */

IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NULL
BEGIN
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
END;
GO

/* =====================================================================
   6. OVERTIME CHARGES & PAYMENT TRANSACTIONS TABLES
   ===================================================================== */

IF OBJECT_ID(N'dbo.EventOvertimeCharges', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.EventOvertimeCharges (
        OvertimeChargeID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EventAppointmentID nvarchar(30) NOT NULL,
        ExtraHours decimal(5,2) NOT NULL CONSTRAINT DF_EventOvertime_Hours DEFAULT 0.00,
        HourlyRate decimal(18,2) NOT NULL CONSTRAINT DF_EventOvertime_Rate DEFAULT 0.00,
        SurchargeAmount decimal(18,2) NOT NULL CONSTRAINT DF_EventOvertime_Surcharge DEFAULT 0.00,
        TotalOvertimeFee AS (ExtraHours * HourlyRate + SurchargeAmount) PERSISTED,
        ReasonNotes nvarchar(500) NULL,
        LoggedByEmployeeID nvarchar(30) NOT NULL,
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_EventOvertime_CreatedAt DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'dbo.PaymentTransactions', N'U') IS NULL
BEGIN
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
        PaidAt datetime2 NULL,
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_PaymentTransactions_CreatedAt DEFAULT SYSUTCDATETIME()
    );
END;
GO

/* =====================================================================
   7. PORTFOLIO & SHOWCASE TABLES
   ===================================================================== */

IF OBJECT_ID(N'dbo.PortfolioCategory', N'U') IS NULL
BEGIN
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
END;

IF OBJECT_ID(N'dbo.PortfolioCategoryService', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PortfolioCategoryService (
        CategoryID nvarchar(50) NOT NULL,
        ServiceName nvarchar(120) NOT NULL,
        CONSTRAINT PK_PortfolioCategoryService PRIMARY KEY (CategoryID, ServiceName)
    );
END;

IF OBJECT_ID(N'dbo.Portfolios', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Portfolios (
        PortfolioId nvarchar(50) NOT NULL PRIMARY KEY,
        PortfolioName nvarchar(255) NOT NULL,
        PortfolioDescription nvarchar(1000) NULL,
        CategoryID nvarchar(50) NOT NULL,
        IsFeatured bit NOT NULL CONSTRAINT DF_Portfolios_Featured DEFAULT 0,
        HighlightImageFileName nvarchar(255) NULL,
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_Portfolios_CreatedAt DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'dbo.PortfolioImages', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PortfolioImages (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        PortfolioId nvarchar(50) NOT NULL,
        FileName nvarchar(255) NOT NULL,
        UploadedAt datetime2 NOT NULL CONSTRAINT DF_PortfolioImages_UploadedAt DEFAULT SYSUTCDATETIME()
    );
END;
GO

/* =====================================================================
   8. FOREIGN KEY RELATIONSHIPS (Safe Idempotent Existence Checks)
   ===================================================================== */
IF OBJECT_ID(N'dbo.FK_Equipment_EquipmentType', N'F') IS NULL
    ALTER TABLE dbo.Equipment ADD CONSTRAINT FK_Equipment_EquipmentType FOREIGN KEY (EquipmentTypeID) REFERENCES dbo.EquipmentType(EquipmentTypeID);

IF OBJECT_ID(N'dbo.FK_BookingNegotiation_Employee', N'F') IS NULL
    ALTER TABLE dbo.BookingNegotiation ADD CONSTRAINT FK_BookingNegotiation_Employee FOREIGN KEY (EmployeeID) REFERENCES dbo.Employees(EmployeeID);

IF OBJECT_ID(N'dbo.FK_BookingNegotiation_Customer', N'F') IS NULL
    ALTER TABLE dbo.BookingNegotiation ADD CONSTRAINT FK_BookingNegotiation_Customer FOREIGN KEY (CustomerID) REFERENCES dbo.Customer(CustomerID);

IF OBJECT_ID(N'dbo.FK_BookingNegotiation_EventAppointment', N'F') IS NULL
    ALTER TABLE dbo.BookingNegotiation ADD CONSTRAINT FK_BookingNegotiation_EventAppointment FOREIGN KEY (EventAppointmentID) REFERENCES dbo.EventAppointment(EventAppointmentID) ON DELETE SET NULL;

IF OBJECT_ID(N'dbo.FK_EventEmployee_Employee', N'F') IS NULL
    ALTER TABLE dbo.EventEmployee ADD CONSTRAINT FK_EventEmployee_Employee FOREIGN KEY (EmployeeID) REFERENCES dbo.Employees(EmployeeID);

IF OBJECT_ID(N'dbo.FK_EventEquipment_Equipment', N'F') IS NULL
    ALTER TABLE dbo.EventEquipment ADD CONSTRAINT FK_EventEquipment_Equipment FOREIGN KEY (EquipmentID) REFERENCES dbo.Equipment(EquipmentID);

IF OBJECT_ID(N'dbo.FK_EventAppointment_Customer', N'F') IS NULL
    ALTER TABLE dbo.EventAppointment ADD CONSTRAINT FK_EventAppointment_Customer FOREIGN KEY (CustomerID) REFERENCES dbo.Customer(CustomerID);

IF OBJECT_ID(N'dbo.FK_EventLineItem_AdditionalServices', N'F') IS NULL
    ALTER TABLE dbo.EventLineItem ADD CONSTRAINT FK_EventLineItem_AdditionalServices FOREIGN KEY (AdditionalServicesID) REFERENCES dbo.AdditionalServices(AdditionalServicesID);

IF OBJECT_ID(N'dbo.FK_EventLineItem_Services', N'F') IS NULL
    ALTER TABLE dbo.EventLineItem ADD CONSTRAINT FK_EventLineItem_Services FOREIGN KEY (ServicesID) REFERENCES dbo.Services(ServicesID);

IF OBJECT_ID(N'dbo.FK_EmpJointSpec_Employee', N'F') IS NULL
    ALTER TABLE dbo.EmployeeJointSpecialization ADD CONSTRAINT FK_EmpJointSpec_Employee FOREIGN KEY (EmployeeID) REFERENCES dbo.Employees(EmployeeID) ON DELETE CASCADE;

IF OBJECT_ID(N'dbo.FK_EmpJointSpec_Specialization', N'F') IS NULL
    ALTER TABLE dbo.EmployeeJointSpecialization ADD CONSTRAINT FK_EmpJointSpec_Specialization FOREIGN KEY (SpecializationID) REFERENCES dbo.EmployeeSpecialization(SpecializationID) ON DELETE CASCADE;

IF OBJECT_ID(N'dbo.FK_ComboPackageJoin_Services', N'F') IS NULL
    ALTER TABLE dbo.ComboPackageJoin ADD CONSTRAINT FK_ComboPackageJoin_Services FOREIGN KEY (ServicesID) REFERENCES dbo.Services(ServicesID);

IF OBJECT_ID(N'dbo.FK_ComboPackageJoin_AdditionalServices', N'F') IS NULL
    ALTER TABLE dbo.ComboPackageJoin ADD CONSTRAINT FK_ComboPackageJoin_AdditionalServices FOREIGN KEY (AdditionalServicesID) REFERENCES dbo.AdditionalServices(AdditionalServicesID);

IF OBJECT_ID(N'dbo.FK_ComboPackageJoin_ComboPackage', N'F') IS NULL
    ALTER TABLE dbo.ComboPackageJoin ADD CONSTRAINT FK_ComboPackageJoin_ComboPackage FOREIGN KEY (ComboPackageID) REFERENCES dbo.ComboPackage(ComboPackageID) ON DELETE CASCADE;

IF OBJECT_ID(N'dbo.FK_EventLineItem_EventAppointment', N'F') IS NULL
    ALTER TABLE dbo.EventLineItem ADD CONSTRAINT FK_EventLineItem_EventAppointment FOREIGN KEY (EventAppointmentID) REFERENCES dbo.EventAppointment(EventAppointmentID) ON DELETE CASCADE;

IF OBJECT_ID(N'dbo.FK_EventLineItem_ComboPackage', N'F') IS NULL
    ALTER TABLE dbo.EventLineItem ADD CONSTRAINT FK_EventLineItem_ComboPackage FOREIGN KEY (ComboPackageID) REFERENCES dbo.ComboPackage(ComboPackageID);

IF OBJECT_ID(N'dbo.FK_EventEmployee_EventAppointment', N'F') IS NULL
    ALTER TABLE dbo.EventEmployee ADD CONSTRAINT FK_EventEmployee_EventAppointment FOREIGN KEY (EventAppointmentID) REFERENCES dbo.EventAppointment(EventAppointmentID) ON DELETE CASCADE;

IF OBJECT_ID(N'dbo.FK_EventEquipment_EventAppointment', N'F') IS NULL
    ALTER TABLE dbo.EventEquipment ADD CONSTRAINT FK_EventEquipment_EventAppointment FOREIGN KEY (EventAppointmentID) REFERENCES dbo.EventAppointment(EventAppointmentID) ON DELETE CASCADE;

IF OBJECT_ID(N'dbo.FK_ServiceCategoryJoins_ServiceCategory', N'F') IS NULL
    ALTER TABLE dbo.ServiceCategoryJoins ADD CONSTRAINT FK_ServiceCategoryJoins_ServiceCategory FOREIGN KEY (ServiceCategoryID) REFERENCES dbo.ServiceCategory(ServiceCategoryID) ON DELETE CASCADE;

IF OBJECT_ID(N'dbo.FK_ServiceCategoryJoins_Services', N'F') IS NULL
    ALTER TABLE dbo.ServiceCategoryJoins ADD CONSTRAINT FK_ServiceCategoryJoins_Services FOREIGN KEY (ServicesID) REFERENCES dbo.Services(ServicesID) ON DELETE CASCADE;

IF OBJECT_ID(N'dbo.FK_ServiceCategoryJoins_AdditionalServices', N'F') IS NULL
    ALTER TABLE dbo.ServiceCategoryJoins ADD CONSTRAINT FK_ServiceCategoryJoins_AdditionalServices FOREIGN KEY (AdditionalServicesID) REFERENCES dbo.AdditionalServices(AdditionalServicesID) ON DELETE CASCADE;

IF OBJECT_ID(N'dbo.FK_ServiceCategoryJoins_ComboPackage', N'F') IS NULL
    ALTER TABLE dbo.ServiceCategoryJoins ADD CONSTRAINT FK_ServiceCategoryJoins_ComboPackage FOREIGN KEY (ComboPackageID) REFERENCES dbo.ComboPackage(ComboPackageID) ON DELETE CASCADE;

IF OBJECT_ID(N'dbo.FK_HistoryLog_EventAppointment', N'F') IS NULL
    ALTER TABLE dbo.HistoryLog ADD CONSTRAINT FK_HistoryLog_EventAppointment FOREIGN KEY (EventAppointmentID) REFERENCES dbo.EventAppointment(EventAppointmentID) ON DELETE SET NULL;

IF OBJECT_ID(N'dbo.FK_HistoryLog_BookingNegotiation', N'F') IS NULL
    ALTER TABLE dbo.HistoryLog ADD CONSTRAINT FK_HistoryLog_BookingNegotiation FOREIGN KEY (NegotiationID) REFERENCES dbo.BookingNegotiation(NegotiationID) ON DELETE SET NULL;

IF OBJECT_ID(N'dbo.FK_HistoryLog_Customer', N'F') IS NULL
    ALTER TABLE dbo.HistoryLog ADD CONSTRAINT FK_HistoryLog_Customer FOREIGN KEY (CustomerID) REFERENCES dbo.Customer(CustomerID);

IF OBJECT_ID(N'dbo.FK_HistoryLog_Employee', N'F') IS NULL
    ALTER TABLE dbo.HistoryLog ADD CONSTRAINT FK_HistoryLog_Employee FOREIGN KEY (EmployeeID) REFERENCES dbo.Employees(EmployeeID);

IF OBJECT_ID(N'dbo.FK_HistoryLog_ChangedByCustomer', N'F') IS NULL
    ALTER TABLE dbo.HistoryLog ADD CONSTRAINT FK_HistoryLog_ChangedByCustomer FOREIGN KEY (ChangedByCustomerID) REFERENCES dbo.Customer(CustomerID) ON DELETE SET NULL;

IF OBJECT_ID(N'dbo.FK_HistoryLog_ChangedByEmployee', N'F') IS NULL
    ALTER TABLE dbo.HistoryLog ADD CONSTRAINT FK_HistoryLog_ChangedByEmployee FOREIGN KEY (ChangedByEmployeeID) REFERENCES dbo.Employees(EmployeeID) ON DELETE SET NULL;

IF OBJECT_ID(N'dbo.FK_HistoryLog_ChangedByAdmin', N'F') IS NULL
    ALTER TABLE dbo.HistoryLog ADD CONSTRAINT FK_HistoryLog_ChangedByAdmin FOREIGN KEY (ChangedByAdminID) REFERENCES dbo.Admin(AdminID) ON DELETE SET NULL;

IF OBJECT_ID(N'dbo.FK_ServiceRate_Services', N'F') IS NULL
    ALTER TABLE dbo.ServiceRate ADD CONSTRAINT FK_ServiceRate_Services FOREIGN KEY (ServicesID) REFERENCES dbo.Services(ServicesID) ON DELETE CASCADE;

IF OBJECT_ID(N'dbo.FK_ServiceRate_RateType', N'F') IS NULL
    ALTER TABLE dbo.ServiceRate ADD CONSTRAINT FK_ServiceRate_RateType FOREIGN KEY (RateTypeID) REFERENCES dbo.RateType(RateTypeID);

IF OBJECT_ID(N'dbo.FK_EmployeeTimesheets_Employees', N'F') IS NULL
    ALTER TABLE dbo.EmployeeTimesheets ADD CONSTRAINT FK_EmployeeTimesheets_Employees FOREIGN KEY (EmployeeStudioId) REFERENCES dbo.Employees(EmployeeID) ON DELETE CASCADE;

IF OBJECT_ID(N'dbo.FK_PortfolioCategoryService_Category', N'F') IS NULL
    ALTER TABLE dbo.PortfolioCategoryService ADD CONSTRAINT FK_PortfolioCategoryService_Category FOREIGN KEY (CategoryID) REFERENCES dbo.PortfolioCategory(CategoryID) ON DELETE CASCADE;

IF OBJECT_ID(N'dbo.FK_Portfolios_PortfolioCategory', N'F') IS NULL
    ALTER TABLE dbo.Portfolios ADD CONSTRAINT FK_Portfolios_PortfolioCategory FOREIGN KEY (CategoryID) REFERENCES dbo.PortfolioCategory(CategoryID);

IF OBJECT_ID(N'dbo.FK_PortfolioImages_Portfolios', N'F') IS NULL
    ALTER TABLE dbo.PortfolioImages ADD CONSTRAINT FK_PortfolioImages_Portfolios FOREIGN KEY (PortfolioId) REFERENCES dbo.Portfolios(PortfolioId) ON DELETE CASCADE;

IF OBJECT_ID(N'dbo.FK_EventOvertime_EventAppointment', N'F') IS NULL
    ALTER TABLE dbo.EventOvertimeCharges ADD CONSTRAINT FK_EventOvertime_EventAppointment FOREIGN KEY (EventAppointmentID) REFERENCES dbo.EventAppointment(EventAppointmentID) ON DELETE CASCADE;

IF OBJECT_ID(N'dbo.FK_EventOvertime_Employee', N'F') IS NULL
    ALTER TABLE dbo.EventOvertimeCharges ADD CONSTRAINT FK_EventOvertime_Employee FOREIGN KEY (LoggedByEmployeeID) REFERENCES dbo.Employees(EmployeeID);

IF OBJECT_ID(N'dbo.FK_PaymentTransactions_EventAppointment', N'F') IS NULL
    ALTER TABLE dbo.PaymentTransactions ADD CONSTRAINT FK_PaymentTransactions_EventAppointment FOREIGN KEY (EventAppointmentID) REFERENCES dbo.EventAppointment(EventAppointmentID) ON DELETE CASCADE;

IF OBJECT_ID(N'dbo.FK_PaymentTransactions_Employee', N'F') IS NULL
    ALTER TABLE dbo.PaymentTransactions ADD CONSTRAINT FK_PaymentTransactions_Employee FOREIGN KEY (ReceivedByEmployeeID) REFERENCES dbo.Employees(EmployeeID);
GO

/* =====================================================================
   9. INDEXES FOR COMMON AUDIT, CATALOG, BOOKING & PORTFOLIO LOOKUPS (Safe Idempotent Existence Checks)
   ===================================================================== */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_HistoryLog_EventAppointment' AND object_id = OBJECT_ID(N'dbo.HistoryLog'))
    CREATE INDEX IX_HistoryLog_EventAppointment ON dbo.HistoryLog (EventAppointmentID, LoggedAt DESC);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_HistoryLog_Entity' AND object_id = OBJECT_ID(N'dbo.HistoryLog'))
    CREATE INDEX IX_HistoryLog_Entity           ON dbo.HistoryLog (EntityName, EntityID, LoggedAt DESC);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_HistoryLog_ChangedBy' AND object_id = OBJECT_ID(N'dbo.HistoryLog'))
    CREATE INDEX IX_HistoryLog_ChangedBy        ON dbo.HistoryLog (ChangedBy, LoggedAt DESC);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_HistoryLog_LoggedAt' AND object_id = OBJECT_ID(N'dbo.HistoryLog'))
    CREATE INDEX IX_HistoryLog_LoggedAt         ON dbo.HistoryLog (LoggedAt DESC);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_EventAppointment_Customer' AND object_id = OBJECT_ID(N'dbo.EventAppointment'))
    CREATE INDEX IX_EventAppointment_Customer   ON dbo.EventAppointment (CustomerID, EventDate DESC);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_EventAppointment_Schedule' AND object_id = OBJECT_ID(N'dbo.EventAppointment'))
    CREATE INDEX IX_EventAppointment_Schedule   ON dbo.EventAppointment (EventDate, EventTime);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_EventLineItem_Event' AND object_id = OBJECT_ID(N'dbo.EventLineItem'))
    CREATE INDEX IX_EventLineItem_Event         ON dbo.EventLineItem (EventAppointmentID);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ServiceCategoryJoins_Cat' AND object_id = OBJECT_ID(N'dbo.ServiceCategoryJoins'))
    CREATE INDEX IX_ServiceCategoryJoins_Cat    ON dbo.ServiceCategoryJoins (ServiceCategoryID);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ComboPackageJoin_Package' AND object_id = OBJECT_ID(N'dbo.ComboPackageJoin'))
    CREATE INDEX IX_ComboPackageJoin_Package    ON dbo.ComboPackageJoin (ComboPackageID);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Equipment_EquipmentType' AND object_id = OBJECT_ID(N'dbo.Equipment'))
    CREATE INDEX IX_Equipment_EquipmentType     ON dbo.Equipment (EquipmentTypeID);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_EmployeeTimesheets_EmpDate' AND object_id = OBJECT_ID(N'dbo.EmployeeTimesheets'))
    CREATE INDEX IX_EmployeeTimesheets_EmpDate ON dbo.EmployeeTimesheets (EmployeeStudioId, WorkDate);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_EmployeeTimesheets_ClockIn' AND object_id = OBJECT_ID(N'dbo.EmployeeTimesheets'))
    CREATE INDEX IX_EmployeeTimesheets_ClockIn ON dbo.EmployeeTimesheets (ClockIn DESC);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ServiceRate_Services' AND object_id = OBJECT_ID(N'dbo.ServiceRate'))
    CREATE INDEX IX_ServiceRate_Services       ON dbo.ServiceRate (ServicesID);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ServiceRate_RateType' AND object_id = OBJECT_ID(N'dbo.ServiceRate'))
    CREATE INDEX IX_ServiceRate_RateType       ON dbo.ServiceRate (RateTypeID);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Portfolios_CategoryID' AND object_id = OBJECT_ID(N'dbo.Portfolios'))
    CREATE INDEX IX_Portfolios_CategoryID      ON dbo.Portfolios (CategoryID);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PortfolioImages_PortfolioId' AND object_id = OBJECT_ID(N'dbo.PortfolioImages'))
    CREATE INDEX IX_PortfolioImages_PortfolioId ON dbo.PortfolioImages (PortfolioId);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PaymentTransactions_Event' AND object_id = OBJECT_ID(N'dbo.PaymentTransactions'))
    CREATE INDEX IX_PaymentTransactions_Event  ON dbo.PaymentTransactions (EventAppointmentID, CreatedAt DESC);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_EventOvertime_Event' AND object_id = OBJECT_ID(N'dbo.EventOvertimeCharges'))
    CREATE INDEX IX_EventOvertime_Event        ON dbo.EventOvertimeCharges (EventAppointmentID);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BookingNegotiation_Customer' AND object_id = OBJECT_ID(N'dbo.BookingNegotiation'))
    CREATE INDEX IX_BookingNegotiation_Customer ON dbo.BookingNegotiation (CustomerID, ConsultationDate DESC);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BookingNegotiation_Employee' AND object_id = OBJECT_ID(N'dbo.BookingNegotiation'))
    CREATE INDEX IX_BookingNegotiation_Employee ON dbo.BookingNegotiation (EmployeeID, ConsultationDate DESC);
GO
