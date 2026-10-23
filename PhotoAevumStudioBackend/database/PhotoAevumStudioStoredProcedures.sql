SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

/* =====================================================================
   1. USER MANAGEMENT (Customer, Employee, Admin & SystemAdmin)
   ===================================================================== */

-- Create a new Customer
CREATE OR ALTER PROCEDURE dbo.sp_CreateCustomer
    @CustomerID nvarchar(30),
    @CustomerFirstName nvarchar(255),
    @CustomerLastName nvarchar(255),
    @CustomerMiddleName nvarchar(255) = NULL,
    @CustomerEmail nvarchar(255),
    @CustomerPhoneNumber nvarchar(30) = NULL,
    @CustomerPassword nvarchar(255)
AS
BEGIN
    SET NOCOUNT ON;
    
    INSERT INTO dbo.Customer (
        CustomerID, CustomerFirstName, CustomerLastName, CustomerMiddleName, 
        CustomerEmail, CustomerPhoneNumber, CustomerPassword
    )
    VALUES (
        @CustomerID, @CustomerFirstName, @CustomerLastName, @CustomerMiddleName, 
        @CustomerEmail, @CustomerPhoneNumber, @CustomerPassword
    );
END;
GO

-- Create a new Employee
CREATE OR ALTER PROCEDURE dbo.sp_CreateEmployee
    @EmployeeID nvarchar(30),
    @EmployeeFirstName nvarchar(255),
    @EmployeeLastName nvarchar(255),
    @EmployeeMiddleName nvarchar(255) = NULL,
    @EmployeeEmail nvarchar(255),
    @EmployeePassword nvarchar(255),
    @isFirstLogin bit = 1 -- Defaults to 1 (True) if not provided by backend
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.Employee (
        EmployeeID, EmployeeFirstName, EmployeeLastName, EmployeeMiddleName, 
        EmployeeEmail, EmployeePassword, EmployeeStatus, isFirstLogin
    )
    VALUES (
        @EmployeeID, @EmployeeFirstName, @EmployeeLastName, @EmployeeMiddleName, 
        @EmployeeEmail, @EmployeePassword, 'Active', @isFirstLogin
    );
END;
GO

-- Complete Employee First Login
CREATE OR ALTER PROCEDURE dbo.sp_CompleteEmployeeFirstLogin
    @EmployeeID nvarchar(30),
    @NewPassword nvarchar(255) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Employee
    SET isFirstLogin = 0,
        EmployeePassword = COALESCE(@NewPassword, EmployeePassword)
    WHERE EmployeeID = @EmployeeID;
END;
GO

-- Complete Admin First Login
CREATE OR ALTER PROCEDURE dbo.sp_CompleteAdminFirstLogin
    @AdminID nvarchar(30),
    @NewPassword nvarchar(255) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Admin
    SET isFirstLogin = 0,
        AdminPassword = COALESCE(@NewPassword, AdminPassword)
    WHERE AdminID = @AdminID;
END;
GO

