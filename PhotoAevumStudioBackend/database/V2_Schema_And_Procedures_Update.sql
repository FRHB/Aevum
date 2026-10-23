-- =====================================================================
-- Photo Aevum Studio - Database Schema & Stored Procedure V2 Updates
-- Compatibility: Microsoft SQL Server (T-SQL)
-- =====================================================================

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

/* =====================================================================
   1. TABLE SCHEMA UPDATES
   ===================================================================== */

-- 1.1 Admin Table: Add Email Column (Nullable, with Unique Index)
IF COL_LENGTH(N'dbo.Admin', N'Email') IS NULL
BEGIN
    ALTER TABLE dbo.Admin ADD Email nvarchar(255) NULL;
    PRINT 'Added Email column to dbo.Admin.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Admin_Email' AND object_id = OBJECT_ID(N'dbo.Admin'))
BEGIN
    CREATE UNIQUE INDEX UX_Admin_Email ON dbo.Admin(Email) WHERE Email IS NOT NULL;
    PRINT 'Created unique filtered index UX_Admin_Email on dbo.Admin.';
END
GO

-- 1.2 SystemAdmin Table: Role/Table does NOT require an email field
IF OBJECT_ID(N'dbo.SystemAdmin', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SystemAdmin (
        SystemAdminID nvarchar(30)  NOT NULL PRIMARY KEY,
        FirstName     nvarchar(255) NOT NULL,
        LastName      nvarchar(255) NOT NULL,
        MiddleName    nvarchar(255) NULL,
        PasswordHash  nvarchar(255) NOT NULL,
        CreatedAt     datetime2     NOT NULL CONSTRAINT DF_SystemAdmin_CreatedAt DEFAULT SYSUTCDATETIME()
        -- SystemAdmin does not require email; authentication uses SystemAdminID credentials.
    );
    PRINT 'Created table dbo.SystemAdmin.';
END
GO

-- 1.3 Service Images (Longblob / VARBINARY(MAX))
-- Services
IF COL_LENGTH(N'dbo.Services', N'ServiceImage') IS NULL
BEGIN
    ALTER TABLE dbo.Services ADD ServiceImage varbinary(max) NULL;
    PRINT 'Added ServiceImage column to dbo.Services.';
END
GO
IF COL_LENGTH(N'dbo.Services', N'ServiceImageMimeType') IS NULL
BEGIN
    ALTER TABLE dbo.Services ADD ServiceImageMimeType nvarchar(100) NULL CONSTRAINT DF_Services_MimeType DEFAULT 'image/jpeg';
    PRINT 'Added ServiceImageMimeType column to dbo.Services.';
END
GO

-- AdditionalServices
IF COL_LENGTH(N'dbo.AdditionalServices', N'AdditionalServiceImage') IS NULL
BEGIN
    ALTER TABLE dbo.AdditionalServices ADD AdditionalServiceImage varbinary(max) NULL;
    PRINT 'Added AdditionalServiceImage column to dbo.AdditionalServices.';
END
GO
IF COL_LENGTH(N'dbo.AdditionalServices', N'AdditionalServiceImageMimeType') IS NULL
BEGIN
    ALTER TABLE dbo.AdditionalServices ADD AdditionalServiceImageMimeType nvarchar(100) NULL CONSTRAINT DF_AdditionalServices_MimeType DEFAULT 'image/jpeg';
    PRINT 'Added AdditionalServiceImageMimeType column to dbo.AdditionalServices.';
END
GO

-- ComboPackage
IF COL_LENGTH(N'dbo.ComboPackage', N'ComboPackageImage') IS NULL
BEGIN
    ALTER TABLE dbo.ComboPackage ADD ComboPackageImage varbinary(max) NULL;
    PRINT 'Added ComboPackageImage column to dbo.ComboPackage.';
END
GO
IF COL_LENGTH(N'dbo.ComboPackage', N'ComboPackageImageMimeType') IS NULL
BEGIN
    ALTER TABLE dbo.ComboPackage ADD ComboPackageImageMimeType nvarchar(100) NULL CONSTRAINT DF_ComboPackage_MimeType DEFAULT 'image/jpeg';
    PRINT 'Added ComboPackageImageMimeType column to dbo.ComboPackage.';
END
GO

-- 1.4 HistoryLog Schema Updates: Support SystemAdmin, broadened ActionTypes and Administrative tracking
IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
BEGIN
    -- Drop old constraints if they restrict SystemAdmin or new actions
    IF OBJECT_ID(N'dbo.CK_HistoryLog_ActionType', N'C') IS NOT NULL
        ALTER TABLE dbo.HistoryLog DROP CONSTRAINT CK_HistoryLog_ActionType;

    IF OBJECT_ID(N'dbo.CK_HistoryLog_ChangedByType', N'C') IS NOT NULL
        ALTER TABLE dbo.HistoryLog DROP CONSTRAINT CK_HistoryLog_ChangedByType;

    IF OBJECT_ID(N'dbo.CK_HistoryLog_Actor', N'C') IS NOT NULL
        ALTER TABLE dbo.HistoryLog DROP CONSTRAINT CK_HistoryLog_Actor;

    IF OBJECT_ID(N'dbo.CK_HistoryLog_AdminScope', N'C') IS NOT NULL
        ALTER TABLE dbo.HistoryLog DROP CONSTRAINT CK_HistoryLog_AdminScope;

    -- Alter column ChangedByType to hold longer names (e.g. 'SystemAdmin')
    ALTER TABLE dbo.HistoryLog ALTER COLUMN ChangedByType nvarchar(20) NOT NULL;

    -- Re-apply expanded constraints
    ALTER TABLE dbo.HistoryLog ADD CONSTRAINT CK_HistoryLog_ActionType
        CHECK (ActionType IN ('INSERT','UPDATE','DELETE','NEGOTIATE','STATUS_CHANGE','LOGIN','LOGOUT','PASSWORD_CHANGE','PASSWORD_RESET','IMAGE_UPLOAD'));

    ALTER TABLE dbo.HistoryLog ADD CONSTRAINT CK_HistoryLog_ChangedByType
        CHECK (ChangedByType IN ('Customer','Employee','Admin','SystemAdmin','System'));

    ALTER TABLE dbo.HistoryLog ADD CONSTRAINT CK_HistoryLog_Actor CHECK (
        (ChangedByType = 'Customer' AND ChangedByCustomerID IS NOT NULL AND ChangedByEmployeeID IS NULL AND ChangedByAdminID IS NULL)
     OR (ChangedByType = 'Employee' AND ChangedByEmployeeID IS NOT NULL AND ChangedByCustomerID IS NULL AND ChangedByAdminID IS NULL)
     OR (ChangedByType IN ('Admin', 'SystemAdmin') AND ChangedByAdminID IS NOT NULL AND ChangedByCustomerID IS NULL AND ChangedByEmployeeID IS NULL)
     OR (ChangedByType = 'System'   AND ChangedByCustomerID IS NULL     AND ChangedByEmployeeID IS NULL AND ChangedByAdminID IS NULL));

    PRINT 'Updated constraints on dbo.HistoryLog to support SystemAdmin and full administrative tracking.';
END
GO


/* =====================================================================
   2. STORED PROCEDURE UPDATES
   ===================================================================== */

-- 2.1 Admin Creation Procedure: Restricted EXCLUSIVELY to SystemAdmin
CREATE OR ALTER PROCEDURE dbo.sp_CreateAdmin
    @AdminID          nvarchar(30),
    @AdminRole        nvarchar(15),
    @AdminFirstName   nvarchar(255),
    @AdminLastName    nvarchar(255),
    @AdminMiddleName  nvarchar(255) = NULL,
    @AdminEmail       nvarchar(255) = NULL,
    @AdminPassword    nvarchar(255),
    @RequestingUserID nvarchar(30)
AS
BEGIN
    SET NOCOUNT ON;

    -- Verify requester is SystemAdmin (SADM-### format or role 'SystemAdmin'/'Super Admin')
    DECLARE @RequesterRole nvarchar(15) = NULL;
    SELECT @RequesterRole = AdminRole FROM dbo.Admin WHERE AdminID = @RequestingUserID;

    IF (@RequestingUserID NOT LIKE 'SADM-%' 
        AND ISNULL(@RequesterRole, '') NOT IN ('SystemAdmin', 'Super Admin')
        AND NOT EXISTS (SELECT 1 FROM dbo.SystemAdmin WHERE SystemAdminID = @RequestingUserID))
    BEGIN
        RAISERROR('Permission Denied: Only a SystemAdmin (SADM-###) is authorized to create new Admin accounts.', 16, 1);
        RETURN;
    END

    -- If creating a regular Admin account, Email is required
    IF (@AdminRole <> 'SystemAdmin' AND @AdminID NOT LIKE 'SADM-%' AND (@AdminEmail IS NULL OR LTRIM(RTRIM(@AdminEmail)) = ''))
    BEGIN
        RAISERROR('Validation Error: Email address is required for regular Admin accounts.', 16, 1);
        RETURN;
    END

    -- Insert into dbo.Admin
    INSERT INTO dbo.Admin (
        AdminID, AdminRole, AdminFirstName, AdminLastName, AdminMiddleName, Email, AdminPassword, isFirstLogin
    )
    VALUES (
        @AdminID, @AdminRole, @AdminFirstName, @AdminLastName, @AdminMiddleName, @AdminEmail, @AdminPassword, 1
    );

    -- If this is a SystemAdmin, ensure record in dbo.SystemAdmin
    IF (@AdminRole = 'SystemAdmin' OR @AdminID LIKE 'SADM-%')
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM dbo.SystemAdmin WHERE SystemAdminID = @AdminID)
        BEGIN
            INSERT INTO dbo.SystemAdmin (SystemAdminID, FirstName, LastName, MiddleName, PasswordHash)
            VALUES (@AdminID, @AdminFirstName, @AdminLastName, @AdminMiddleName, @AdminPassword);
        END
    END

    -- Log action in HistoryLog
    INSERT INTO dbo.HistoryLog (
        ActionType, EntityName, EntityID, ActionDetails, ChangedByType, ChangedByAdminID
    )
    VALUES (
        'INSERT', 'Admin', @AdminID, CONCAT('Created new Admin user ', @AdminID, ' (Role: ', @AdminRole, ')'),
        'SystemAdmin', @RequestingUserID
    );

    PRINT 'Admin account created and logged successfully.';
END;
GO

-- 2.2 History Logging Procedures
CREATE OR ALTER PROCEDURE dbo.sp_AddHistoryLog
    @ActionType          nvarchar(20),
    @EntityName          nvarchar(128),
    @EntityID            nvarchar(30)  = NULL,
    @ActionDetails       nvarchar(max) = NULL,
    @EventAppointmentID  nvarchar(30)  = NULL,
    @NegotiationID       nvarchar(30)  = NULL,
    @CustomerID          nvarchar(30)  = NULL,
    @EmployeeID          nvarchar(30)  = NULL,
    @ChangedByType       nvarchar(20), -- 'Customer', 'Employee', 'Admin', 'SystemAdmin', 'System'
    @ActorID             nvarchar(30)  = NULL
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @ChangedByCustomer nvarchar(30) = NULL;
    DECLARE @ChangedByEmployee nvarchar(30) = NULL;
    DECLARE @ChangedByAdmin    nvarchar(30) = NULL;

    IF @ChangedByType = 'Customer' SET @ChangedByCustomer = @ActorID;
    ELSE IF @ChangedByType = 'Employee' SET @ChangedByEmployee = @ActorID;
    ELSE IF @ChangedByType IN ('Admin', 'SystemAdmin') SET @ChangedByAdmin = @ActorID;

    INSERT INTO dbo.HistoryLog (
        ActionType, EntityName, EntityID, ActionDetails,
        EventAppointmentID, NegotiationID, CustomerID, EmployeeID,
        ChangedByType, ChangedByCustomerID, ChangedByEmployeeID, ChangedByAdminID
    )
    VALUES (
        @ActionType, @EntityName, @EntityID, @ActionDetails,
        @EventAppointmentID, @NegotiationID, @CustomerID, @EmployeeID,
        @ChangedByType, @ChangedByCustomer, @ChangedByEmployee, @ChangedByAdmin
    );
END;
GO

CREATE OR ALTER PROCEDURE dbo.sp_GetHistoryLogs
    @RequestingUserID nvarchar(30),
    @TopCount         int = 100
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @IsSystemAdmin bit = 0;
    IF (@RequestingUserID LIKE 'SADM-%' 
        OR EXISTS (SELECT 1 FROM dbo.Admin WHERE AdminID = @RequestingUserID AND AdminRole IN ('SystemAdmin', 'Super Admin'))
        OR EXISTS (SELECT 1 FROM dbo.SystemAdmin WHERE SystemAdminID = @RequestingUserID))
    BEGIN
        SET @IsSystemAdmin = 1;
    END

    IF @IsSystemAdmin = 1
    BEGIN
        -- System Admins have full access to view all system activity logs
        SELECT TOP (@TopCount)
            HistoryLogID,
            ActionType,
            EntityName,
            EntityID,
            ActionDetails,
            EventAppointmentID,
            NegotiationID,
            CustomerID,
            EmployeeID,
            ChangedByType,
            ChangedBy,
            LoggedAt
        FROM dbo.HistoryLog
        ORDER BY LoggedAt DESC;
    END
    ELSE
    BEGIN
        -- Regular Admins see operational & resource logs
        SELECT TOP (@TopCount)
            HistoryLogID,
            ActionType,
            EntityName,
            EntityID,
            ActionDetails,
            EventAppointmentID,
            NegotiationID,
            CustomerID,
            EmployeeID,
            ChangedByType,
            ChangedBy,
            LoggedAt
        FROM dbo.HistoryLog
        WHERE ChangedByAdminID = @RequestingUserID
           OR EntityName IN ('Services', 'AdditionalServices', 'ComboPackage', 'Equipment', 'EventAppointment')
        ORDER BY LoggedAt DESC;
    END
END;
GO

-- 2.3 Service Images Retrieval & Update Stored Procedures
-- CRUCIAL RULE: Do not load heavy image binaries on initial grid/table view.
-- Summary procedures return lightweight metadata and a HasImage bit indicator.

-- Services Summary (Lightweight)
CREATE OR ALTER PROCEDURE dbo.sp_GetServicesSummary
AS
BEGIN
    SET NOCOUNT ON;
    SELECT 
        ServicesID, 
        ServiceName, 
        ServicesDescription, 
        ServicePrice,
        CAST(CASE WHEN ServiceImage IS NOT NULL THEN 1 ELSE 0 END AS bit) AS HasImage
    FROM dbo.Services
    ORDER BY ServicesID;
END;
GO

-- Service Detailed View with Image (Fetched only on explicit detail click)
CREATE OR ALTER PROCEDURE dbo.sp_GetServiceDetailWithImage
    @ServicesID nvarchar(30)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT 
        ServicesID, 
        ServiceName, 
        ServicesDescription, 
        ServicePrice,
        ServiceImage,
        ISNULL(ServiceImageMimeType, 'image/jpeg') AS ServiceImageMimeType,
        CAST(CASE WHEN ServiceImage IS NOT NULL THEN 1 ELSE 0 END AS bit) AS HasImage
    FROM dbo.Services
    WHERE ServicesID = @ServicesID;
END;
GO

-- Update Service Image
CREATE OR ALTER PROCEDURE dbo.sp_UpdateServiceImage
    @ServicesID           nvarchar(30),
    @ServiceImage         varbinary(max),
    @ServiceImageMimeType nvarchar(100) = 'image/jpeg',
    @UpdatedByAdminID     nvarchar(30)  = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Services
    SET ServiceImage = @ServiceImage,
        ServiceImageMimeType = @ServiceImageMimeType
    WHERE ServicesID = @ServicesID;

    IF @UpdatedByAdminID IS NOT NULL
    BEGIN
        DECLARE @ActorType nvarchar(20) = CASE WHEN @UpdatedByAdminID LIKE 'SADM-%' THEN 'SystemAdmin' ELSE 'Admin' END;
        EXEC dbo.sp_AddHistoryLog 
            @ActionType = 'IMAGE_UPLOAD', 
            @EntityName = 'Services', 
            @EntityID = @ServicesID, 
            @ActionDetails = 'Service image attached/updated via Image Wizard.',
            @ChangedByType = @ActorType, 
            @ActorID = @UpdatedByAdminID;
    END
END;
GO

-- Additional Services Summary (Lightweight)
CREATE OR ALTER PROCEDURE dbo.sp_GetAdditionalServicesSummary
AS
BEGIN
    SET NOCOUNT ON;
    SELECT 
        AdditionalServicesID, 
        AdditionalServiceName, 
        AdditionalServicesDescription,
        CAST(CASE WHEN AdditionalServiceImage IS NOT NULL THEN 1 ELSE 0 END AS bit) AS HasImage
    FROM dbo.AdditionalServices
    ORDER BY AdditionalServicesID;
END;
GO

-- Additional Service Detailed View with Image
CREATE OR ALTER PROCEDURE dbo.sp_GetAdditionalServiceDetailWithImage
    @AdditionalServicesID nvarchar(30)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT 
        AdditionalServicesID, 
        AdditionalServiceName, 
        AdditionalServicesDescription,
        AdditionalServiceImage,
        ISNULL(AdditionalServiceImageMimeType, 'image/jpeg') AS AdditionalServiceImageMimeType,
        CAST(CASE WHEN AdditionalServiceImage IS NOT NULL THEN 1 ELSE 0 END AS bit) AS HasImage
    FROM dbo.AdditionalServices
    WHERE AdditionalServicesID = @AdditionalServicesID;
END;
GO

-- Update Additional Service Image
CREATE OR ALTER PROCEDURE dbo.sp_UpdateAdditionalServiceImage
    @AdditionalServicesID           nvarchar(30),
    @AdditionalServiceImage         varbinary(max),
    @AdditionalServiceImageMimeType nvarchar(100) = 'image/jpeg',
    @UpdatedByAdminID               nvarchar(30)  = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.AdditionalServices
    SET AdditionalServiceImage = @AdditionalServiceImage,
        AdditionalServiceImageMimeType = @AdditionalServiceImageMimeType
    WHERE AdditionalServicesID = @AdditionalServicesID;

    IF @UpdatedByAdminID IS NOT NULL
    BEGIN
        DECLARE @ActorType nvarchar(20) = CASE WHEN @UpdatedByAdminID LIKE 'SADM-%' THEN 'SystemAdmin' ELSE 'Admin' END;
        EXEC dbo.sp_AddHistoryLog 
            @ActionType = 'IMAGE_UPLOAD', 
            @EntityName = 'AdditionalServices', 
            @EntityID = @AdditionalServicesID, 
            @ActionDetails = 'Additional service image attached/updated via Image Wizard.',
            @ChangedByType = @ActorType, 
            @ActorID = @UpdatedByAdminID;
    END
END;
GO

-- Combo Package Summary (Lightweight)
CREATE OR ALTER PROCEDURE dbo.sp_GetComboPackagesSummary
AS
BEGIN
    SET NOCOUNT ON;
    SELECT 
        ComboPackageID, 
        ComboPackageName, 
        ComboPackageDescription,
        CAST(CASE WHEN ComboPackageImage IS NOT NULL THEN 1 ELSE 0 END AS bit) AS HasImage
    FROM dbo.ComboPackage
    ORDER BY ComboPackageID;
END;
GO

-- Combo Package Detailed View with Image
CREATE OR ALTER PROCEDURE dbo.sp_GetComboPackageDetailWithImage
    @ComboPackageID nvarchar(30)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT 
        ComboPackageID, 
        ComboPackageName, 
        ComboPackageDescription,
        ComboPackageImage,
        ISNULL(ComboPackageImageMimeType, 'image/jpeg') AS ComboPackageImageMimeType,
        CAST(CASE WHEN ComboPackageImage IS NOT NULL THEN 1 ELSE 0 END AS bit) AS HasImage
    FROM dbo.ComboPackage
    WHERE ComboPackageID = @ComboPackageID;
END;
GO

-- Update Combo Package Image
CREATE OR ALTER PROCEDURE dbo.sp_UpdateComboPackageImage
    @ComboPackageID           nvarchar(30),
    @ComboPackageImage         varbinary(max),
    @ComboPackageImageMimeType nvarchar(100) = 'image/jpeg',
    @UpdatedByAdminID         nvarchar(30)  = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.ComboPackage
    SET ComboPackageImage = @ComboPackageImage,
        ComboPackageImageMimeType = @ComboPackageImageMimeType
    WHERE ComboPackageID = @ComboPackageID;

    IF @UpdatedByAdminID IS NOT NULL
    BEGIN
        DECLARE @ActorType nvarchar(20) = CASE WHEN @UpdatedByAdminID LIKE 'SADM-%' THEN 'SystemAdmin' ELSE 'Admin' END;
        EXEC dbo.sp_AddHistoryLog 
            @ActionType = 'IMAGE_UPLOAD', 
            @EntityName = 'ComboPackage', 
            @EntityID = @ComboPackageID, 
            @ActionDetails = 'Combo package image attached/updated via Image Wizard.',
            @ChangedByType = @ActorType, 
            @ActorID = @UpdatedByAdminID;
    END
END;
GO
