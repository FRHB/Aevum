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
IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = N'Seq_DepartmentID' AND schema_id = SCHEMA_ID(N'dbo'))
    CREATE SEQUENCE dbo.Seq_DepartmentID AS INT START WITH 8 INCREMENT BY 1;
IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = N'Seq_HomeHighlightID' AND schema_id = SCHEMA_ID(N'dbo'))
    CREATE SEQUENCE dbo.Seq_HomeHighlightID AS INT START WITH 1 INCREMENT BY 1;
IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = N'Seq_DropboxID' AND schema_id = SCHEMA_ID(N'dbo'))
    CREATE SEQUENCE dbo.Seq_DropboxID AS INT START WITH 1 INCREMENT BY 1;
IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = N'Seq_DropboxFileID' AND schema_id = SCHEMA_ID(N'dbo'))
    CREATE SEQUENCE dbo.Seq_DropboxFileID AS INT START WITH 1 INCREMENT BY 1;
GO

/* =====================================================================
   2. USER AND ACCESS MANAGEMENT TABLES
   ===================================================================== */

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

IF OBJECT_ID(N'dbo.Admin', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Admin (
        AdminID nvarchar(30) NOT NULL PRIMARY KEY,
        AdminRole nvarchar(15) NOT NULL,
        AdminFirstName nvarchar(255) NOT NULL,
        AdminLastName nvarchar(255) NOT NULL,
        AdminMiddleName nvarchar(255),
        Email nvarchar(255) NULL,
        BirthYear int NULL,
        AdminPassword nvarchar(255) NOT NULL,
        isFirstLogin bit NOT NULL CONSTRAINT DF_Admin_isFirstLogin DEFAULT 1,
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_Admin_CreatedAt DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'dbo.Admin', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.Admin', N'BirthYear') IS NULL
        ALTER TABLE dbo.Admin ADD BirthYear int NULL;
END;

IF OBJECT_ID(N'dbo.SystemAdmin', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SystemAdmin (
        SystemAdminID nvarchar(30) NOT NULL PRIMARY KEY,
        FirstName nvarchar(255) NOT NULL,
        LastName nvarchar(255) NOT NULL,
        MiddleName nvarchar(255) NULL,
        Email nvarchar(255) NULL,
        BirthYear int NULL,
        PasswordHash nvarchar(255) NOT NULL,
        isFirstLogin bit NOT NULL CONSTRAINT DF_SystemAdmin_isFirstLogin DEFAULT 1,
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_SystemAdmin_CreatedAt DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'dbo.SystemAdmin', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.SystemAdmin', N'Email') IS NULL
        ALTER TABLE dbo.SystemAdmin ADD Email nvarchar(255) NULL;
    IF COL_LENGTH(N'dbo.SystemAdmin', N'BirthYear') IS NULL
        ALTER TABLE dbo.SystemAdmin ADD BirthYear int NULL;
    IF COL_LENGTH(N'dbo.SystemAdmin', N'isFirstLogin') IS NULL
        ALTER TABLE dbo.SystemAdmin ADD isFirstLogin bit NOT NULL CONSTRAINT DF_SystemAdmin_isFirstLogin DEFAULT 0;
END;

IF OBJECT_ID(N'dbo.EmployeePasswordResetRequest', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.EmployeePasswordResetRequest (
        RequestID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeID nvarchar(30) NOT NULL,
        EmployeeName nvarchar(200) NULL,
        EmployeeEmail nvarchar(255) NULL,
        Reason nvarchar(500) NULL,
        Status nvarchar(30) NOT NULL CONSTRAINT DF_EmpPwdResetReq_Status DEFAULT 'Pending',
        ReviewedByAdminID nvarchar(30) NULL,
        ReviewedByName nvarchar(200) NULL,
        RejectionReason nvarchar(500) NULL,
        RequestedAt datetime2 NOT NULL CONSTRAINT DF_EmpPwdResetReq_RequestedAt DEFAULT SYSUTCDATETIME(),
        ReviewedAt datetime2 NULL
    );
END;

IF OBJECT_ID(N'dbo.EmployeePasswordResetRequest', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.Employees', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.FK_EmpPwdResetReq_Employee', N'F') IS NULL
BEGIN
    ALTER TABLE dbo.EmployeePasswordResetRequest
        ADD CONSTRAINT FK_EmpPwdResetReq_Employee FOREIGN KEY (EmployeeID) REFERENCES dbo.Employees(EmployeeID) ON DELETE CASCADE;
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
        DeletionRequested bit NOT NULL CONSTRAINT DF_Customer_DeletionRequested DEFAULT 0,
        DeletionRequestedAt datetime2 NULL,
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
        DepartmentID nvarchar(30) NULL,
        DepartmentName nvarchar(100) NULL,
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
        RateType nvarchar(50) NOT NULL CONSTRAINT DF_ComboPackage_RateType DEFAULT 'Per Hour',
        ComboPackageImage varbinary(max) NULL,
        ComboPackageImageMimeType nvarchar(100) NULL CONSTRAINT DF_ComboPackage_MimeType DEFAULT 'image/jpeg',
        ImageFileName nvarchar(500) NULL,
        ImageUrl nvarchar(500) NULL,
        IsArchived bit NOT NULL CONSTRAINT DF_ComboPackage_IsArchived DEFAULT 0,
        ArchivedAt datetime2 NULL
    );
END;

IF OBJECT_ID(N'dbo.ComboPackage', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.ComboPackage', N'RateType') IS NULL
        ALTER TABLE dbo.ComboPackage ADD RateType nvarchar(50) NOT NULL CONSTRAINT DF_ComboPackage_RateType DEFAULT 'Per Hour';
    IF COL_LENGTH(N'dbo.ComboPackage', N'ImageFileName') IS NULL
        ALTER TABLE dbo.ComboPackage ADD ImageFileName nvarchar(500) NULL;
    IF COL_LENGTH(N'dbo.ComboPackage', N'ImageUrl') IS NULL
        ALTER TABLE dbo.ComboPackage ADD ImageUrl nvarchar(500) NULL;
    IF COL_LENGTH(N'dbo.ComboPackage', N'IsArchived') IS NULL
        ALTER TABLE dbo.ComboPackage ADD IsArchived bit NOT NULL CONSTRAINT DF_ComboPackage_IsArchived DEFAULT 0;
    IF COL_LENGTH(N'dbo.ComboPackage', N'ArchivedAt') IS NULL
        ALTER TABLE dbo.ComboPackage ADD ArchivedAt datetime2 NULL;
END;

IF OBJECT_ID(N'dbo.AdditionalServices', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AdditionalServices (
        AdditionalServicesID nvarchar(30) NOT NULL PRIMARY KEY,
        AdditionalServiceName nvarchar(255),
        AdditionalServicesDescription nvarchar(1000),
        ServicePrice decimal(18,2) NOT NULL CONSTRAINT DF_AdditionalServices_Price DEFAULT 0.00,
        CategoryName nvarchar(100) NULL,
        RateType nvarchar(50) NOT NULL CONSTRAINT DF_AdditionalServices_RateType DEFAULT 'Per Hour',
        AdditionalServiceImage varbinary(max) NULL,
        AdditionalServiceImageMimeType nvarchar(100) NULL CONSTRAINT DF_AdditionalServices_MimeType DEFAULT 'image/jpeg',
        ImageFileName nvarchar(500) NULL,
        ImageUrl nvarchar(500) NULL,
        IsArchived bit NOT NULL CONSTRAINT DF_AdditionalServices_IsArchived DEFAULT 0,
        ArchivedAt datetime2 NULL,
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_AdditionalServices_CreatedAt DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'dbo.AdditionalServices', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.AdditionalServices', N'RateType') IS NULL
        ALTER TABLE dbo.AdditionalServices ADD RateType nvarchar(50) NOT NULL CONSTRAINT DF_AdditionalServices_RateType DEFAULT 'Per Hour';
    IF COL_LENGTH(N'dbo.AdditionalServices', N'ImageFileName') IS NULL
        ALTER TABLE dbo.AdditionalServices ADD ImageFileName nvarchar(500) NULL;
    IF COL_LENGTH(N'dbo.AdditionalServices', N'ImageUrl') IS NULL
        ALTER TABLE dbo.AdditionalServices ADD ImageUrl nvarchar(500) NULL;
    IF COL_LENGTH(N'dbo.AdditionalServices', N'IsArchived') IS NULL
        ALTER TABLE dbo.AdditionalServices ADD IsArchived bit NOT NULL CONSTRAINT DF_AdditionalServices_IsArchived DEFAULT 0;
    IF COL_LENGTH(N'dbo.AdditionalServices', N'ArchivedAt') IS NULL
        ALTER TABLE dbo.AdditionalServices ADD ArchivedAt datetime2 NULL;
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
        IsArchived bit NOT NULL CONSTRAINT DF_Services_IsArchived DEFAULT 0,
        ArchivedAt datetime2 NULL,
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_Services_CreatedAt DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'dbo.Services', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.Services', N'RateType') IS NULL
        ALTER TABLE dbo.Services ADD RateType nvarchar(50) NOT NULL CONSTRAINT DF_Services_RateType DEFAULT 'Per Hour';
    IF COL_LENGTH(N'dbo.Services', N'ImageFileName') IS NULL
        ALTER TABLE dbo.Services ADD ImageFileName nvarchar(500) NULL;
    IF COL_LENGTH(N'dbo.Services', N'ImageUrl') IS NULL
        ALTER TABLE dbo.Services ADD ImageUrl nvarchar(500) NULL;
    IF COL_LENGTH(N'dbo.Services', N'IsArchived') IS NULL
        ALTER TABLE dbo.Services ADD IsArchived bit NOT NULL CONSTRAINT DF_Services_IsArchived DEFAULT 0;
    IF COL_LENGTH(N'dbo.Services', N'ArchivedAt') IS NULL
        ALTER TABLE dbo.Services ADD ArchivedAt datetime2 NULL;
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
        ServiceCategoryDescription nvarchar(1000) NOT NULL,
        RateTypeID nvarchar(30) NOT NULL CONSTRAINT DF_ServiceCategory_RateTypeID DEFAULT 'RT-0001',
        RateTypeName nvarchar(50) NOT NULL CONSTRAINT DF_ServiceCategory_RateTypeName DEFAULT 'Per Hour',
        Status nvarchar(30) NOT NULL CONSTRAINT DF_ServiceCategory_Status DEFAULT 'Active',
        IsArchived bit NOT NULL CONSTRAINT DF_ServiceCategory_IsArchived DEFAULT 0,
        ArchivedAt datetime2 NULL
    );
END;

IF OBJECT_ID(N'dbo.ServiceCategory', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.ServiceCategory', N'Status') IS NULL
        ALTER TABLE dbo.ServiceCategory ADD Status nvarchar(30) NOT NULL CONSTRAINT DF_ServiceCategory_Status DEFAULT 'Active';
    IF COL_LENGTH(N'dbo.ServiceCategory', N'IsArchived') IS NULL
        ALTER TABLE dbo.ServiceCategory ADD IsArchived bit NOT NULL CONSTRAINT DF_ServiceCategory_IsArchived DEFAULT 0;
    IF COL_LENGTH(N'dbo.ServiceCategory', N'ArchivedAt') IS NULL
        ALTER TABLE dbo.ServiceCategory ADD ArchivedAt datetime2 NULL;
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
        EquipmentStatus nvarchar(30) NOT NULL CONSTRAINT DF_Equipment_Status DEFAULT 'Available',
        EquipmentImage varbinary(max) NULL, 
        ImagePath nvarchar(500) NULL,
        SerialNo nvarchar(30),
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_Equipment_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_Equipment_Status CHECK (EquipmentStatus IN ('Available','Maintenance','Retired','Needs Replacement'))
    );
END;

IF OBJECT_ID(N'dbo.MaintenanceSchedule', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MaintenanceSchedule (
        MaintenanceScheduleID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EquipmentID nvarchar(30) NOT NULL,
        ServiceType nvarchar(100) NOT NULL CONSTRAINT DF_MaintenanceSchedule_ServiceType DEFAULT 'Repair',
        IssueDetails nvarchar(1000) NULL,
        Status nvarchar(50) NOT NULL CONSTRAINT DF_MaintenanceSchedule_Status DEFAULT 'In Maintenance',
        SubStatus nvarchar(50) NULL,
        ResolutionNotes nvarchar(1000) NULL,
        ScheduledDate datetime2 NOT NULL CONSTRAINT DF_MaintenanceSchedule_ScheduledDate DEFAULT SYSUTCDATETIME(),
        UpdatedAt datetime2 NOT NULL CONSTRAINT DF_MaintenanceSchedule_UpdatedAt DEFAULT SYSUTCDATETIME(),
        ResolvedAt datetime2 NULL,
        CreatedByAdminID nvarchar(30) NULL,
        UpdatedByAdminID nvarchar(30) NULL
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
        EventLocation nvarchar(255) NULL,
        CustomerID nvarchar(30) NOT NULL,
        EmployeeID nvarchar(30) NULL,
        AssignedEmployeeName nvarchar(100) NULL,
        EventDate date NOT NULL,
        EventTime time NOT NULL,
        EventEndTime time NULL,
        DurationMinutes int NOT NULL CONSTRAINT DF_EventAppointment_DurationMinutes DEFAULT 60,
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
        FinalPaymentMethod nvarchar(30) NULL,
        FinalPaymentStatus nvarchar(60) NULL,
        FinalQrString nvarchar(1000) NULL,
        FinalPaymentReferenceId nvarchar(100) NULL,
        FinalPaymentPaidAt datetime2 NULL,
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_EventAppointment_Created DEFAULT SYSUTCDATETIME(),
        RowVer rowversion, 
        CONSTRAINT CK_EventAppointment_Status CHECK (EventStatus IN ('Pending','Pending Agreement','Pending Reservation Fee','Awaiting_Downpayment','Pending Payment','Ready','In Execution','In Progress','Preparation','Preparing','Dispatch','Ongoing','On Going','Event Done','Post Event','Awaiting Settlement','Pending Verification','Paid Cash Pending Verification','Payment Verification (Cash)','Confirmed','Complete','Completed','Shot','Editing','Delivered','Cancelled','Expired'))
    );
END;

-- Idempotent column additions for EventAppointment
IF OBJECT_ID(N'dbo.EventAppointment', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.EventAppointment', N'AssignedEmployeeName') IS NULL
        ALTER TABLE dbo.EventAppointment ADD AssignedEmployeeName NVARCHAR(100) NULL;
    IF COL_LENGTH(N'dbo.EventAppointment', N'EventEndTime') IS NULL
        ALTER TABLE dbo.EventAppointment ADD EventEndTime time NULL;
    IF COL_LENGTH(N'dbo.EventAppointment', N'DurationMinutes') IS NULL
        ALTER TABLE dbo.EventAppointment ADD DurationMinutes int NOT NULL CONSTRAINT DF_EventAppointment_DurationMinutes DEFAULT 60;
    IF COL_LENGTH(N'dbo.EventAppointment', N'EventLocation') IS NULL
        ALTER TABLE dbo.EventAppointment ADD EventLocation nvarchar(255) NULL;
END;

IF OBJECT_ID(N'dbo.BookingNegotiation', N'U') IS NULL
BEGIN
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
        -- Supported NegotiationStatus values: 'Pending_Call', 'Pending Payment', 'Appointment Booked', 'Deal_Won', 'Deal_Lost', 'Rescheduled', 'Cancelled'
        NegotiationStatus nvarchar(50) NOT NULL CONSTRAINT DF_BookingNegotiation_Status DEFAULT 'Pending_Call',
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_BookingNegotiation_Created DEFAULT SYSUTCDATETIME()
    );
END;

-- Idempotent column additions for BookingNegotiation
IF OBJECT_ID(N'dbo.BookingNegotiation', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.BookingNegotiation', N'AssignedEmployeeName') IS NULL
        ALTER TABLE dbo.BookingNegotiation ADD AssignedEmployeeName NVARCHAR(100) NULL;
END;

IF OBJECT_ID(N'dbo.EventEmployee', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.EventEmployee (
        EventEmployeeID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EventAppointmentID nvarchar(30) NOT NULL,
        EmployeeID nvarchar(30) NULL,
        AssignedEmployeeName nvarchar(100) NULL,
        IsTeamLeader bit NOT NULL CONSTRAINT DF_EventEmployee_IsTeamLeader DEFAULT 0,
        IsVerified bit NOT NULL CONSTRAINT DF_EventEmployee_IsVerified DEFAULT 0,
        VerifiedAt datetime2 NULL,
        VerifiedByEmployeeID nvarchar(30) NULL,
        DutyStatus nvarchar(30) NOT NULL CONSTRAINT DF_EventEmployee_DutyStatus DEFAULT 'Assigned',
        CONSTRAINT UQ_EventEmployee_Assign UNIQUE (EventAppointmentID, EmployeeID)
    );
END;

-- Idempotent column additions for EventEmployee
IF OBJECT_ID(N'dbo.EventEmployee', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.EventEmployee', N'AssignedEmployeeName') IS NULL
        ALTER TABLE dbo.EventEmployee ADD AssignedEmployeeName NVARCHAR(100) NULL;
    IF COL_LENGTH(N'dbo.EventEmployee', N'IsVerified') IS NULL
        ALTER TABLE dbo.EventEmployee ADD IsVerified bit NOT NULL CONSTRAINT DF_EventEmployee_IsVerified DEFAULT 0;
    IF COL_LENGTH(N'dbo.EventEmployee', N'VerifiedAt') IS NULL
        ALTER TABLE dbo.EventEmployee ADD VerifiedAt datetime2 NULL;
    IF COL_LENGTH(N'dbo.EventEmployee', N'VerifiedByEmployeeID') IS NULL
        ALTER TABLE dbo.EventEmployee ADD VerifiedByEmployeeID nvarchar(30) NULL;
    IF COL_LENGTH(N'dbo.EventEmployee', N'DutyStatus') IS NULL
        ALTER TABLE dbo.EventEmployee ADD DutyStatus nvarchar(30) NOT NULL CONSTRAINT DF_EventEmployee_DutyStatus DEFAULT 'Assigned';
END;

IF OBJECT_ID(N'dbo.EventCrewReplacementRequest', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.EventCrewReplacementRequest (
        RequestID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EventAppointmentID nvarchar(30) NOT NULL,
        AbsentEmployeeID nvarchar(30) NULL,
        AbsentEmployeeName nvarchar(100) NULL,
        RequestedByEmployeeID nvarchar(30) NULL,
        RequestedByName nvarchar(100) NULL,
        Reason nvarchar(500) NULL,
        Status nvarchar(30) NOT NULL CONSTRAINT DF_CrewRepReq_Status DEFAULT 'Pending',
        RequestType nvarchar(30) NOT NULL CONSTRAINT DF_CrewRepReq_RequestType DEFAULT 'Replacement',
        ReplacementEmployeeID nvarchar(30) NULL,
        ReplacementEmployeeName nvarchar(100) NULL,
        FulfilledBy nvarchar(100) NULL,
        FulfilledAt datetime2 NULL,
        RejectionReason nvarchar(500) NULL,
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_CrewRepReq_CreatedAt DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'dbo.EventCrewReplacementRequest', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.EventCrewReplacementRequest', N'RequestType') IS NULL
        ALTER TABLE dbo.EventCrewReplacementRequest ADD RequestType nvarchar(30) NOT NULL CONSTRAINT DF_CrewRepReq_RequestType DEFAULT 'Replacement';
    IF COL_LENGTH(N'dbo.EventCrewReplacementRequest', N'RejectionReason') IS NULL
        ALTER TABLE dbo.EventCrewReplacementRequest ADD RejectionReason nvarchar(500) NULL;
END;

IF OBJECT_ID(N'dbo.EventEquipmentRequest', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.EventEquipmentRequest (
        RequestID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EventAppointmentID nvarchar(30) NOT NULL,
        RequestType nvarchar(30) NOT NULL,
        CurrentEquipmentID nvarchar(30) NULL,
        RequestedEquipmentID nvarchar(30) NULL,
        RequestedByEmployeeID nvarchar(30) NULL,
        RequestedByName nvarchar(100) NULL,
        Reason nvarchar(500) NULL,
        Status nvarchar(30) NOT NULL CONSTRAINT DF_EquipReq_Status DEFAULT 'Pending',
        ReviewedByEmployeeID nvarchar(30) NULL,
        ReviewedByName nvarchar(100) NULL,
        RejectionReason nvarchar(500) NULL,
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_EquipReq_CreatedAt DEFAULT SYSUTCDATETIME(),
        ReviewedAt datetime2 NULL
    );
END;

IF OBJECT_ID(N'dbo.EventEquipmentRequest', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.EventAppointment', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.FK_EventEquipmentRequest_EventAppointment', N'F') IS NULL
BEGIN
    ALTER TABLE dbo.EventEquipmentRequest
        ADD CONSTRAINT FK_EventEquipmentRequest_EventAppointment FOREIGN KEY (EventAppointmentID) REFERENCES dbo.EventAppointment(EventAppointmentID) ON DELETE CASCADE;
END;

IF OBJECT_ID(N'dbo.EventEquipmentRequest', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_EventEquipmentRequest_EventStatus' AND object_id = OBJECT_ID(N'dbo.EventEquipmentRequest'))
BEGIN
    CREATE INDEX IX_EventEquipmentRequest_EventStatus ON dbo.EventEquipmentRequest (EventAppointmentID, Status);
END;

IF OBJECT_ID(N'dbo.EventCrewReplacementRequest', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_EventCrewReplacementRequest_Appt_Status' AND object_id = OBJECT_ID(N'dbo.EventCrewReplacementRequest'))
BEGIN
    CREATE INDEX IX_EventCrewReplacementRequest_Appt_Status ON dbo.EventCrewReplacementRequest (EventAppointmentID, Status);
END;

IF OBJECT_ID(N'dbo.EventEquipment', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.EventEquipment (
        EventEquipmentID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EventAppointmentID nvarchar(30) NOT NULL,
        EquipmentID nvarchar(30) NOT NULL,
        IsTaken bit NOT NULL CONSTRAINT DF_EventEquipment_IsTaken DEFAULT 0,
        TakenAt datetime2 NULL,
        TakenByEmployeeID nvarchar(30) NULL,
        IsAdditionalRequest bit NOT NULL CONSTRAINT DF_EventEquipment_IsAdditionalRequest DEFAULT 0,
        RequestedNotes nvarchar(500) NULL,
        CONSTRAINT UQ_EventEquipment_Assign UNIQUE (EventAppointmentID, EquipmentID)
    );
END;

-- Idempotent column additions for EventEquipment
IF OBJECT_ID(N'dbo.EventEquipment', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.EventEquipment', N'IsTaken') IS NULL
        ALTER TABLE dbo.EventEquipment ADD IsTaken bit NOT NULL CONSTRAINT DF_EventEquipment_IsTaken DEFAULT 0;
    IF COL_LENGTH(N'dbo.EventEquipment', N'TakenAt') IS NULL
        ALTER TABLE dbo.EventEquipment ADD TakenAt datetime2 NULL;
    IF COL_LENGTH(N'dbo.EventEquipment', N'TakenByEmployeeID') IS NULL
        ALTER TABLE dbo.EventEquipment ADD TakenByEmployeeID nvarchar(30) NULL;
    IF COL_LENGTH(N'dbo.EventEquipment', N'IsAdditionalRequest') IS NULL
        ALTER TABLE dbo.EventEquipment ADD IsAdditionalRequest bit NOT NULL CONSTRAINT DF_EventEquipment_IsAdditionalRequest DEFAULT 0;
    IF COL_LENGTH(N'dbo.EventEquipment', N'RequestedNotes') IS NULL
        ALTER TABLE dbo.EventEquipment ADD RequestedNotes nvarchar(500) NULL;
END;

IF OBJECT_ID(N'dbo.EventLineItem', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.EventLineItem (
        EventLineItemID nvarchar(30) NOT NULL PRIMARY KEY,
        EventAppointmentID nvarchar(30) NOT NULL,
        ServicesID nvarchar(30),
        AdditionalServicesID nvarchar(30),
        ComboPackageID nvarchar(30),
        Quantity int NOT NULL CONSTRAINT DF_EventLineItem_Quantity DEFAULT 1,
        UnitPrice decimal(18,2) NOT NULL CONSTRAINT DF_EventLineItem_UnitPrice DEFAULT 0.00,
        LineTotal decimal(18,2) NOT NULL CONSTRAINT DF_EventLineItem_LineTotal DEFAULT 0.00,
        RateType nvarchar(50) NULL,
        DurationHours int NULL,
        ServiceNameSnapshot nvarchar(255) NULL,
        ServiceKindSnapshot nvarchar(30) NULL,
        CONSTRAINT CK_EventLineItem_ExactlyOne CHECK (
            ((CASE WHEN ServicesID IS NOT NULL THEN 1 ELSE 0 END) +
             (CASE WHEN AdditionalServicesID IS NOT NULL THEN 1 ELSE 0 END) +
             (CASE WHEN ComboPackageID IS NOT NULL THEN 1 ELSE 0 END) = 1)
            OR (ServicesID IS NULL AND AdditionalServicesID IS NULL AND ComboPackageID IS NULL AND ServiceNameSnapshot IS NOT NULL))
    );
END;

-- Idempotent column additions for EventLineItem
IF OBJECT_ID(N'dbo.EventLineItem', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.EventLineItem', N'Quantity') IS NULL
        ALTER TABLE dbo.EventLineItem ADD Quantity int NOT NULL CONSTRAINT DF_EventLineItem_Quantity DEFAULT 1;
    IF COL_LENGTH(N'dbo.EventLineItem', N'UnitPrice') IS NULL
        ALTER TABLE dbo.EventLineItem ADD UnitPrice decimal(18,2) NOT NULL CONSTRAINT DF_EventLineItem_UnitPrice DEFAULT 0.00;
    IF COL_LENGTH(N'dbo.EventLineItem', N'LineTotal') IS NULL
        ALTER TABLE dbo.EventLineItem ADD LineTotal decimal(18,2) NOT NULL CONSTRAINT DF_EventLineItem_LineTotal DEFAULT 0.00;
    IF COL_LENGTH(N'dbo.EventLineItem', N'RateType') IS NULL
        ALTER TABLE dbo.EventLineItem ADD RateType nvarchar(50) NULL;
    IF COL_LENGTH(N'dbo.EventLineItem', N'DurationHours') IS NULL
        ALTER TABLE dbo.EventLineItem ADD DurationHours int NULL;
    IF COL_LENGTH(N'dbo.EventLineItem', N'ServiceNameSnapshot') IS NULL
        ALTER TABLE dbo.EventLineItem ADD ServiceNameSnapshot nvarchar(255) NULL;
    IF COL_LENGTH(N'dbo.EventLineItem', N'ServiceKindSnapshot') IS NULL
        ALTER TABLE dbo.EventLineItem ADD ServiceKindSnapshot nvarchar(30) NULL;

    IF OBJECT_ID(N'dbo.CK_EventLineItem_ExactlyOne', N'C') IS NOT NULL
        ALTER TABLE dbo.EventLineItem DROP CONSTRAINT CK_EventLineItem_ExactlyOne;

    EXEC(N'
        ALTER TABLE dbo.EventLineItem ADD CONSTRAINT CK_EventLineItem_ExactlyOne CHECK (
            ((CASE WHEN ServicesID IS NOT NULL THEN 1 ELSE 0 END) +
             (CASE WHEN AdditionalServicesID IS NOT NULL THEN 1 ELSE 0 END) +
             (CASE WHEN ComboPackageID IS NOT NULL THEN 1 ELSE 0 END) = 1)
            OR (ServicesID IS NULL AND AdditionalServicesID IS NULL AND ComboPackageID IS NULL AND ServiceNameSnapshot IS NOT NULL)
        );
    ');
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

IF OBJECT_ID(N'dbo.ArchiveIndex', N'U') IS NULL
BEGIN
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
END;

IF OBJECT_ID(N'dbo.StudioAnalyticsActions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.StudioAnalyticsActions (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_StudioAnalyticsActions PRIMARY KEY,
        ActionKey nvarchar(120) NOT NULL,
        Title nvarchar(255) NOT NULL,
        Category nvarchar(80) NOT NULL,
        EffortTag nvarchar(50) NOT NULL,
        EstimatedImpact nvarchar(255) NOT NULL,
        TriggerSignal nvarchar(500) NULL,
        RelatedReferences nvarchar(1000) NULL,
        Notes nvarchar(1000) NULL,
        AppliedByUserId nvarchar(100) NOT NULL,
        AppliedByName nvarchar(200) NOT NULL,
        AppliedAtUtc datetime2 NOT NULL CONSTRAINT DF_StudioAnalyticsActions_AppliedAtUtc DEFAULT SYSUTCDATETIME(),
        Status nvarchar(40) NOT NULL CONSTRAINT DF_StudioAnalyticsActions_Status DEFAULT 'Applied'
    );
    CREATE INDEX IX_StudioAnalyticsActions_Key_Date ON dbo.StudioAnalyticsActions (ActionKey, AppliedAtUtc DESC);
END;
GO

IF OBJECT_ID(N'dbo.StudioFAQs', N'U') IS NULL
BEGIN
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
        Notes nvarchar(500) NULL,
        LoggedByEmployeeID nvarchar(30) NULL,
        AssignedEmployeeName nvarchar(100) NULL,
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_EventOvertime_CreatedAt DEFAULT SYSUTCDATETIME()
    );
END;

-- Idempotent column additions for EventOvertimeCharges
IF OBJECT_ID(N'dbo.EventOvertimeCharges', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.EventOvertimeCharges', N'AssignedEmployeeName') IS NULL
        ALTER TABLE dbo.EventOvertimeCharges ADD AssignedEmployeeName NVARCHAR(100) NULL;

    IF COL_LENGTH(N'dbo.EventOvertimeCharges', N'Notes') IS NULL
    BEGIN
        ALTER TABLE dbo.EventOvertimeCharges ADD Notes NVARCHAR(500) NULL;
        IF COL_LENGTH(N'dbo.EventOvertimeCharges', N'ReasonNotes') IS NOT NULL
            EXEC(N'UPDATE dbo.EventOvertimeCharges SET Notes = ReasonNotes WHERE Notes IS NULL AND ReasonNotes IS NOT NULL;');
    END;
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
        AssignedEmployeeName nvarchar(100) NULL,
        PaidAt datetime2 NULL,
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_PaymentTransactions_CreatedAt DEFAULT SYSUTCDATETIME()
    );
END;

-- Idempotent column additions for PaymentTransactions
IF OBJECT_ID(N'dbo.PaymentTransactions', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.PaymentTransactions', N'AssignedEmployeeName') IS NULL
        ALTER TABLE dbo.PaymentTransactions ADD AssignedEmployeeName NVARCHAR(100) NULL;
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
        FileName nvarchar(500) NOT NULL,
        ImageUrl nvarchar(500) NULL,
        ContentType nvarchar(100) NULL CONSTRAINT DF_PortfolioImages_ContentType DEFAULT 'image/jpeg',
        ImageData varbinary(max) NULL,
        UploadedAt datetime2 NOT NULL CONSTRAINT DF_PortfolioImages_UploadedAt DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'dbo.PortfolioImages', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.PortfolioImages', N'ImageUrl') IS NULL
        ALTER TABLE dbo.PortfolioImages ADD ImageUrl nvarchar(500) NULL;
    IF COL_LENGTH(N'dbo.PortfolioImages', N'ContentType') IS NULL
        ALTER TABLE dbo.PortfolioImages ADD ContentType nvarchar(100) NULL CONSTRAINT DF_PortfolioImages_ContentType DEFAULT 'image/jpeg';
    IF COL_LENGTH(N'dbo.PortfolioImages', N'ImageData') IS NULL
        ALTER TABLE dbo.PortfolioImages ADD ImageData varbinary(max) NULL;
END;

IF OBJECT_ID(N'dbo.HomeHighlights', N'U') IS NULL
BEGIN
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
END;

IF OBJECT_ID(N'dbo.CustomerDropbox', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerDropbox (
        DropboxID nvarchar(30) NOT NULL PRIMARY KEY,
        EventAppointmentID nvarchar(30) NOT NULL,
        CustomerID nvarchar(30) NOT NULL,
        BucketName nvarchar(100) NOT NULL CONSTRAINT DF_CustomerDropbox_Bucket DEFAULT 'dropbox',
        R2ObjectKey nvarchar(500) NULL,
        OriginalFileName nvarchar(255) NULL,
        FileSizeBytes bigint NULL,
        ContentType nvarchar(100) NULL,
        DropboxStatus nvarchar(30) NOT NULL CONSTRAINT DF_CustomerDropbox_Status DEFAULT 'Waiting',
        EventCompletedAt datetime2 NOT NULL CONSTRAINT DF_CustomerDropbox_CompletedAt DEFAULT SYSUTCDATETIME(),
        UploadedAt datetime2 NULL,
        UploadedByEmployeeID nvarchar(30) NULL,
        ExpiresAt datetime2 NULL,
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_CustomerDropbox_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_CustomerDropbox_Event UNIQUE (EventAppointmentID),
        CONSTRAINT CK_CustomerDropbox_Status CHECK (DropboxStatus IN ('Waiting', 'Uploaded', 'Expired'))
    );
END;

IF OBJECT_ID(N'dbo.CustomerDropbox', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.CustomerDropbox', N'ExpiresAt') IS NOT NULL
       AND COLUMNPROPERTY(OBJECT_ID(N'dbo.CustomerDropbox'), N'ExpiresAt', N'AllowsNull') = 0
    BEGIN
        ALTER TABLE dbo.CustomerDropbox ALTER COLUMN ExpiresAt datetime2 NULL;
    END;

    UPDATE dbo.CustomerDropbox
    SET ExpiresAt = NULL
    WHERE DropboxStatus = 'Waiting'
      AND UploadedAt IS NULL
      AND ExpiresAt IS NOT NULL;

    UPDATE dbo.CustomerDropbox
    SET ExpiresAt = DATEADD(day, 30, UploadedAt)
    WHERE DropboxStatus = 'Uploaded'
      AND UploadedAt IS NOT NULL
      AND (ExpiresAt IS NULL OR ExpiresAt < DATEADD(day, 30, UploadedAt));
END;

IF OBJECT_ID(N'dbo.CustomerDropboxFiles', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerDropboxFiles (
        DropboxFileID nvarchar(30) NOT NULL PRIMARY KEY,
        DropboxID nvarchar(30) NOT NULL,
        EventAppointmentID nvarchar(30) NOT NULL,
        CustomerID nvarchar(30) NOT NULL,
        BucketName nvarchar(100) NOT NULL CONSTRAINT DF_CustomerDropboxFiles_Bucket DEFAULT 'dropbox',
        R2ObjectKey nvarchar(500) NULL,
        OriginalFileName nvarchar(255) NOT NULL,
        FileSizeBytes bigint NOT NULL,
        ContentType nvarchar(100) NULL,
        FileStatus nvarchar(30) NOT NULL CONSTRAINT DF_CustomerDropboxFiles_Status DEFAULT 'Active',
        UploadedAt datetime2 NOT NULL CONSTRAINT DF_CustomerDropboxFiles_UploadedAt DEFAULT SYSUTCDATETIME(),
        UploadedByEmployeeID nvarchar(30) NULL,
        ExpiresAt datetime2 NOT NULL,
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_CustomerDropboxFiles_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_CustomerDropboxFiles_Status CHECK (FileStatus IN ('Active', 'Expired')),
        CONSTRAINT CK_CustomerDropboxFiles_Size CHECK (FileSizeBytes > 0 AND FileSizeBytes <= 524288000)
    );
END;

IF OBJECT_ID(N'dbo.CustomerDropbox', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.CustomerDropboxFiles', N'U') IS NOT NULL
BEGIN
    INSERT INTO dbo.CustomerDropboxFiles (
        DropboxFileID, DropboxID, EventAppointmentID, CustomerID, BucketName,
        R2ObjectKey, OriginalFileName, FileSizeBytes, ContentType, FileStatus,
        UploadedAt, UploadedByEmployeeID, ExpiresAt, CreatedAt
    )
    SELECT
        N'DRPF-' + RIGHT(N'0000' + CAST(NEXT VALUE FOR dbo.Seq_DropboxFileID AS nvarchar(10)), 4),
        cd.DropboxID,
        cd.EventAppointmentID,
        cd.CustomerID,
        cd.BucketName,
        cd.R2ObjectKey,
        COALESCE(cd.OriginalFileName, cd.EventAppointmentID + N'.zip'),
        CASE WHEN ISNULL(cd.FileSizeBytes, 0) > 0 AND cd.FileSizeBytes <= 524288000 THEN cd.FileSizeBytes ELSE 1 END,
        cd.ContentType,
        CASE WHEN cd.DropboxStatus = N'Expired' THEN N'Expired' ELSE N'Active' END,
        COALESCE(cd.UploadedAt, cd.CreatedAt, SYSUTCDATETIME()),
        cd.UploadedByEmployeeID,
        COALESCE(cd.ExpiresAt, DATEADD(day, 30, COALESCE(cd.UploadedAt, cd.CreatedAt, SYSUTCDATETIME()))),
        COALESCE(cd.CreatedAt, SYSUTCDATETIME())
    FROM dbo.CustomerDropbox cd
    WHERE cd.DropboxStatus IN (N'Uploaded', N'Expired')
      AND (cd.R2ObjectKey IS NOT NULL OR cd.OriginalFileName IS NOT NULL)
      AND NOT EXISTS (
          SELECT 1 FROM dbo.CustomerDropboxFiles cdf WHERE cdf.DropboxID = cd.DropboxID
      );
END;
GO

/* =====================================================================
   8. FOREIGN KEY RELATIONSHIPS (Safe Idempotent Drop-and-Recreate)
   ===================================================================== */

-- 8.1 Equipment Constraints
IF OBJECT_ID(N'dbo.FK_Equipment_EquipmentType', N'F') IS NOT NULL
    ALTER TABLE dbo.Equipment DROP CONSTRAINT FK_Equipment_EquipmentType;
ALTER TABLE dbo.Equipment ADD CONSTRAINT FK_Equipment_EquipmentType FOREIGN KEY (EquipmentTypeID) REFERENCES dbo.EquipmentType(EquipmentTypeID);

-- 8.2 BookingNegotiation Constraints
IF COL_LENGTH(N'dbo.BookingNegotiation', N'EmployeeID') IS NOT NULL
    ALTER TABLE dbo.BookingNegotiation ALTER COLUMN EmployeeID NVARCHAR(30) NULL;

IF OBJECT_ID(N'dbo.FK_BookingNegotiation_Employee', N'F') IS NOT NULL
    ALTER TABLE dbo.BookingNegotiation DROP CONSTRAINT FK_BookingNegotiation_Employee;
ALTER TABLE dbo.BookingNegotiation ADD CONSTRAINT FK_BookingNegotiation_Employee FOREIGN KEY (EmployeeID) REFERENCES dbo.Employees(EmployeeID) ON DELETE SET NULL;

IF OBJECT_ID(N'dbo.FK_BookingNegotiation_Customer', N'F') IS NOT NULL
    ALTER TABLE dbo.BookingNegotiation DROP CONSTRAINT FK_BookingNegotiation_Customer;
ALTER TABLE dbo.BookingNegotiation ADD CONSTRAINT FK_BookingNegotiation_Customer FOREIGN KEY (CustomerID) REFERENCES dbo.Customer(CustomerID);

IF OBJECT_ID(N'dbo.FK_BookingNegotiation_EventAppointment', N'F') IS NOT NULL
    ALTER TABLE dbo.BookingNegotiation DROP CONSTRAINT FK_BookingNegotiation_EventAppointment;
ALTER TABLE dbo.BookingNegotiation ADD CONSTRAINT FK_BookingNegotiation_EventAppointment FOREIGN KEY (EventAppointmentID) REFERENCES dbo.EventAppointment(EventAppointmentID) ON DELETE SET NULL;

-- 8.3 EventEmployee Constraints
IF COL_LENGTH(N'dbo.EventEmployee', N'EmployeeID') IS NOT NULL
    ALTER TABLE dbo.EventEmployee ALTER COLUMN EmployeeID NVARCHAR(30) NULL;

IF OBJECT_ID(N'dbo.FK_EventEmployee_Employee', N'F') IS NOT NULL
    ALTER TABLE dbo.EventEmployee DROP CONSTRAINT FK_EventEmployee_Employee;
ALTER TABLE dbo.EventEmployee ADD CONSTRAINT FK_EventEmployee_Employee FOREIGN KEY (EmployeeID) REFERENCES dbo.Employees(EmployeeID) ON DELETE SET NULL;

IF OBJECT_ID(N'dbo.FK_EventEmployee_EventAppointment', N'F') IS NOT NULL
    ALTER TABLE dbo.EventEmployee DROP CONSTRAINT FK_EventEmployee_EventAppointment;
ALTER TABLE dbo.EventEmployee ADD CONSTRAINT FK_EventEmployee_EventAppointment FOREIGN KEY (EventAppointmentID) REFERENCES dbo.EventAppointment(EventAppointmentID) ON DELETE CASCADE;

IF OBJECT_ID(N'dbo.EventCrewReplacementRequest', N'U') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.FK_EventCrewReplacementRequest_Appointment', N'F') IS NOT NULL
        ALTER TABLE dbo.EventCrewReplacementRequest DROP CONSTRAINT FK_EventCrewReplacementRequest_Appointment;
    ALTER TABLE dbo.EventCrewReplacementRequest ADD CONSTRAINT FK_EventCrewReplacementRequest_Appointment FOREIGN KEY (EventAppointmentID) REFERENCES dbo.EventAppointment(EventAppointmentID) ON DELETE CASCADE;

    IF OBJECT_ID(N'dbo.FK_EventCrewReplacementRequest_AbsentEmployee', N'F') IS NOT NULL
        ALTER TABLE dbo.EventCrewReplacementRequest DROP CONSTRAINT FK_EventCrewReplacementRequest_AbsentEmployee;
    ALTER TABLE dbo.EventCrewReplacementRequest ADD CONSTRAINT FK_EventCrewReplacementRequest_AbsentEmployee FOREIGN KEY (AbsentEmployeeID) REFERENCES dbo.Employees(EmployeeID) ON DELETE NO ACTION;

    IF OBJECT_ID(N'dbo.FK_EventCrewReplacementRequest_ReplacementEmployee', N'F') IS NOT NULL
        ALTER TABLE dbo.EventCrewReplacementRequest DROP CONSTRAINT FK_EventCrewReplacementRequest_ReplacementEmployee;
    ALTER TABLE dbo.EventCrewReplacementRequest ADD CONSTRAINT FK_EventCrewReplacementRequest_ReplacementEmployee FOREIGN KEY (ReplacementEmployeeID) REFERENCES dbo.Employees(EmployeeID) ON DELETE NO ACTION;
END;

-- 8.4 EventAppointment Constraints
IF COL_LENGTH(N'dbo.EventAppointment', N'EmployeeID') IS NOT NULL
    ALTER TABLE dbo.EventAppointment ALTER COLUMN EmployeeID NVARCHAR(30) NULL;

IF OBJECT_ID(N'dbo.FK_EventAppointment_Customer', N'F') IS NOT NULL
    ALTER TABLE dbo.EventAppointment DROP CONSTRAINT FK_EventAppointment_Customer;
ALTER TABLE dbo.EventAppointment ADD CONSTRAINT FK_EventAppointment_Customer FOREIGN KEY (CustomerID) REFERENCES dbo.Customer(CustomerID);

IF OBJECT_ID(N'dbo.FK_EventAppointment_Employee', N'F') IS NOT NULL
    ALTER TABLE dbo.EventAppointment DROP CONSTRAINT FK_EventAppointment_Employee;
ALTER TABLE dbo.EventAppointment ADD CONSTRAINT FK_EventAppointment_Employee FOREIGN KEY (EmployeeID) REFERENCES dbo.Employees(EmployeeID) ON DELETE SET NULL;

-- 8.5 EventEquipment Constraints
IF OBJECT_ID(N'dbo.FK_EventEquipment_Equipment', N'F') IS NOT NULL
    ALTER TABLE dbo.EventEquipment DROP CONSTRAINT FK_EventEquipment_Equipment;
ALTER TABLE dbo.EventEquipment ADD CONSTRAINT FK_EventEquipment_Equipment FOREIGN KEY (EquipmentID) REFERENCES dbo.Equipment(EquipmentID);

IF OBJECT_ID(N'dbo.FK_EventEquipment_EventAppointment', N'F') IS NOT NULL
    ALTER TABLE dbo.EventEquipment DROP CONSTRAINT FK_EventEquipment_EventAppointment;
ALTER TABLE dbo.EventEquipment ADD CONSTRAINT FK_EventEquipment_EventAppointment FOREIGN KEY (EventAppointmentID) REFERENCES dbo.EventAppointment(EventAppointmentID) ON DELETE CASCADE;

-- 8.6 EventLineItem Constraints
IF OBJECT_ID(N'dbo.FK_EventLineItem_AdditionalServices', N'F') IS NOT NULL
    ALTER TABLE dbo.EventLineItem DROP CONSTRAINT FK_EventLineItem_AdditionalServices;
ALTER TABLE dbo.EventLineItem ADD CONSTRAINT FK_EventLineItem_AdditionalServices FOREIGN KEY (AdditionalServicesID) REFERENCES dbo.AdditionalServices(AdditionalServicesID);

IF OBJECT_ID(N'dbo.FK_EventLineItem_Services', N'F') IS NOT NULL
    ALTER TABLE dbo.EventLineItem DROP CONSTRAINT FK_EventLineItem_Services;
ALTER TABLE dbo.EventLineItem ADD CONSTRAINT FK_EventLineItem_Services FOREIGN KEY (ServicesID) REFERENCES dbo.Services(ServicesID);

IF OBJECT_ID(N'dbo.FK_EventLineItem_EventAppointment', N'F') IS NOT NULL
    ALTER TABLE dbo.EventLineItem DROP CONSTRAINT FK_EventLineItem_EventAppointment;
ALTER TABLE dbo.EventLineItem ADD CONSTRAINT FK_EventLineItem_EventAppointment FOREIGN KEY (EventAppointmentID) REFERENCES dbo.EventAppointment(EventAppointmentID) ON DELETE CASCADE;

IF OBJECT_ID(N'dbo.FK_EventLineItem_ComboPackage', N'F') IS NOT NULL
    ALTER TABLE dbo.EventLineItem DROP CONSTRAINT FK_EventLineItem_ComboPackage;
ALTER TABLE dbo.EventLineItem ADD CONSTRAINT FK_EventLineItem_ComboPackage FOREIGN KEY (ComboPackageID) REFERENCES dbo.ComboPackage(ComboPackageID);

-- 8.7 Employee Specialization Constraints
IF OBJECT_ID(N'dbo.FK_EmpJointSpec_Employee', N'F') IS NOT NULL
    ALTER TABLE dbo.EmployeeJointSpecialization DROP CONSTRAINT FK_EmpJointSpec_Employee;
ALTER TABLE dbo.EmployeeJointSpecialization ADD CONSTRAINT FK_EmpJointSpec_Employee FOREIGN KEY (EmployeeID) REFERENCES dbo.Employees(EmployeeID) ON DELETE CASCADE;

IF OBJECT_ID(N'dbo.FK_EmpJointSpec_Specialization', N'F') IS NOT NULL
    ALTER TABLE dbo.EmployeeJointSpecialization DROP CONSTRAINT FK_EmpJointSpec_Specialization;
ALTER TABLE dbo.EmployeeJointSpecialization ADD CONSTRAINT FK_EmpJointSpec_Specialization FOREIGN KEY (SpecializationID) REFERENCES dbo.EmployeeSpecialization(SpecializationID) ON DELETE CASCADE;

-- 8.8 ComboPackageJoin Constraints
IF OBJECT_ID(N'dbo.FK_ComboPackageJoin_Services', N'F') IS NOT NULL
    ALTER TABLE dbo.ComboPackageJoin DROP CONSTRAINT FK_ComboPackageJoin_Services;
ALTER TABLE dbo.ComboPackageJoin ADD CONSTRAINT FK_ComboPackageJoin_Services FOREIGN KEY (ServicesID) REFERENCES dbo.Services(ServicesID);

IF OBJECT_ID(N'dbo.FK_ComboPackageJoin_AdditionalServices', N'F') IS NOT NULL
    ALTER TABLE dbo.ComboPackageJoin DROP CONSTRAINT FK_ComboPackageJoin_AdditionalServices;
ALTER TABLE dbo.ComboPackageJoin ADD CONSTRAINT FK_ComboPackageJoin_AdditionalServices FOREIGN KEY (AdditionalServicesID) REFERENCES dbo.AdditionalServices(AdditionalServicesID);

IF OBJECT_ID(N'dbo.FK_ComboPackageJoin_ComboPackage', N'F') IS NOT NULL
    ALTER TABLE dbo.ComboPackageJoin DROP CONSTRAINT FK_ComboPackageJoin_ComboPackage;
ALTER TABLE dbo.ComboPackageJoin ADD CONSTRAINT FK_ComboPackageJoin_ComboPackage FOREIGN KEY (ComboPackageID) REFERENCES dbo.ComboPackage(ComboPackageID) ON DELETE CASCADE;

-- 8.9 ServiceCategoryJoins Constraints
IF OBJECT_ID(N'dbo.FK_ServiceCategoryJoins_ServiceCategory', N'F') IS NOT NULL
    ALTER TABLE dbo.ServiceCategoryJoins DROP CONSTRAINT FK_ServiceCategoryJoins_ServiceCategory;
ALTER TABLE dbo.ServiceCategoryJoins ADD CONSTRAINT FK_ServiceCategoryJoins_ServiceCategory FOREIGN KEY (ServiceCategoryID) REFERENCES dbo.ServiceCategory(ServiceCategoryID) ON DELETE CASCADE;

IF OBJECT_ID(N'dbo.FK_ServiceCategoryJoins_Services', N'F') IS NOT NULL
    ALTER TABLE dbo.ServiceCategoryJoins DROP CONSTRAINT FK_ServiceCategoryJoins_Services;
ALTER TABLE dbo.ServiceCategoryJoins ADD CONSTRAINT FK_ServiceCategoryJoins_Services FOREIGN KEY (ServicesID) REFERENCES dbo.Services(ServicesID) ON DELETE CASCADE;

IF OBJECT_ID(N'dbo.FK_ServiceCategoryJoins_AdditionalServices', N'F') IS NOT NULL
    ALTER TABLE dbo.ServiceCategoryJoins DROP CONSTRAINT FK_ServiceCategoryJoins_AdditionalServices;
ALTER TABLE dbo.ServiceCategoryJoins ADD CONSTRAINT FK_ServiceCategoryJoins_AdditionalServices FOREIGN KEY (AdditionalServicesID) REFERENCES dbo.AdditionalServices(AdditionalServicesID) ON DELETE CASCADE;

IF OBJECT_ID(N'dbo.FK_ServiceCategoryJoins_ComboPackage', N'F') IS NOT NULL
    ALTER TABLE dbo.ServiceCategoryJoins DROP CONSTRAINT FK_ServiceCategoryJoins_ComboPackage;
ALTER TABLE dbo.ServiceCategoryJoins ADD CONSTRAINT FK_ServiceCategoryJoins_ComboPackage FOREIGN KEY (ComboPackageID) REFERENCES dbo.ComboPackage(ComboPackageID) ON DELETE CASCADE;

-- 8.10 ServiceRate Constraints
IF OBJECT_ID(N'dbo.FK_ServiceRate_Services', N'F') IS NOT NULL
    ALTER TABLE dbo.ServiceRate DROP CONSTRAINT FK_ServiceRate_Services;
ALTER TABLE dbo.ServiceRate ADD CONSTRAINT FK_ServiceRate_Services FOREIGN KEY (ServicesID) REFERENCES dbo.Services(ServicesID) ON DELETE CASCADE;

IF OBJECT_ID(N'dbo.FK_ServiceRate_RateType', N'F') IS NOT NULL
    ALTER TABLE dbo.ServiceRate DROP CONSTRAINT FK_ServiceRate_RateType;
ALTER TABLE dbo.ServiceRate ADD CONSTRAINT FK_ServiceRate_RateType FOREIGN KEY (RateTypeID) REFERENCES dbo.RateType(RateTypeID);

-- 8.10b Department Constraints
IF OBJECT_ID(N'dbo.FK_Employees_Department', N'F') IS NOT NULL
    ALTER TABLE dbo.Employees DROP CONSTRAINT FK_Employees_Department;
ALTER TABLE dbo.Employees ADD CONSTRAINT FK_Employees_Department FOREIGN KEY (DepartmentID) REFERENCES dbo.Departments(DepartmentID) ON DELETE SET NULL;

IF OBJECT_ID(N'dbo.FK_EmployeeSpecialization_Department', N'F') IS NOT NULL
    ALTER TABLE dbo.EmployeeSpecialization DROP CONSTRAINT FK_EmployeeSpecialization_Department;
ALTER TABLE dbo.EmployeeSpecialization ADD CONSTRAINT FK_EmployeeSpecialization_Department FOREIGN KEY (DepartmentID) REFERENCES dbo.Departments(DepartmentID) ON DELETE SET NULL;

IF OBJECT_ID(N'dbo.FK_ServiceCategory_RateType', N'F') IS NOT NULL
    ALTER TABLE dbo.ServiceCategory DROP CONSTRAINT FK_ServiceCategory_RateType;
ALTER TABLE dbo.ServiceCategory ADD CONSTRAINT FK_ServiceCategory_RateType FOREIGN KEY (RateTypeID) REFERENCES dbo.RateType(RateTypeID);

-- 8.11 EmployeeTimesheets Constraints
IF OBJECT_ID(N'dbo.FK_EmployeeTimesheets_Employees', N'F') IS NOT NULL
    ALTER TABLE dbo.EmployeeTimesheets DROP CONSTRAINT FK_EmployeeTimesheets_Employees;
ALTER TABLE dbo.EmployeeTimesheets ADD CONSTRAINT FK_EmployeeTimesheets_Employees FOREIGN KEY (EmployeeStudioId) REFERENCES dbo.Employees(EmployeeID) ON DELETE CASCADE;

-- 8.12 Portfolio Constraints
IF OBJECT_ID(N'dbo.FK_PortfolioCategoryService_Category', N'F') IS NOT NULL
    ALTER TABLE dbo.PortfolioCategoryService DROP CONSTRAINT FK_PortfolioCategoryService_Category;
ALTER TABLE dbo.PortfolioCategoryService ADD CONSTRAINT FK_PortfolioCategoryService_Category FOREIGN KEY (CategoryID) REFERENCES dbo.PortfolioCategory(CategoryID) ON DELETE CASCADE;

IF OBJECT_ID(N'dbo.FK_Portfolios_PortfolioCategory', N'F') IS NOT NULL
    ALTER TABLE dbo.Portfolios DROP CONSTRAINT FK_Portfolios_PortfolioCategory;
ALTER TABLE dbo.Portfolios ADD CONSTRAINT FK_Portfolios_PortfolioCategory FOREIGN KEY (CategoryID) REFERENCES dbo.PortfolioCategory(CategoryID);

IF OBJECT_ID(N'dbo.FK_PortfolioImages_Portfolios', N'F') IS NOT NULL
    ALTER TABLE dbo.PortfolioImages DROP CONSTRAINT FK_PortfolioImages_Portfolios;
ALTER TABLE dbo.PortfolioImages ADD CONSTRAINT FK_PortfolioImages_Portfolios FOREIGN KEY (PortfolioId) REFERENCES dbo.Portfolios(PortfolioId) ON DELETE CASCADE;

-- 8.13 EventOvertimeCharges Constraints
IF COL_LENGTH(N'dbo.EventOvertimeCharges', N'LoggedByEmployeeID') IS NOT NULL
    ALTER TABLE dbo.EventOvertimeCharges ALTER COLUMN LoggedByEmployeeID NVARCHAR(30) NULL;

IF OBJECT_ID(N'dbo.FK_EventOvertime_EventAppointment', N'F') IS NOT NULL
    ALTER TABLE dbo.EventOvertimeCharges DROP CONSTRAINT FK_EventOvertime_EventAppointment;
ALTER TABLE dbo.EventOvertimeCharges ADD CONSTRAINT FK_EventOvertime_EventAppointment FOREIGN KEY (EventAppointmentID) REFERENCES dbo.EventAppointment(EventAppointmentID) ON DELETE CASCADE;

IF OBJECT_ID(N'dbo.FK_EventOvertime_Employee', N'F') IS NOT NULL
    ALTER TABLE dbo.EventOvertimeCharges DROP CONSTRAINT FK_EventOvertime_Employee;
ALTER TABLE dbo.EventOvertimeCharges ADD CONSTRAINT FK_EventOvertime_Employee FOREIGN KEY (LoggedByEmployeeID) REFERENCES dbo.Employees(EmployeeID) ON DELETE SET NULL;

-- 8.14 PaymentTransactions Constraints
IF COL_LENGTH(N'dbo.PaymentTransactions', N'ReceivedByEmployeeID') IS NOT NULL
    ALTER TABLE dbo.PaymentTransactions ALTER COLUMN ReceivedByEmployeeID NVARCHAR(30) NULL;

IF OBJECT_ID(N'dbo.FK_PaymentTransactions_EventAppointment', N'F') IS NOT NULL
    ALTER TABLE dbo.PaymentTransactions DROP CONSTRAINT FK_PaymentTransactions_EventAppointment;
ALTER TABLE dbo.PaymentTransactions ADD CONSTRAINT FK_PaymentTransactions_EventAppointment FOREIGN KEY (EventAppointmentID) REFERENCES dbo.EventAppointment(EventAppointmentID) ON DELETE CASCADE;

IF OBJECT_ID(N'dbo.FK_PaymentTransactions_Employee', N'F') IS NOT NULL
    ALTER TABLE dbo.PaymentTransactions DROP CONSTRAINT FK_PaymentTransactions_Employee;
ALTER TABLE dbo.PaymentTransactions ADD CONSTRAINT FK_PaymentTransactions_Employee FOREIGN KEY (ReceivedByEmployeeID) REFERENCES dbo.Employees(EmployeeID) ON DELETE SET NULL;

-- 8.15 HistoryLog Audit Constraints (STRICTLY ON DELETE NO ACTION to prevent cyclical paths)
IF OBJECT_ID(N'dbo.FK_HistoryLog_EventAppointment', N'F') IS NOT NULL
    ALTER TABLE dbo.HistoryLog DROP CONSTRAINT FK_HistoryLog_EventAppointment;
ALTER TABLE dbo.HistoryLog ADD CONSTRAINT FK_HistoryLog_EventAppointment FOREIGN KEY (EventAppointmentID) REFERENCES dbo.EventAppointment(EventAppointmentID) ON DELETE NO ACTION;

IF OBJECT_ID(N'dbo.FK_HistoryLog_BookingNegotiation', N'F') IS NOT NULL
    ALTER TABLE dbo.HistoryLog DROP CONSTRAINT FK_HistoryLog_BookingNegotiation;
ALTER TABLE dbo.HistoryLog ADD CONSTRAINT FK_HistoryLog_BookingNegotiation FOREIGN KEY (NegotiationID) REFERENCES dbo.BookingNegotiation(NegotiationID) ON DELETE NO ACTION;

IF OBJECT_ID(N'dbo.FK_HistoryLog_Customer', N'F') IS NOT NULL
    ALTER TABLE dbo.HistoryLog DROP CONSTRAINT FK_HistoryLog_Customer;
ALTER TABLE dbo.HistoryLog ADD CONSTRAINT FK_HistoryLog_Customer FOREIGN KEY (CustomerID) REFERENCES dbo.Customer(CustomerID) ON DELETE NO ACTION;

IF OBJECT_ID(N'dbo.FK_HistoryLog_Employee', N'F') IS NOT NULL
    ALTER TABLE dbo.HistoryLog DROP CONSTRAINT FK_HistoryLog_Employee;
ALTER TABLE dbo.HistoryLog ADD CONSTRAINT FK_HistoryLog_Employee FOREIGN KEY (EmployeeID) REFERENCES dbo.Employees(EmployeeID) ON DELETE NO ACTION;

IF OBJECT_ID(N'dbo.FK_HistoryLog_ChangedByCustomer', N'F') IS NOT NULL
    ALTER TABLE dbo.HistoryLog DROP CONSTRAINT FK_HistoryLog_ChangedByCustomer;
ALTER TABLE dbo.HistoryLog WITH NOCHECK ADD CONSTRAINT FK_HistoryLog_ChangedByCustomer FOREIGN KEY (ChangedByCustomerID) REFERENCES dbo.Customer(CustomerID) ON DELETE NO ACTION;

IF OBJECT_ID(N'dbo.FK_HistoryLog_ChangedByEmployee', N'F') IS NOT NULL
    ALTER TABLE dbo.HistoryLog DROP CONSTRAINT FK_HistoryLog_ChangedByEmployee;
ALTER TABLE dbo.HistoryLog WITH NOCHECK ADD CONSTRAINT FK_HistoryLog_ChangedByEmployee FOREIGN KEY (ChangedByEmployeeID) REFERENCES dbo.Employees(EmployeeID) ON DELETE NO ACTION;

IF OBJECT_ID(N'dbo.FK_HistoryLog_ChangedByAdmin', N'F') IS NOT NULL
    ALTER TABLE dbo.HistoryLog DROP CONSTRAINT FK_HistoryLog_ChangedByAdmin;
ALTER TABLE dbo.HistoryLog WITH NOCHECK ADD CONSTRAINT FK_HistoryLog_ChangedByAdmin FOREIGN KEY (ChangedByAdminID) REFERENCES dbo.Admin(AdminID) ON DELETE NO ACTION;

-- 8.10 CustomerDropbox Constraints
IF OBJECT_ID(N'dbo.FK_CustomerDropbox_EventAppointment', N'F') IS NOT NULL
    ALTER TABLE dbo.CustomerDropbox DROP CONSTRAINT FK_CustomerDropbox_EventAppointment;
ALTER TABLE dbo.CustomerDropbox ADD CONSTRAINT FK_CustomerDropbox_EventAppointment FOREIGN KEY (EventAppointmentID) REFERENCES dbo.EventAppointment(EventAppointmentID) ON DELETE CASCADE;

IF OBJECT_ID(N'dbo.FK_CustomerDropbox_Customer', N'F') IS NOT NULL
    ALTER TABLE dbo.CustomerDropbox DROP CONSTRAINT FK_CustomerDropbox_Customer;
ALTER TABLE dbo.CustomerDropbox ADD CONSTRAINT FK_CustomerDropbox_Customer FOREIGN KEY (CustomerID) REFERENCES dbo.Customer(CustomerID) ON DELETE NO ACTION;

-- 8.10B CustomerDropboxFiles Constraints
IF OBJECT_ID(N'dbo.FK_CustomerDropboxFiles_CustomerDropbox', N'F') IS NOT NULL
    ALTER TABLE dbo.CustomerDropboxFiles DROP CONSTRAINT FK_CustomerDropboxFiles_CustomerDropbox;
ALTER TABLE dbo.CustomerDropboxFiles ADD CONSTRAINT FK_CustomerDropboxFiles_CustomerDropbox FOREIGN KEY (DropboxID) REFERENCES dbo.CustomerDropbox(DropboxID) ON DELETE CASCADE;

IF OBJECT_ID(N'dbo.FK_CustomerDropboxFiles_EventAppointment', N'F') IS NOT NULL
    ALTER TABLE dbo.CustomerDropboxFiles DROP CONSTRAINT FK_CustomerDropboxFiles_EventAppointment;
ALTER TABLE dbo.CustomerDropboxFiles ADD CONSTRAINT FK_CustomerDropboxFiles_EventAppointment FOREIGN KEY (EventAppointmentID) REFERENCES dbo.EventAppointment(EventAppointmentID) ON DELETE NO ACTION;

IF OBJECT_ID(N'dbo.FK_CustomerDropboxFiles_Customer', N'F') IS NOT NULL
    ALTER TABLE dbo.CustomerDropboxFiles DROP CONSTRAINT FK_CustomerDropboxFiles_Customer;
ALTER TABLE dbo.CustomerDropboxFiles ADD CONSTRAINT FK_CustomerDropboxFiles_Customer FOREIGN KEY (CustomerID) REFERENCES dbo.Customer(CustomerID) ON DELETE NO ACTION;

-- 8.11 MaintenanceSchedule Constraints
IF OBJECT_ID(N'dbo.FK_MaintenanceSchedule_Equipment', N'F') IS NOT NULL
    ALTER TABLE dbo.MaintenanceSchedule DROP CONSTRAINT FK_MaintenanceSchedule_Equipment;
ALTER TABLE dbo.MaintenanceSchedule ADD CONSTRAINT FK_MaintenanceSchedule_Equipment FOREIGN KEY (EquipmentID) REFERENCES dbo.Equipment(EquipmentID) ON DELETE CASCADE;
GO

/* =====================================================================
   8C. DOLE-COMPLIANT HYBRID LEAVE MANAGEMENT (Office Clerks vs Shoot Crew)
   Single idempotent block: safe to re-run on any database state.
   - Office Clerks  : PayBasis = FixedMonthly,        IsTitleIExempt = 0 (VL/SL credits, LWOP, 125% approved OT)
   - Shoot Crew     : PayBasis = CommissionPerEvent,  IsTitleIExempt = 1 (UNAVAIL + special statutory leaves only)
   ===================================================================== */

-- 8C.1 Employees: PayBasis + IsTitleIExempt (+ backfill of Office Clerks)
IF OBJECT_ID(N'dbo.Employees', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.Employees', N'PayBasis') IS NULL
        ALTER TABLE dbo.Employees ADD PayBasis nvarchar(30) NOT NULL CONSTRAINT DF_Employees_PayBasis DEFAULT 'CommissionPerEvent';
    IF COL_LENGTH(N'dbo.Employees', N'IsTitleIExempt') IS NULL
        ALTER TABLE dbo.Employees ADD IsTitleIExempt bit NOT NULL CONSTRAINT DF_Employees_IsTitleIExempt DEFAULT 1;

    -- Office Clerks (EMP-003, EMP-004, CLK-% ids, or Office Clerk specialization) => fixed monthly, Title I covered.
    EXEC(N'UPDATE dbo.Employees
           SET PayBasis = ''FixedMonthly'', IsTitleIExempt = 0
           WHERE (PayBasis <> ''FixedMonthly'' OR IsTitleIExempt <> 0)
             AND (EmployeeID IN (N''EMP-003'', N''EMP-004'')
                  OR EmployeeID LIKE N''CLK-%''
                  OR UPPER(ISNULL(Specializations, N'''')) LIKE N''%OFFICE CLERK%'');');
END
GO

-- 8C.2 LeaveTypes catalogue (columns added defensively, then upserted)
IF OBJECT_ID(N'dbo.LeaveTypes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.LeaveTypes (
        LeaveTypeID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Code nvarchar(20) NOT NULL,
        Name nvarchar(50) NOT NULL,
        IsPaid bit NOT NULL CONSTRAINT DF_LeaveTypes_IsPaid DEFAULT 1,
        RequiresCredits bit NOT NULL CONSTRAINT DF_LeaveTypes_RequiresCredits DEFAULT 1,
        DefaultAnnualCredits decimal(5,2) NOT NULL CONSTRAINT DF_LeaveTypes_DefaultAnnualCredits DEFAULT 0,
        IsCashConvertible bit NOT NULL CONSTRAINT DF_LeaveTypes_IsCashConvertible DEFAULT 0,
        AppliesToFieldExempt bit NOT NULL CONSTRAINT DF_LeaveTypes_AppliesToFieldExempt DEFAULT 0,
        RequiresAttachment bit NOT NULL CONSTRAINT DF_LeaveTypes_RequiresAttachment DEFAULT 0,
        GenderRestriction nvarchar(10) NULL,
        CONSTRAINT UQ_LeaveTypes_Code UNIQUE (Code)
    );
END
GO

IF OBJECT_ID(N'dbo.LeaveTypes', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.LeaveTypes', N'Code') IS NULL ALTER TABLE dbo.LeaveTypes ADD Code nvarchar(20) NULL;
    IF COL_LENGTH(N'dbo.LeaveTypes', N'IsPaid') IS NULL ALTER TABLE dbo.LeaveTypes ADD IsPaid bit NOT NULL CONSTRAINT DF_LeaveTypes_IsPaid DEFAULT 1;
    IF COL_LENGTH(N'dbo.LeaveTypes', N'RequiresCredits') IS NULL ALTER TABLE dbo.LeaveTypes ADD RequiresCredits bit NOT NULL CONSTRAINT DF_LeaveTypes_RequiresCredits DEFAULT 1;
    IF COL_LENGTH(N'dbo.LeaveTypes', N'DefaultAnnualCredits') IS NULL ALTER TABLE dbo.LeaveTypes ADD DefaultAnnualCredits decimal(5,2) NOT NULL CONSTRAINT DF_LeaveTypes_DefaultAnnualCredits DEFAULT 0;
    IF COL_LENGTH(N'dbo.LeaveTypes', N'IsCashConvertible') IS NULL ALTER TABLE dbo.LeaveTypes ADD IsCashConvertible bit NOT NULL CONSTRAINT DF_LeaveTypes_IsCashConvertible DEFAULT 0;
    IF COL_LENGTH(N'dbo.LeaveTypes', N'AppliesToFieldExempt') IS NULL ALTER TABLE dbo.LeaveTypes ADD AppliesToFieldExempt bit NOT NULL CONSTRAINT DF_LeaveTypes_AppliesToFieldExempt DEFAULT 0;
    IF COL_LENGTH(N'dbo.LeaveTypes', N'RequiresAttachment') IS NULL ALTER TABLE dbo.LeaveTypes ADD RequiresAttachment bit NOT NULL CONSTRAINT DF_LeaveTypes_RequiresAttachment DEFAULT 0;
    IF COL_LENGTH(N'dbo.LeaveTypes', N'GenderRestriction') IS NULL ALTER TABLE dbo.LeaveTypes ADD GenderRestriction nvarchar(10) NULL;

    EXEC(N'
    MERGE dbo.LeaveTypes AS t
    USING (VALUES
        (N''VL'',     N''Vacation Leave'',                 1, 1,   5.00, 1, 0, 0, NULL),
        (N''SL'',     N''Sick Leave'',                     1, 1,   5.00, 0, 0, 1, NULL),
        (N''LWOP'',   N''Leave Without Pay'',              0, 0,   0.00, 0, 0, 0, NULL),
        (N''UNAVAIL'',N''Shoot Unavailability / Time Off'',0, 0,   0.00, 0, 1, 0, NULL),
        (N''MAT'',    N''Maternity Leave'',                1, 1, 105.00, 0, 1, 0, N''Female''),
        (N''PAT'',    N''Paternity Leave'',                1, 1,   7.00, 0, 1, 0, N''Male''),
        (N''SPL'',    N''Solo Parent Leave'',              1, 1,   7.00, 0, 1, 0, NULL),
        (N''VAWC'',   N''VAWC Leave'',                     1, 1,  10.00, 0, 1, 0, N''Female''),
        (N''EMERG'',  N''Emergency / Sudden Sick'',        0, 0,   0.00, 0, 1, 0, NULL)
    ) AS s (Code, Name, IsPaid, RequiresCredits, DefaultAnnualCredits, IsCashConvertible, AppliesToFieldExempt, RequiresAttachment, GenderRestriction)
    ON t.Code = s.Code
    WHEN MATCHED THEN UPDATE SET
        Name = s.Name, IsPaid = s.IsPaid, RequiresCredits = s.RequiresCredits,
        DefaultAnnualCredits = s.DefaultAnnualCredits, IsCashConvertible = s.IsCashConvertible,
        AppliesToFieldExempt = s.AppliesToFieldExempt, RequiresAttachment = s.RequiresAttachment,
        GenderRestriction = s.GenderRestriction
    WHEN NOT MATCHED BY TARGET THEN INSERT
        (Code, Name, IsPaid, RequiresCredits, DefaultAnnualCredits, IsCashConvertible, AppliesToFieldExempt, RequiresAttachment, GenderRestriction)
        VALUES (s.Code, s.Name, s.IsPaid, s.RequiresCredits, s.DefaultAnnualCredits, s.IsCashConvertible, s.AppliesToFieldExempt, s.RequiresAttachment, s.GenderRestriction);');
END
GO

-- 8C.3 LeaveBalances (AvailableCredits is computed: TotalAllocated - PendingCredits - UsedCredits)
IF OBJECT_ID(N'dbo.LeaveBalances', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.LeaveBalances (
        LeaveBalanceID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeID nvarchar(30) NOT NULL,
        LeaveTypeID int NOT NULL,
        [Year] int NOT NULL,
        TotalAllocated decimal(6,2) NOT NULL CONSTRAINT DF_LeaveBalances_TotalAllocated DEFAULT 0,
        PendingCredits decimal(6,2) NOT NULL CONSTRAINT DF_LeaveBalances_PendingCredits DEFAULT 0,
        UsedCredits decimal(6,2) NOT NULL CONSTRAINT DF_LeaveBalances_UsedCredits DEFAULT 0,
        AvailableCredits AS (TotalAllocated - PendingCredits - UsedCredits),
        UpdatedAt datetime2 NOT NULL CONSTRAINT DF_LeaveBalances_UpdatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_LeaveBalances_Emp_Type_Year UNIQUE (EmployeeID, LeaveTypeID, [Year]),
        CONSTRAINT CK_LeaveBalances_NonNegative CHECK (TotalAllocated >= 0 AND PendingCredits >= 0 AND UsedCredits >= 0)
    );
END
GO

IF OBJECT_ID(N'dbo.LeaveBalances', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.Employees', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.LeaveTypes', N'U') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.FK_LeaveBalances_Employees', N'F') IS NOT NULL
        ALTER TABLE dbo.LeaveBalances DROP CONSTRAINT FK_LeaveBalances_Employees;
    ALTER TABLE dbo.LeaveBalances ADD CONSTRAINT FK_LeaveBalances_Employees FOREIGN KEY (EmployeeID) REFERENCES dbo.Employees(EmployeeID) ON DELETE CASCADE;

    IF OBJECT_ID(N'dbo.FK_LeaveBalances_LeaveTypes', N'F') IS NOT NULL
        ALTER TABLE dbo.LeaveBalances DROP CONSTRAINT FK_LeaveBalances_LeaveTypes;
    ALTER TABLE dbo.LeaveBalances ADD CONSTRAINT FK_LeaveBalances_LeaveTypes FOREIGN KEY (LeaveTypeID) REFERENCES dbo.LeaveTypes(LeaveTypeID) ON DELETE NO ACTION;
END
GO

-- 8C.4 EmployeeLeaves (leave requests): create if absent, then add DOLE columns
IF OBJECT_ID(N'dbo.EmployeeLeaves', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.EmployeeLeaves (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        LeaveId nvarchar(50) NOT NULL,
        EmployeeStudioId nvarchar(50) NOT NULL,
        EmployeeName nvarchar(255) NOT NULL,
        LeaveType nvarchar(50) NOT NULL,
        StartDate date NOT NULL,
        EndDate date NOT NULL,
        Reason nvarchar(500) NULL,
        Status nvarchar(20) NOT NULL CONSTRAINT DF_Leave_Status DEFAULT 'Pending',
        ApprovedBy nvarchar(255) NULL,
        ApprovedAt datetime2 NULL,
        RejectionReason nvarchar(500) NULL,
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_Leave_CreatedAt DEFAULT SYSUTCDATETIME()
    );
END
GO

IF OBJECT_ID(N'dbo.EmployeeLeaves', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.EmployeeLeaves', N'LeaveTypeCode') IS NULL ALTER TABLE dbo.EmployeeLeaves ADD LeaveTypeCode nvarchar(20) NULL;
    IF COL_LENGTH(N'dbo.EmployeeLeaves', N'CountableDays') IS NULL ALTER TABLE dbo.EmployeeLeaves ADD CountableDays decimal(5,2) NULL;
    IF COL_LENGTH(N'dbo.EmployeeLeaves', N'IsHalfDay') IS NULL ALTER TABLE dbo.EmployeeLeaves ADD IsHalfDay bit NOT NULL CONSTRAINT DF_EmployeeLeaves_IsHalfDay DEFAULT 0;
    IF COL_LENGTH(N'dbo.EmployeeLeaves', N'CreditYear') IS NULL ALTER TABLE dbo.EmployeeLeaves ADD CreditYear int NULL;
    IF COL_LENGTH(N'dbo.EmployeeLeaves', N'CreditsReserved') IS NULL ALTER TABLE dbo.EmployeeLeaves ADD CreditsReserved bit NOT NULL CONSTRAINT DF_EmployeeLeaves_CreditsReserved DEFAULT 0;
    IF COL_LENGTH(N'dbo.EmployeeLeaves', N'CreditsConsumed') IS NULL ALTER TABLE dbo.EmployeeLeaves ADD CreditsConsumed bit NOT NULL CONSTRAINT DF_EmployeeLeaves_CreditsConsumed DEFAULT 0;
    IF COL_LENGTH(N'dbo.EmployeeLeaves', N'ConflictCount') IS NULL ALTER TABLE dbo.EmployeeLeaves ADD ConflictCount int NOT NULL CONSTRAINT DF_EmployeeLeaves_ConflictCount DEFAULT 0;
    IF COL_LENGTH(N'dbo.EmployeeLeaves', N'AttachmentName') IS NULL ALTER TABLE dbo.EmployeeLeaves ADD AttachmentName nvarchar(255) NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_EmployeeLeaves_Emp_Status_Dates' AND object_id = OBJECT_ID(N'dbo.EmployeeLeaves'))
        CREATE INDEX IX_EmployeeLeaves_Emp_Status_Dates ON dbo.EmployeeLeaves (EmployeeStudioId, Status, StartDate, EndDate);
END
GO

-- 8C.5 EmployeeTimesheets (time entries): regular / undertime / OT split, leave-sync link
IF OBJECT_ID(N'dbo.EmployeeTimesheets', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.EmployeeTimesheets', N'RegularMinutes') IS NULL ALTER TABLE dbo.EmployeeTimesheets ADD RegularMinutes int NOT NULL CONSTRAINT DF_EmployeeTimesheets_RegularMinutes DEFAULT 0;
    IF COL_LENGTH(N'dbo.EmployeeTimesheets', N'UndertimeMinutes') IS NULL ALTER TABLE dbo.EmployeeTimesheets ADD UndertimeMinutes int NOT NULL CONSTRAINT DF_EmployeeTimesheets_UndertimeMinutes DEFAULT 0;
    IF COL_LENGTH(N'dbo.EmployeeTimesheets', N'PendingOvertimeMinutes') IS NULL ALTER TABLE dbo.EmployeeTimesheets ADD PendingOvertimeMinutes int NOT NULL CONSTRAINT DF_EmployeeTimesheets_PendingOT DEFAULT 0;
    IF COL_LENGTH(N'dbo.EmployeeTimesheets', N'ApprovedOvertimeMinutes') IS NULL ALTER TABLE dbo.EmployeeTimesheets ADD ApprovedOvertimeMinutes int NOT NULL CONSTRAINT DF_EmployeeTimesheets_ApprovedOT DEFAULT 0;
    IF COL_LENGTH(N'dbo.EmployeeTimesheets', N'IsOvertimeApproved') IS NULL ALTER TABLE dbo.EmployeeTimesheets ADD IsOvertimeApproved bit NOT NULL CONSTRAINT DF_EmployeeTimesheets_IsOTApproved DEFAULT 0;
    IF COL_LENGTH(N'dbo.EmployeeTimesheets', N'OvertimeApprovedBy') IS NULL ALTER TABLE dbo.EmployeeTimesheets ADD OvertimeApprovedBy nvarchar(100) NULL;
    IF COL_LENGTH(N'dbo.EmployeeTimesheets', N'OvertimeApprovedAt') IS NULL ALTER TABLE dbo.EmployeeTimesheets ADD OvertimeApprovedAt datetime2 NULL;
    IF COL_LENGTH(N'dbo.EmployeeTimesheets', N'EntryType') IS NULL ALTER TABLE dbo.EmployeeTimesheets ADD EntryType nvarchar(30) NOT NULL CONSTRAINT DF_EmployeeTimesheets_EntryType DEFAULT 'Work';
    IF COL_LENGTH(N'dbo.EmployeeTimesheets', N'LeaveRequestID') IS NULL ALTER TABLE dbo.EmployeeTimesheets ADD LeaveRequestID int NULL;
END
GO

IF OBJECT_ID(N'dbo.EmployeeTimesheets', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.EmployeeLeaves', N'U') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.FK_EmployeeTimesheets_LeaveRequest', N'F') IS NOT NULL
        ALTER TABLE dbo.EmployeeTimesheets DROP CONSTRAINT FK_EmployeeTimesheets_LeaveRequest;
    ALTER TABLE dbo.EmployeeTimesheets ADD CONSTRAINT FK_EmployeeTimesheets_LeaveRequest FOREIGN KEY (LeaveRequestID) REFERENCES dbo.EmployeeLeaves(Id) ON DELETE SET NULL;

    -- One synced leave entry per (leave request, day)
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_EmployeeTimesheets_Leave_Day' AND object_id = OBJECT_ID(N'dbo.EmployeeTimesheets'))
        EXEC(N'CREATE UNIQUE INDEX UX_EmployeeTimesheets_Leave_Day ON dbo.EmployeeTimesheets (LeaveRequestID, WorkDate) WHERE LeaveRequestID IS NOT NULL;');
END
GO

-- 8C.6 Data hygiene + seeds (dynamic SQL because the columns above were created in earlier batches of the same script run)
IF OBJECT_ID(N'dbo.EmployeeLeaves', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.LeaveTypes', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.Employees', N'IsTitleIExempt') IS NOT NULL
BEGIN
    -- Crew (Title I exempt) cannot hold VL/SL/LWOP: convert active ones to Shoot Unavailability so the schedule block survives.
    EXEC(N'UPDATE el
           SET el.LeaveType = N''Shoot Unavailability / Time Off'', el.LeaveTypeCode = N''UNAVAIL'', el.CreditsReserved = 0, el.CreditsConsumed = 0
           FROM dbo.EmployeeLeaves el
           JOIN dbo.Employees e ON e.EmployeeID = el.EmployeeStudioId
           WHERE e.IsTitleIExempt = 1
             AND el.LeaveType IN (N''Sick Leave'', N''Vacation Leave'', N''Leave Without Pay'')
             AND el.Status IN (N''Pending'', N''Approved'');');

    EXEC(N'UPDATE el SET el.LeaveTypeCode = lt.Code
           FROM dbo.EmployeeLeaves el JOIN dbo.LeaveTypes lt ON lt.Name = el.LeaveType
           WHERE el.LeaveTypeCode IS NULL;');

    EXEC(N'UPDATE dbo.EmployeeLeaves
           SET CountableDays = CASE WHEN IsHalfDay = 1 THEN 0.5 ELSE CAST(DATEDIFF(DAY, StartDate, EndDate) + 1 AS decimal(5,2)) END,
               CreditYear = YEAR(StartDate)
           WHERE CountableDays IS NULL;');

    -- Remove VL/SL/credit balances that belong to Title I exempt crew
    EXEC(N'DELETE lb
           FROM dbo.LeaveBalances lb
           JOIN dbo.Employees e ON e.EmployeeID = lb.EmployeeID
           JOIN dbo.LeaveTypes lt ON lt.LeaveTypeID = lb.LeaveTypeID
           WHERE e.IsTitleIExempt = 1 AND lt.AppliesToFieldExempt = 0 AND lt.RequiresCredits = 1;');

    -- 2026 VL + SL credits for every Title I covered employee (Office Clerks)
    EXEC(N'INSERT INTO dbo.LeaveBalances (EmployeeID, LeaveTypeID, [Year], TotalAllocated)
           SELECT e.EmployeeID, lt.LeaveTypeID, 2026, lt.DefaultAnnualCredits
           FROM dbo.Employees e
           CROSS JOIN dbo.LeaveTypes lt
           WHERE e.IsTitleIExempt = 0 AND lt.Code IN (N''VL'', N''SL'')
             AND NOT EXISTS (SELECT 1 FROM dbo.LeaveBalances lb WHERE lb.EmployeeID = e.EmployeeID AND lb.LeaveTypeID = lt.LeaveTypeID AND lb.[Year] = 2026);');
END
GO

IF OBJECT_ID(N'dbo.EmployeeTimesheets', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.EmployeeTimesheets', N'RegularMinutes') IS NOT NULL AND COL_LENGTH(N'dbo.Employees', N'IsTitleIExempt') IS NOT NULL
BEGIN
    -- Historical completed clerk shifts: cap regular minutes at 480 (no retroactive OT/undertime is manufactured)
    EXEC(N'UPDATE ts
           SET ts.RegularMinutes = CASE WHEN ts.TotalHours * 60 > 480 THEN 480 ELSE CAST(ROUND(ts.TotalHours * 60, 0) AS int) END
           FROM dbo.EmployeeTimesheets ts
           JOIN dbo.Employees e ON e.EmployeeID = ts.EmployeeStudioId
           WHERE e.IsTitleIExempt = 0 AND ts.ClockOut IS NOT NULL AND ts.EntryType = N''Work'' AND ts.RegularMinutes = 0 AND ts.TotalHours > 0;');
END
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

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ServiceCategoryJoins_Services' AND object_id = OBJECT_ID(N'dbo.ServiceCategoryJoins'))
    CREATE INDEX IX_ServiceCategoryJoins_Services ON dbo.ServiceCategoryJoins (ServicesID) INCLUDE (ServiceCategoryID);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ServiceCategoryJoins_AddOn' AND object_id = OBJECT_ID(N'dbo.ServiceCategoryJoins'))
    CREATE INDEX IX_ServiceCategoryJoins_AddOn    ON dbo.ServiceCategoryJoins (AdditionalServicesID) INCLUDE (ServiceCategoryID);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ServiceCategoryJoins_Pkg' AND object_id = OBJECT_ID(N'dbo.ServiceCategoryJoins'))
    CREATE INDEX IX_ServiceCategoryJoins_Pkg      ON dbo.ServiceCategoryJoins (ComboPackageID) INCLUDE (ServiceCategoryID);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ComboPackageJoin_Package' AND object_id = OBJECT_ID(N'dbo.ComboPackageJoin'))
    CREATE INDEX IX_ComboPackageJoin_Package    ON dbo.ComboPackageJoin (ComboPackageID);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ComboPackageJoin_Services' AND object_id = OBJECT_ID(N'dbo.ComboPackageJoin'))
    CREATE INDEX IX_ComboPackageJoin_Services   ON dbo.ComboPackageJoin (ServicesID) INCLUDE (ComboPackageID);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ComboPackageJoin_AddOn' AND object_id = OBJECT_ID(N'dbo.ComboPackageJoin'))
    CREATE INDEX IX_ComboPackageJoin_AddOn      ON dbo.ComboPackageJoin (AdditionalServicesID) INCLUDE (ComboPackageID);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ServiceCategory_Name' AND object_id = OBJECT_ID(N'dbo.ServiceCategory'))
    CREATE INDEX IX_ServiceCategory_Name        ON dbo.ServiceCategory (ServiceCategoryName);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Services_Name' AND object_id = OBJECT_ID(N'dbo.Services'))
    CREATE INDEX IX_Services_Name               ON dbo.Services (ServiceName);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Equipment_EquipmentType' AND object_id = OBJECT_ID(N'dbo.Equipment'))
    CREATE INDEX IX_Equipment_EquipmentType     ON dbo.Equipment (EquipmentTypeID);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MaintenanceSchedule_EquipmentID_Status' AND object_id = OBJECT_ID(N'dbo.MaintenanceSchedule'))
    CREATE INDEX IX_MaintenanceSchedule_EquipmentID_Status ON dbo.MaintenanceSchedule (EquipmentID, Status, UpdatedAt DESC);

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

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_HomeHighlights_DisplayOrder' AND object_id = OBJECT_ID(N'dbo.HomeHighlights'))
    CREATE INDEX IX_HomeHighlights_DisplayOrder ON dbo.HomeHighlights (IsActive, DisplayOrder, CreatedAt DESC);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PaymentTransactions_Event' AND object_id = OBJECT_ID(N'dbo.PaymentTransactions'))
    CREATE INDEX IX_PaymentTransactions_Event  ON dbo.PaymentTransactions (EventAppointmentID, CreatedAt DESC);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_EventOvertime_Event' AND object_id = OBJECT_ID(N'dbo.EventOvertimeCharges'))
    CREATE INDEX IX_EventOvertime_Event        ON dbo.EventOvertimeCharges (EventAppointmentID);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BookingNegotiation_Customer' AND object_id = OBJECT_ID(N'dbo.BookingNegotiation'))
    CREATE INDEX IX_BookingNegotiation_Customer ON dbo.BookingNegotiation (CustomerID, ConsultationDate DESC);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BookingNegotiation_Employee' AND object_id = OBJECT_ID(N'dbo.BookingNegotiation'))
    CREATE INDEX IX_BookingNegotiation_Employee ON dbo.BookingNegotiation (EmployeeID, ConsultationDate DESC);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ArchiveIndex_Entity_ArchivedAt' AND object_id = OBJECT_ID(N'dbo.ArchiveIndex'))
    CREATE INDEX IX_ArchiveIndex_Entity_ArchivedAt ON dbo.ArchiveIndex (EntityName, ArchivedAt DESC);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Employees_Status_Dept' AND object_id = OBJECT_ID(N'dbo.Employees'))
    CREATE INDEX IX_Employees_Status_Dept ON dbo.Employees (EmployeeStatus, DepartmentID);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_EmployeeJointSpecialization_Emp_Spec' AND object_id = OBJECT_ID(N'dbo.EmployeeJointSpecialization'))
    CREATE INDEX IX_EmployeeJointSpecialization_Emp_Spec ON dbo.EmployeeJointSpecialization (EmployeeID, SpecializationID);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_EventAppointment_Date_Status' AND object_id = OBJECT_ID(N'dbo.EventAppointment'))
    CREATE INDEX IX_EventAppointment_Date_Status ON dbo.EventAppointment (EventDate, EventStatus);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PaymentTransactions_Remittance' AND object_id = OBJECT_ID(N'dbo.PaymentTransactions'))
    CREATE INDEX IX_PaymentTransactions_Remittance ON dbo.PaymentTransactions (EmployeeRemittanceStatus, PaymentType);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CustomerDropbox_Customer_ExpiresAt' AND object_id = OBJECT_ID(N'dbo.CustomerDropbox'))
    CREATE INDEX IX_CustomerDropbox_Customer_ExpiresAt ON dbo.CustomerDropbox (CustomerID, ExpiresAt);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CustomerDropbox_ExpiresAt' AND object_id = OBJECT_ID(N'dbo.CustomerDropbox'))
    CREATE INDEX IX_CustomerDropbox_ExpiresAt ON dbo.CustomerDropbox (ExpiresAt);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CustomerDropboxFiles_Dropbox_Status' AND object_id = OBJECT_ID(N'dbo.CustomerDropboxFiles'))
    CREATE INDEX IX_CustomerDropboxFiles_Dropbox_Status ON dbo.CustomerDropboxFiles (DropboxID, FileStatus, ExpiresAt);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CustomerDropboxFiles_ExpiresAt' AND object_id = OBJECT_ID(N'dbo.CustomerDropboxFiles'))
    CREATE INDEX IX_CustomerDropboxFiles_ExpiresAt ON dbo.CustomerDropboxFiles (ExpiresAt);
GO

/* =====================================================================
   10. SNAPSHOT HARD DELETE SCHEMA ALTERATIONS & NEW COLUMNS
   (Non-destructive upgrade statements for existing databases)
   ===================================================================== */
-- Departments table & initial seed data
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
    IF COL_LENGTH(N'dbo.EventAppointment', N'FinalPaymentMethod') IS NULL
        ALTER TABLE dbo.EventAppointment ADD FinalPaymentMethod nvarchar(30) NULL;
    IF COL_LENGTH(N'dbo.EventAppointment', N'FinalPaymentStatus') IS NULL
        ALTER TABLE dbo.EventAppointment ADD FinalPaymentStatus nvarchar(60) NULL;
    ELSE IF COL_LENGTH(N'dbo.EventAppointment', N'FinalPaymentStatus') < 120
        ALTER TABLE dbo.EventAppointment ALTER COLUMN FinalPaymentStatus nvarchar(60) NULL;
    IF COL_LENGTH(N'dbo.EventAppointment', N'FinalQrString') IS NULL
        ALTER TABLE dbo.EventAppointment ADD FinalQrString nvarchar(1000) NULL;
    IF COL_LENGTH(N'dbo.EventAppointment', N'FinalPaymentReferenceId') IS NULL
        ALTER TABLE dbo.EventAppointment ADD FinalPaymentReferenceId nvarchar(100) NULL;
    IF COL_LENGTH(N'dbo.EventAppointment', N'FinalPaymentPaidAt') IS NULL
        ALTER TABLE dbo.EventAppointment ADD FinalPaymentPaidAt datetime2 NULL;
END;
GO

IF OBJECT_ID(N'dbo.EventAppointment', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.check_constraints
                   WHERE name = N'CK_EventAppointment_Status'
                     AND definition LIKE N'%Pending Verification%'
                     AND definition LIKE N'%Dispatch%'
                     AND definition LIKE N'%Complete%'
                     AND definition LIKE N'%Completed%'
                     AND definition LIKE N'%In Execution%'
                     AND definition LIKE N'%Ready%'
                     AND definition LIKE N'%Event Done%'
                     AND definition LIKE N'%Payment Verification (Cash)%')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_EventAppointment_Status')
        ALTER TABLE dbo.EventAppointment DROP CONSTRAINT CK_EventAppointment_Status;

    ALTER TABLE dbo.EventAppointment ADD CONSTRAINT CK_EventAppointment_Status
        CHECK (EventStatus IN ('Pending','Pending Agreement','Pending Reservation Fee','Awaiting_Downpayment','Pending Payment','Ready','In Execution','In Progress','Preparation','Preparing','Dispatch','Ongoing','On Going','Event Done','Post Event','Awaiting Settlement','Pending Verification','Paid Cash Pending Verification','Payment Verification (Cash)','Confirmed','Complete','Completed','Shot','Editing','Delivered','Cancelled','Expired'));
END;
GO

IF OBJECT_ID(N'dbo.Customer', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.Customer', N'DeletionRequested') IS NULL
        ALTER TABLE dbo.Customer ADD DeletionRequested bit NOT NULL CONSTRAINT DF_Customer_DeletionRequested DEFAULT 0;
    IF COL_LENGTH(N'dbo.Customer', N'DeletionRequestedAt') IS NULL
        ALTER TABLE dbo.Customer ADD DeletionRequestedAt datetime2 NULL;
END;
GO

IF OBJECT_ID(N'dbo.Equipment', N'U') IS NOT NULL
BEGIN
    ALTER TABLE dbo.Equipment ALTER COLUMN EquipmentStatus nvarchar(30) NOT NULL;
    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
                   WHERE name = N'CK_Equipment_Status'
                     AND definition LIKE N'%Needs Replacement%')
    BEGIN
        IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Equipment_Status')
            ALTER TABLE dbo.Equipment DROP CONSTRAINT CK_Equipment_Status;
        ALTER TABLE dbo.Equipment ADD CONSTRAINT CK_Equipment_Status
            CHECK (EquipmentStatus IN ('Available','Maintenance','Retired','Needs Replacement'));
    END;
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


/* =====================================================================
   15. TWO-STAGE CONTRACTS, TERMS VERSIONS, PAYMENT LEDGER & RECEIPTS (IDEMPOTENT)
   ===================================================================== */

IF OBJECT_ID(N'dbo.EventAppointment', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.EventAppointment', N'OvertimeAmount') IS NULL
        ALTER TABLE dbo.EventAppointment ADD OvertimeAmount decimal(18,2) NOT NULL CONSTRAINT DF_EventAppointment_OvertimeAmount DEFAULT 0.00;
    IF COL_LENGTH(N'dbo.EventAppointment', N'RemainingBalance') IS NULL
        ALTER TABLE dbo.EventAppointment ADD RemainingBalance decimal(18,2) NOT NULL CONSTRAINT DF_EventAppointment_RemainingBalance DEFAULT 0.00;
    IF COL_LENGTH(N'dbo.EventAppointment', N'IsDeliverablesUnlocked') IS NULL
        ALTER TABLE dbo.EventAppointment ADD IsDeliverablesUnlocked bit NOT NULL CONSTRAINT DF_EventAppointment_IsDeliverablesUnlocked DEFAULT 0;
    IF COL_LENGTH(N'dbo.EventAppointment', N'ContractAcceptedAtUtc') IS NULL
        ALTER TABLE dbo.EventAppointment ADD ContractAcceptedAtUtc datetime2 NULL;
END;
GO

IF OBJECT_ID(N'dbo.TermsVersions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TermsVersions (
        TermsVersionId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_TermsVersions PRIMARY KEY,
        VersionCode nvarchar(30) NOT NULL,
        Title nvarchar(200) NOT NULL CONSTRAINT DF_TermsVersions_Title DEFAULT 'Standard Terms and Conditions',
        EffectiveDate date NOT NULL,
        DeliveryBusinessDays int NOT NULL CONSTRAINT DF_TermsVersions_DeliveryDays DEFAULT 7,
        OvertimeHourlyRate decimal(18,2) NOT NULL CONSTRAINT DF_TermsVersions_OvertimeRate DEFAULT 1500.00,
        TermsBodyHtml nvarchar(max) NOT NULL,
        IsActive bit NOT NULL CONSTRAINT DF_TermsVersions_IsActive DEFAULT 1,
        CreatedAtUtc datetime2 NOT NULL CONSTRAINT DF_TermsVersions_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_TermsVersions_VersionCode UNIQUE (VersionCode)
    );
END;
GO

IF OBJECT_ID(N'dbo.BookingContracts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BookingContracts (
        BookingContractId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_BookingContracts PRIMARY KEY,
        ContractNumber nvarchar(40) NOT NULL,
        EventAppointmentID nvarchar(30) NOT NULL,
        NegotiationID nvarchar(30) NULL,
        CustomerID nvarchar(30) NOT NULL,
        TermsVersionId int NOT NULL,
        TermsVersionCode nvarchar(30) NOT NULL,
        DeliveryBusinessDays int NOT NULL CONSTRAINT DF_BookingContracts_DeliveryDays DEFAULT 7,
        StudioName nvarchar(200) NOT NULL,
        StudioAddress nvarchar(500) NOT NULL,
        StudioPhone nvarchar(100) NOT NULL,
        StudioEmail nvarchar(200) NOT NULL,
        ClientName nvarchar(200) NOT NULL,
        ClientAddress nvarchar(500) NULL,
        ClientPhone nvarchar(100) NULL,
        ClientEmail nvarchar(200) NOT NULL,
        SessionType nvarchar(150) NOT NULL,
        DateOfShoot date NOT NULL,
        StartTime time NOT NULL,
        EndTime time NOT NULL,
        Venue nvarchar(255) NOT NULL,
        PackageName nvarchar(255) NOT NULL,
        PackageInclusions nvarchar(1000) NOT NULL,
        TotalPackageAmount decimal(18,2) NOT NULL,
        ReservationFeeAmount decimal(18,2) NOT NULL,
        RemainingBalanceAmount decimal(18,2) NOT NULL,
        RepresentativeName nvarchar(200) NOT NULL,
        ContractStatus nvarchar(30) NOT NULL CONSTRAINT DF_BookingContracts_Status DEFAULT 'PendingAcceptance',
        AcceptedAtUtc datetime2 NULL,
        ClientIpAddress nvarchar(100) NULL,
        UserAgent nvarchar(500) NULL,
        ClickwrapReference nvarchar(100) NULL,
        RenderedContractHtml nvarchar(max) NOT NULL,
        PdfBucketName nvarchar(100) NULL,
        PdfObjectKey nvarchar(500) NULL,
        PdfUri nvarchar(1000) NULL,
        CreatedAtUtc datetime2 NOT NULL CONSTRAINT DF_BookingContracts_CreatedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc datetime2 NOT NULL CONSTRAINT DF_BookingContracts_UpdatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_BookingContracts_ContractNumber UNIQUE (ContractNumber)
    );
    CREATE INDEX IX_BookingContracts_EventAppointmentID ON dbo.BookingContracts (EventAppointmentID);
END;
GO

IF OBJECT_ID(N'dbo.BookingContracts', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.EventAppointment', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.FK_BookingContracts_EventAppointment', N'F') IS NULL
    ALTER TABLE dbo.BookingContracts ADD CONSTRAINT FK_BookingContracts_EventAppointment
        FOREIGN KEY (EventAppointmentID) REFERENCES dbo.EventAppointment(EventAppointmentID) ON DELETE CASCADE;
IF OBJECT_ID(N'dbo.BookingContracts', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.TermsVersions', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.FK_BookingContracts_TermsVersions', N'F') IS NULL
    ALTER TABLE dbo.BookingContracts ADD CONSTRAINT FK_BookingContracts_TermsVersions
        FOREIGN KEY (TermsVersionId) REFERENCES dbo.TermsVersions(TermsVersionId) ON DELETE NO ACTION;
GO

IF OBJECT_ID(N'dbo.BookingPayments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BookingPayments (
        BookingPaymentId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_BookingPayments PRIMARY KEY,
        EventAppointmentID nvarchar(30) NOT NULL,
        BookingContractId int NULL,
        PaymentStage nvarchar(30) NOT NULL,
        PaymentMethod nvarchar(50) NOT NULL,
        Amount decimal(18,2) NOT NULL,
        OvertimeAmount decimal(18,2) NOT NULL CONSTRAINT DF_BookingPayments_Overtime DEFAULT 0.00,
        GatewayReferenceId nvarchar(150) NOT NULL,
        ExternalReference nvarchar(150) NULL,
        Status nvarchar(30) NOT NULL CONSTRAINT DF_BookingPayments_Status DEFAULT 'Paid',
        PaidAtUtc datetime2 NOT NULL CONSTRAINT DF_BookingPayments_PaidAt DEFAULT SYSUTCDATETIME(),
        VerifiedByEmployeeID nvarchar(30) NULL,
        Notes nvarchar(1000) NULL,
        CreatedAtUtc datetime2 NOT NULL CONSTRAINT DF_BookingPayments_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_BookingPayments_Stage CHECK (PaymentStage IN ('ReservationFee', 'FinalSettlement'))
    );
    CREATE INDEX IX_BookingPayments_Event_Stage ON dbo.BookingPayments (EventAppointmentID, PaymentStage);
END;
GO

IF OBJECT_ID(N'dbo.BookingPayments', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.EventAppointment', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.FK_BookingPayments_EventAppointment', N'F') IS NULL
    ALTER TABLE dbo.BookingPayments ADD CONSTRAINT FK_BookingPayments_EventAppointment
        FOREIGN KEY (EventAppointmentID) REFERENCES dbo.EventAppointment(EventAppointmentID) ON DELETE CASCADE;
IF OBJECT_ID(N'dbo.BookingPayments', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.BookingContracts', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.FK_BookingPayments_BookingContract', N'F') IS NULL
    ALTER TABLE dbo.BookingPayments ADD CONSTRAINT FK_BookingPayments_BookingContract
        FOREIGN KEY (BookingContractId) REFERENCES dbo.BookingContracts(BookingContractId) ON DELETE NO ACTION;
GO

IF OBJECT_ID(N'dbo.BookingReceipts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BookingReceipts (
        BookingReceiptId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_BookingReceipts PRIMARY KEY,
        ReceiptNumber nvarchar(50) NOT NULL,
        EventAppointmentID nvarchar(30) NOT NULL,
        BookingPaymentId int NULL,
        ReceiptType nvarchar(40) NOT NULL,
        TotalContractCharges decimal(18,2) NOT NULL,
        Stage1PaidAmount decimal(18,2) NOT NULL CONSTRAINT DF_BookingReceipts_Stage1 DEFAULT 0.00,
        OvertimeAddOnAmount decimal(18,2) NOT NULL CONSTRAINT DF_BookingReceipts_Overtime DEFAULT 0.00,
        Stage2PaidAmount decimal(18,2) NOT NULL CONSTRAINT DF_BookingReceipts_Stage2 DEFAULT 0.00,
        RemainingBalanceDue decimal(18,2) NOT NULL,
        PaymentMethod nvarchar(50) NOT NULL,
        GatewayReferenceId nvarchar(150) NULL,
        IssuedAtUtc datetime2 NOT NULL CONSTRAINT DF_BookingReceipts_IssuedAt DEFAULT SYSUTCDATETIME(),
        RenderedReceiptHtml nvarchar(max) NOT NULL,
        PdfBucketName nvarchar(100) NULL,
        PdfObjectKey nvarchar(500) NULL,
        PdfUri nvarchar(1000) NULL,
        CreatedAtUtc datetime2 NOT NULL CONSTRAINT DF_BookingReceipts_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_BookingReceipts_ReceiptNumber UNIQUE (ReceiptNumber),
        CONSTRAINT UQ_BookingReceipts_Appt_Type UNIQUE (EventAppointmentID, ReceiptType),
        CONSTRAINT CK_BookingReceipts_Type CHECK (ReceiptType IN ('ReservationAcknowledgment', 'FinalConsolidated'))
    );
END;
GO

IF OBJECT_ID(N'dbo.BookingReceipts', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.EventAppointment', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.FK_BookingReceipts_EventAppointment', N'F') IS NULL
    ALTER TABLE dbo.BookingReceipts ADD CONSTRAINT FK_BookingReceipts_EventAppointment
        FOREIGN KEY (EventAppointmentID) REFERENCES dbo.EventAppointment(EventAppointmentID) ON DELETE CASCADE;
IF OBJECT_ID(N'dbo.BookingReceipts', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.BookingPayments', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.FK_BookingReceipts_BookingPayment', N'F') IS NULL
    ALTER TABLE dbo.BookingReceipts ADD CONSTRAINT FK_BookingReceipts_BookingPayment
        FOREIGN KEY (BookingPaymentId) REFERENCES dbo.BookingPayments(BookingPaymentId) ON DELETE NO ACTION;
GO

IF OBJECT_ID(N'dbo.TermsVersions', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.TermsVersions WHERE VersionCode = 'v1.0')
BEGIN
    INSERT INTO dbo.TermsVersions (VersionCode, Title, EffectiveDate, DeliveryBusinessDays, OvertimeHourlyRate, TermsBodyHtml, IsActive)
    VALUES ('v1.0', 'Standard Terms and Conditions', '2026-01-01', 7, 1500.00, '<p>Standard Terms and Conditions v1.0 - Photo Aevum Studio</p>', 1);
END;
GO