-- Create Admin: Restricted EXCLUSIVELY to SystemAdmin role (SADM-###)
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

    DECLARE @RequesterRole nvarchar(15) = NULL;
    SELECT @RequesterRole = AdminRole FROM dbo.Admin WHERE AdminID = @RequestingUserID;

    -- Strict authorization check: Only SystemAdmin accounts can create new Admin users
    IF (@RequestingUserID NOT LIKE 'SADM-%' 
        AND ISNULL(@RequesterRole, '') NOT IN ('SystemAdmin', 'Super Admin')
        AND NOT EXISTS (SELECT 1 FROM dbo.SystemAdmin WHERE SystemAdminID = @RequestingUserID))
    BEGIN
        RAISERROR('Permission Denied: Only a SystemAdmin (SADM-###) is authorized to create new Admin accounts.', 16, 1);
        RETURN;
    END

    -- Regular Admin accounts require an Email
    IF (@AdminRole <> 'SystemAdmin' AND @AdminID NOT LIKE 'SADM-%' AND (@AdminEmail IS NULL OR LTRIM(RTRIM(@AdminEmail)) = ''))
    BEGIN
        RAISERROR('Validation Error: Email address is required for regular Admin accounts.', 16, 1);
        RETURN;
    END

    INSERT INTO dbo.Admin (
        AdminID, AdminRole, AdminFirstName, AdminLastName, AdminMiddleName, Email, AdminPassword, isFirstLogin
    )
    VALUES (
        @AdminID, @AdminRole, @AdminFirstName, @AdminLastName, @AdminMiddleName, @AdminEmail, @AdminPassword, 1
    );

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
END;
GO

/* =====================================================================
   2. EVENT & BOOKING MANAGEMENT
   ===================================================================== */

-- Create a new Event Appointment
CREATE OR ALTER PROCEDURE dbo.sp_CreateEventAppointment
    @EventAppointmentID nvarchar(30),
    @EventName nvarchar(255),
    @EventDescription nvarchar(255),
    @CustomerID nvarchar(30),
    @EventDate date,
    @EventTime time
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.EventAppointment (
        EventAppointmentID, EventName, EventDescription, CustomerID, 
        EventDate, EventTime, EventStatus
    )
    VALUES (
        @EventAppointmentID, @EventName, @EventDescription, @CustomerID, 
        @EventDate, @EventTime, 'Pending'
    );
END;
GO

-- Add a Line Item to an Event (Service, Additional Service, or Combo)
CREATE OR ALTER PROCEDURE dbo.sp_AddEventLineItem
    @EventLineItemID nvarchar(30),
    @EventAppointmentID nvarchar(30),
    @ServicesID nvarchar(30) = NULL,
    @AdditionalServicesID nvarchar(30) = NULL,
    @ComboPackageID nvarchar(30) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.EventLineItem (
        EventLineItemID, EventAppointmentID, ServicesID, AdditionalServicesID, ComboPackageID
    )
    VALUES (
        @EventLineItemID, @EventAppointmentID, @ServicesID, @AdditionalServicesID, @ComboPackageID
    );
END;
GO

-- Assign Employee to an Event
CREATE OR ALTER PROCEDURE dbo.sp_AssignEmployeeToEvent
    @EventAppointmentID nvarchar(30),
    @EmployeeID nvarchar(30)
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.EventEmployee WHERE EventAppointmentID = @EventAppointmentID AND EmployeeID = @EmployeeID)
    BEGIN
        INSERT INTO dbo.EventEmployee (EventAppointmentID, EmployeeID)
        VALUES (@EventAppointmentID, @EmployeeID);
    END
END;
GO

/* =====================================================================
   3. EQUIPMENT MANAGEMENT
   ===================================================================== */

-- Add new Equipment
CREATE OR ALTER PROCEDURE dbo.sp_AddEquipment
    @EquipmentID nvarchar(30),
    @EquipmentName nvarchar(100),
    @EquipmentTypeID nvarchar(30),
    @SerialNo nvarchar(30) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.Equipment (
        EquipmentID, EquipmentName, EquipmentTypeID, EquipmentStatus, SerialNo
    )
    VALUES (
        @EquipmentID, @EquipmentName, @EquipmentTypeID, 'Available', @SerialNo
    );
END;
GO

-- Assign Equipment to an Event
CREATE OR ALTER PROCEDURE dbo.sp_AssignEquipmentToEvent
    @EventAppointmentID nvarchar(30),
    @EquipmentID nvarchar(30)
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.EventEquipment WHERE EventAppointmentID = @EventAppointmentID AND EquipmentID = @EquipmentID)
    BEGIN
        INSERT INTO dbo.EventEquipment (EventAppointmentID, EquipmentID)
        VALUES (@EventAppointmentID, @EquipmentID);
    END
END;
GO

/* =====================================================================
   4. CATALOG & CATEGORY MANAGEMENT
   ===================================================================== */

CREATE OR ALTER PROCEDURE dbo.sp_AddServiceCategoryJoin
    @ServiceCategoryJoinID nvarchar(30),
    @ServiceCategoryID nvarchar(30),
    @ServicesID nvarchar(30) = NULL,
    @AdditionalServicesID nvarchar(30) = NULL,
    @ComboPackageID nvarchar(30) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.ServiceCategoryJoins (
        ServiceCategoryJoinID, ServiceCategoryID, ServicesID, AdditionalServicesID, ComboPackageID
    )
    VALUES (
        @ServiceCategoryJoinID, @ServiceCategoryID, @ServicesID, @AdditionalServicesID, @ComboPackageID
    );
END;
GO

/* =====================================================================
   5. SERVICE IMAGES (Summary vs Detail with Binaries)
   ===================================================================== */

-- 5.1 Main Services
CREATE OR ALTER PROCEDURE dbo.sp_GetServicesSummary
AS
BEGIN
    SET NOCOUNT ON;
    -- Crucial Rule: Do NOT load heavy binary data on initial list/grid
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

CREATE OR ALTER PROCEDURE dbo.sp_GetServiceDetailWithImage
    @ServicesID nvarchar(30)
AS
BEGIN
    SET NOCOUNT ON;
    -- Fetch heavy image binary only when user opens detail view
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
            @ActionDetails = 'Updated service hero image.',
            @ChangedByType = @ActorType, 
            @ActorID = @UpdatedByAdminID;
    END
END;
GO

-- 5.2 Additional Services
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
            @ActionDetails = 'Updated additional service image.',
            @ChangedByType = @ActorType, 
            @ActorID = @UpdatedByAdminID;
    END
END;
GO

-- 5.3 Combo Packages
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
            @ActionDetails = 'Updated combo package hero image.',
            @ChangedByType = @ActorType, 
            @ActorID = @UpdatedByAdminID;
    END
END;
GO

/* =====================================================================
   6. STATUS UPDATES & AUDIT LOGGING (HistoryLog Integration)
   ===================================================================== */

-- Add record to HistoryLog
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

-- Retrieve History Logs (Full access for SystemAdmin)
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

-- Update Event Status
CREATE OR ALTER PROCEDURE dbo.sp_UpdateEventStatus
    @EventAppointmentID nvarchar(30),
    @NewStatus nvarchar(30),
    @ChangedByType nvarchar(20),  -- 'Customer', 'Employee', 'Admin', 'SystemAdmin', or 'System'
    @ActorID nvarchar(30) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @OldStatus nvarchar(30);
    DECLARE @CustomerID nvarchar(30);
    
    SELECT @OldStatus = EventStatus, @CustomerID = CustomerID 
    FROM dbo.EventAppointment 
    WHERE EventAppointmentID = @EventAppointmentID;

    IF @OldStatus IS NULL
    BEGIN
        RAISERROR('EventAppointmentID not found.', 16, 1);
        RETURN;
    END

    IF @OldStatus <> @NewStatus
    BEGIN
        BEGIN TRY
            BEGIN TRANSACTION;

            UPDATE dbo.EventAppointment
            SET EventStatus = @NewStatus
            WHERE EventAppointmentID = @EventAppointmentID;

            DECLARE @ActionDetails nvarchar(max) = CONCAT('Status changed from ', @OldStatus, ' to ', @NewStatus);

            EXEC dbo.sp_AddHistoryLog
                @ActionType = 'STATUS_CHANGE',
                @EntityName = 'EventAppointment',
                @EntityID = @EventAppointmentID,
                @ActionDetails = @ActionDetails,
                @EventAppointmentID = @EventAppointmentID,
                @CustomerID = @CustomerID,
                @ChangedByType = @ChangedByType,
                @ActorID = @ActorID;

            COMMIT TRANSACTION;
        END TRY
        BEGIN CATCH
            ROLLBACK TRANSACTION;
            THROW;
        END CATCH
    END
END;
GO
