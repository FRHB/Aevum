SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

/* =====================================================================
   1. USER MANAGEMENT (Customer, Employee, Admin & SystemAdmin)
   ===================================================================== */

CREATE OR ALTER PROCEDURE dbo.sp_CreateCustomer
    @CustomerFirstName nvarchar(255),
    @CustomerLastName nvarchar(255),
    @CustomerMiddleName nvarchar(255) = NULL,
    @CustomerEmail nvarchar(255),
    @CustomerPhoneNumber nvarchar(30) = NULL,
    @CustomerPassword nvarchar(255),
    @CustomerID nvarchar(30) = NULL,
    @GeneratedCustomerID nvarchar(30) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    
    BEGIN TRY
        BEGIN TRANSACTION;
        
        DECLARE @NewID NVARCHAR(30) = NULLIF(LTRIM(RTRIM(@CustomerID)), '');
        IF @NewID IS NULL
        BEGIN
            DECLARE @SeqVal INT = NEXT VALUE FOR dbo.Seq_CustomerID;
            SET @NewID = 'CUST-' + RIGHT('0000' + CAST(@SeqVal AS VARCHAR(10)), 4);
        END
        
        WHILE EXISTS (SELECT 1 FROM dbo.Customer WITH (UPDLOCK, HOLDLOCK) WHERE CustomerID = @NewID)
        BEGIN
            DECLARE @NextSeqVal INT = NEXT VALUE FOR dbo.Seq_CustomerID;
            SET @NewID = 'CUST-' + RIGHT('0000' + CAST(@NextSeqVal AS VARCHAR(10)), 4);
        END
        
        INSERT INTO dbo.Customer (
            CustomerID, CustomerFirstName, CustomerLastName, CustomerMiddleName, 
            CustomerEmail, CustomerPhoneNumber, CustomerPassword, CustomerStatus, CreatedAt
        )
        VALUES (
            @NewID, @CustomerFirstName, @CustomerLastName, @CustomerMiddleName, 
            @CustomerEmail, @CustomerPhoneNumber, @CustomerPassword, 'Active', SYSUTCDATETIME()
        );
        
        IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
        BEGIN
            INSERT INTO dbo.HistoryLog (
                ActionType, EntityName, EntityID, ActionDetails, ChangedByType, LoggedAt
            )
            VALUES (
                'INSERT', 'Customer', @NewID, 'Customer account registered via automated ID generation.', 'System', SYSUTCDATETIME()
            );
        END
        
        COMMIT TRANSACTION;
        
        SET @GeneratedCustomerID = @NewID;
        SELECT @NewID AS CustomerID;
        
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

CREATE OR ALTER PROCEDURE dbo.sp_CreateEmployee
    @EmployeeFirstName nvarchar(255),
    @EmployeeLastName nvarchar(255),
    @EmployeeMiddleName nvarchar(255) = NULL,
    @EmployeeEmail nvarchar(255),
    @EmployeePassword nvarchar(255),
    @isFirstLogin bit = 1,
    @CreatedByAdminID nvarchar(30) = NULL,
    @EmployeeID nvarchar(30) = NULL,
    @GeneratedEmployeeID nvarchar(30) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    
    BEGIN TRY
        BEGIN TRANSACTION;
        
        DECLARE @NewID NVARCHAR(30) = NULLIF(LTRIM(RTRIM(@EmployeeID)), '');
        IF @NewID IS NULL
        BEGIN
            DECLARE @SeqVal INT = NEXT VALUE FOR dbo.Seq_EmployeeID;
            SET @NewID = 'EMP-' + RIGHT('000' + CAST(@SeqVal AS VARCHAR(10)), 3);
        END
        
        WHILE EXISTS (SELECT 1 FROM dbo.Employee WITH (UPDLOCK, HOLDLOCK) WHERE EmployeeID = @NewID)
        BEGIN
            DECLARE @NextSeqVal INT = NEXT VALUE FOR dbo.Seq_EmployeeID;
            SET @NewID = 'EMP-' + RIGHT('000' + CAST(@NextSeqVal AS VARCHAR(10)), 3);
        END
        
        INSERT INTO dbo.Employee (
            EmployeeID, EmployeeFirstName, EmployeeLastName, EmployeeMiddleName, 
            EmployeeEmail, EmployeePassword, EmployeeStatus, isFirstLogin, CreatedAt
        )
        VALUES (
            @NewID, @EmployeeFirstName, @EmployeeLastName, @EmployeeMiddleName, 
            @EmployeeEmail, @EmployeePassword, 'Active', @isFirstLogin, SYSUTCDATETIME()
        );
        
        IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL AND @CreatedByAdminID IS NOT NULL
        BEGIN
            DECLARE @ActorType nvarchar(20) = CASE WHEN @CreatedByAdminID LIKE 'SADM-%' THEN 'SystemAdmin' ELSE 'Admin' END;
            INSERT INTO dbo.HistoryLog (
                ActionType, EntityName, EntityID, ActionDetails, ChangedByType, ChangedByAdminID, LoggedAt
            )
            VALUES (
                'INSERT', 'Employee', @NewID, 'Employee account created via automated ID generation.', @ActorType, @CreatedByAdminID, SYSUTCDATETIME()
            );
        END
        
        COMMIT TRANSACTION;
        
        SET @GeneratedEmployeeID = @NewID;
        SELECT @NewID AS EmployeeID;
        
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

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

    IF (@RequestingUserID NOT LIKE 'SADM-%' 
        AND ISNULL(@RequesterRole, '') NOT IN ('SystemAdmin', 'Super Admin')
        AND NOT EXISTS (SELECT 1 FROM dbo.SystemAdmin WHERE SystemAdminID = @RequestingUserID))
    BEGIN
        RAISERROR('Permission Denied: Only a SystemAdmin (SADM-###) is authorized to create new Admin accounts.', 16, 1);
        RETURN;
    END

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

    INSERT INTO dbo.HistoryLog (
        ActionType, EntityName, EntityID, ActionDetails, ChangedByType, ChangedByAdminID
    )
    VALUES (
        'INSERT', 'Admin', @AdminID, CONCAT('Created new Admin user ', @AdminID, ' (Role: ', @AdminRole, ')'),
        'SystemAdmin', @RequestingUserID
    );
END;
GO

CREATE OR ALTER PROCEDURE dbo.sp_UpdateAdmin
    @AdminID          nvarchar(30),
    @AdminFirstName   nvarchar(255),
    @AdminLastName    nvarchar(255),
    @AdminMiddleName  nvarchar(255) = NULL,
    @AdminEmail       nvarchar(255) = NULL,
    @RequestingUserID nvarchar(30)
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @RequesterRole nvarchar(20) = NULL;
    SELECT @RequesterRole = AdminRole FROM dbo.Admin WHERE AdminID = @RequestingUserID;
    
    DECLARE @IsSystemAdmin bit = 0;
    IF (@RequestingUserID LIKE 'SADM-%' 
        OR ISNULL(@RequesterRole, '') IN ('SystemAdmin', 'Super Admin')
        OR EXISTS (SELECT 1 FROM dbo.SystemAdmin WHERE SystemAdminID = @RequestingUserID))
    BEGIN
        SET @IsSystemAdmin = 1;
    END

    IF @IsSystemAdmin = 0
    BEGIN
        RAISERROR('Permission Denied: Admins have read-only access and cannot edit administrator records.', 16, 1);
        RETURN;
    END

    DECLARE @TargetRole nvarchar(20) = NULL;
    SELECT @TargetRole = AdminRole FROM dbo.Admin WHERE AdminID = @AdminID;

    IF (@TargetRole IN ('SystemAdmin', 'Super Admin') OR @AdminID LIKE 'SADM-%')
    BEGIN
        IF UPPER(@AdminID) <> UPPER(@RequestingUserID)
        BEGIN
            RAISERROR('Permission Denied: Cannot edit the data of other System Admin users.', 16, 1);
            RETURN;
        END
    END

    UPDATE dbo.Admin
    SET AdminFirstName = @AdminFirstName,
        AdminLastName = @AdminLastName,
        AdminMiddleName = @AdminMiddleName,
        Email = @AdminEmail
    WHERE AdminID = @AdminID;

    IF EXISTS (SELECT 1 FROM dbo.SystemAdmin WHERE SystemAdminID = @AdminID)
    BEGIN
        UPDATE dbo.SystemAdmin
        SET FirstName = @AdminFirstName,
            LastName = @AdminLastName,
            MiddleName = @AdminMiddleName
        WHERE SystemAdminID = @AdminID;
    END

    INSERT INTO dbo.HistoryLog (
        ActionType, EntityName, EntityID, ActionDetails, ChangedByType, ChangedByAdminID
    )
    VALUES (
        'UPDATE', 'Admin', @AdminID, CONCAT('Updated Admin details for ', @AdminID),
        'SystemAdmin', @RequestingUserID
    );
END;
GO

/* =====================================================================
   2. EVENT & BOOKING MANAGEMENT
   ===================================================================== */

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

CREATE OR ALTER PROCEDURE dbo.sp_UpdateEventStatus
    @EventAppointmentID nvarchar(30),
    @NewStatus nvarchar(30),
    @ChangedByType nvarchar(20), 
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

/* =====================================================================
   3. EQUIPMENT & EQUIPMENT TYPE MANAGEMENT
   ===================================================================== */

CREATE OR ALTER PROCEDURE dbo.sp_GetNextEquipmentTypeID
    @NextID NVARCHAR(30) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @SeqVal INT = NEXT VALUE FOR dbo.Seq_EquipmentType;

    WHILE EXISTS (SELECT 1 FROM dbo.EquipmentType WHERE EquipmentTypeID = 'ETYP-' + RIGHT('0000' + CAST(@SeqVal AS VARCHAR(10)), 4))
    BEGIN
        SET @SeqVal = NEXT VALUE FOR dbo.Seq_EquipmentType;
    END

    SET @NextID = 'ETYP-' + RIGHT('0000' + CAST(@SeqVal AS VARCHAR(10)), 4);
    SELECT @NextID AS NextEquipmentTypeID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.sp_CreateEquipmentType
    @EquipmentTypeName nvarchar(255),
    @EquipmentTypeID nvarchar(30) = NULL,
    @CreatedByAdminID nvarchar(30) = NULL,
    @GeneratedID nvarchar(30) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        IF EXISTS (
            SELECT 1 FROM dbo.EquipmentType 
            WHERE LOWER(RTRIM(LTRIM(EquipmentTypeName))) = LOWER(RTRIM(LTRIM(@EquipmentTypeName)))
        )
        BEGIN
            THROW 51001, 'An Equipment Type with this name already exists in the catalog.', 1;
        END

        DECLARE @NewID NVARCHAR(30) = @EquipmentTypeID;
        IF @NewID IS NULL OR LTRIM(RTRIM(@NewID)) = ''
        BEGIN
            DECLARE @SeqVal INT = NEXT VALUE FOR dbo.Seq_EquipmentType;
            SET @NewID = 'ETYP-' + RIGHT('0000' + CAST(@SeqVal AS VARCHAR(10)), 4);
        END

        WHILE EXISTS (SELECT 1 FROM dbo.EquipmentType WITH (UPDLOCK, HOLDLOCK) WHERE EquipmentTypeID = @NewID)
        BEGIN
            DECLARE @NextSeqVal INT = NEXT VALUE FOR dbo.Seq_EquipmentType;
            SET @NewID = 'ETYP-' + RIGHT('0000' + CAST(@NextSeqVal AS VARCHAR(10)), 4);
        END

        INSERT INTO dbo.EquipmentType (EquipmentTypeID, EquipmentTypeName)
        VALUES (@NewID, RTRIM(LTRIM(@EquipmentTypeName)));

        IF @CreatedByAdminID IS NOT NULL AND OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
        BEGIN
            INSERT INTO dbo.HistoryLog (
                ActionType, EntityName, EntityID, ActionDetails, ChangedByType, ChangedByAdminID, LoggedAt
            )
            VALUES (
                'INSERT', 'EquipmentType', @NewID, 'Created new Equipment Type with atomic ETYP-#### ID generation.', 'Admin', @CreatedByAdminID, SYSUTCDATETIME()
            );
        END

        COMMIT TRANSACTION;
        SET @GeneratedID = @NewID;

        SELECT EquipmentTypeID, EquipmentTypeName FROM dbo.EquipmentType WHERE EquipmentTypeID = @NewID;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

CREATE OR ALTER PROCEDURE dbo.sp_GetEquipmentTypes
AS
BEGIN
    SET NOCOUNT ON;
    SELECT EquipmentTypeID, EquipmentTypeName
    FROM dbo.EquipmentType
    ORDER BY EquipmentTypeName;
END;
GO

CREATE OR ALTER PROCEDURE dbo.sp_GetNextEquipmentIdPreview
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @NextVal INT;

    SELECT @NextVal = CAST(current_value AS INT)
    FROM sys.sequences
    WHERE name = 'EquipmentIdSequence' AND schema_id = SCHEMA_ID('dbo');

    IF @NextVal IS NULL
    BEGIN
        SELECT @NextVal = CAST(start_value AS INT)
        FROM sys.sequences
        WHERE name = 'EquipmentIdSequence' AND schema_id = SCHEMA_ID('dbo');
    END
    ELSE
    BEGIN
        SELECT @NextVal = @NextVal + CAST(increment AS INT)
        FROM sys.sequences
        WHERE name = 'EquipmentIdSequence' AND schema_id = SCHEMA_ID('dbo');
    END;

    DECLARE @MaxInTable INT = 0;
    SELECT @MaxInTable = ISNULL(MAX(TRY_CAST(SUBSTRING(EquipmentID, 5, 10) AS INT)), 0)
    FROM dbo.Equipment
    WHERE EquipmentID LIKE 'EQP-%';

    IF @MaxInTable >= @NextVal
    BEGIN
        SET @NextVal = @MaxInTable + 1;
    END;

    SELECT 'EQP-' + RIGHT('0000' + CAST(@NextVal AS VARCHAR(10)), 4) AS NextEquipmentID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.sp_CreateEquipmentWithCustomId
    @EquipmentName         NVARCHAR(100),
    @EquipmentDescription  NVARCHAR(1000) = NULL,
    @EquipmentTypeID       NVARCHAR(30),
    @SerialNo              NVARCHAR(30)   = NULL,
    @ImagePath             NVARCHAR(500)  = NULL,
    @CreatedByAdminID      NVARCHAR(30)   = 'ADM-001',
    @AssignedEquipmentID   NVARCHAR(30)   = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SET @EquipmentName = RTRIM(LTRIM(@EquipmentName));
    SET @EquipmentDescription = NULLIF(RTRIM(LTRIM(@EquipmentDescription)), '');
    SET @EquipmentTypeID = RTRIM(LTRIM(@EquipmentTypeID));
    SET @SerialNo = NULLIF(RTRIM(LTRIM(@SerialNo)), '');
    SET @ImagePath = NULLIF(RTRIM(LTRIM(@ImagePath)), '');

    IF @EquipmentName IS NULL OR LEN(@EquipmentName) < 2
    BEGIN
        RAISERROR('Equipment Name is required and must be at least 2 characters.', 16, 1);
        RETURN -1;
    END;

    IF @EquipmentTypeID IS NULL OR LEN(@EquipmentTypeID) = 0
    BEGIN
        RAISERROR('Equipment Type is required.', 16, 1);
        RETURN -2;
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.EquipmentType WHERE EquipmentTypeID = @EquipmentTypeID)
    BEGIN
        INSERT INTO dbo.EquipmentType (EquipmentTypeID, EquipmentTypeName)
        VALUES (@EquipmentTypeID, @EquipmentTypeID);
    END;

    BEGIN TRANSACTION;

    BEGIN TRY
        IF @SerialNo IS NOT NULL
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.Equipment WITH (UPDLOCK, HOLDLOCK) WHERE SerialNo = @SerialNo)
            BEGIN
                RAISERROR('An equipment item with Serial Number ''%s'' already exists in inventory.', 16, 1, @SerialNo);
            END;
        END;

        DECLARE @NextSequence INT = NEXT VALUE FOR dbo.EquipmentIdSequence;
        SET @AssignedEquipmentID = 'EQP-' + RIGHT('0000' + CAST(@NextSequence AS VARCHAR(10)), 4);

        WHILE EXISTS (SELECT 1 FROM dbo.Equipment WITH (UPDLOCK, HOLDLOCK) WHERE EquipmentID = @AssignedEquipmentID)
        BEGIN
            SET @NextSequence = NEXT VALUE FOR dbo.EquipmentIdSequence;
            SET @AssignedEquipmentID = 'EQP-' + RIGHT('0000' + CAST(@NextSequence AS VARCHAR(10)), 4);
        END;

        INSERT INTO dbo.Equipment (
            EquipmentID, EquipmentName, EquipmentDescription, EquipmentTypeID, EquipmentStatus, SerialNo, ImagePath, CreatedAt
        )
        VALUES (
            @AssignedEquipmentID, @EquipmentName, @EquipmentDescription, @EquipmentTypeID, 'Available', @SerialNo, @ImagePath, SYSUTCDATETIME()
        );

        IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
        BEGIN
            INSERT INTO dbo.HistoryLog (
                ActionType, EntityName, ActionDetails, ChangedByType, ChangedByAdminID, LoggedAt
            )
            VALUES (
                'INSERT', 'Equipment', 'Registered equipment ''' + @EquipmentName + ''' (' + @AssignedEquipmentID + ') with Category ''' + @EquipmentTypeID + '''.', 'Admin', ISNULL(@CreatedByAdminID, 'ADM-001'), SYSUTCDATETIME()
            );
        END;

        COMMIT TRANSACTION;

        SELECT 
            e.EquipmentID, e.EquipmentName, e.EquipmentDescription, e.EquipmentTypeID, et.EquipmentTypeName, e.EquipmentStatus, e.SerialNo, e.ImagePath, e.CreatedAt
        FROM dbo.Equipment e
        LEFT JOIN dbo.EquipmentType et ON et.EquipmentTypeID = e.EquipmentTypeID
        WHERE e.EquipmentID = @AssignedEquipmentID;

        RETURN 0;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        DECLARE @ErrorMessage NVARCHAR(4000) = ERROR_MESSAGE();
        DECLARE @ErrorSeverity INT = ERROR_SEVERITY();
        DECLARE @ErrorState INT = ERROR_STATE();
        RAISERROR(@ErrorMessage, @ErrorSeverity, @ErrorState);
        RETURN -99;
    END CATCH;
END;
GO

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

CREATE OR ALTER PROCEDURE dbo.sp_GetNextServiceID
    @NextServiceID nvarchar(30) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @MaxNum INT = 0;
    SELECT @MaxNum = ISNULL(MAX(TRY_CAST(SUBSTRING(ServicesID, CHARINDEX('-', ServicesID) + 1, 20) AS INT)), 0)
    FROM dbo.Services WITH (UPDLOCK, HOLDLOCK)
    WHERE ServicesID LIKE 'SRV-%';

    SET @NextServiceID = 'SRV-' + RIGHT('0000' + CAST((@MaxNum + 1) AS VARCHAR(10)), 4);
    SELECT @NextServiceID AS NextServiceID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.sp_GetNextCategoryID
    @NextCategoryID nvarchar(30) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @MaxNum INT = 0;
    SELECT @MaxNum = ISNULL(MAX(TRY_CAST(SUBSTRING(ServiceCategoryID, CHARINDEX('-', ServiceCategoryID) + 1, 20) AS INT)), 0)
    FROM dbo.ServiceCategory WITH (UPDLOCK, HOLDLOCK)
    WHERE ServiceCategoryID LIKE 'CAT-%';

    SET @NextCategoryID = 'CAT-' + RIGHT('0000' + CAST((@MaxNum + 1) AS VARCHAR(10)), 4);
    SELECT @NextCategoryID AS NextCategoryID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.sp_GetServiceCategories
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ServiceCategoryID, ServiceCategoryName, ServiceCategoryDescription
    FROM dbo.ServiceCategory
    ORDER BY ServiceCategoryName;
END;
GO

CREATE OR ALTER PROCEDURE dbo.sp_CreateServiceCategory
    @ServiceCategoryName        nvarchar(255),
    @ServiceCategoryDescription nvarchar(1000),
    @CreatedByAdminID           nvarchar(30)  = NULL,
    @CreatedCategoryID          nvarchar(30)  = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;
    BEGIN TRY
        DECLARE @MaxNum INT = 0;
        SELECT @MaxNum = ISNULL(MAX(TRY_CAST(SUBSTRING(ServiceCategoryID, CHARINDEX('-', ServiceCategoryID) + 1, 20) AS INT)), 0)
        FROM dbo.ServiceCategory WITH (UPDLOCK, HOLDLOCK)
        WHERE ServiceCategoryID LIKE 'CAT-%';

        SET @CreatedCategoryID = 'CAT-' + RIGHT('0000' + CAST((@MaxNum + 1) AS VARCHAR(10)), 4);

        WHILE EXISTS (SELECT 1 FROM dbo.ServiceCategory WHERE ServiceCategoryID = @CreatedCategoryID)
        BEGIN
            SET @MaxNum = @MaxNum + 1;
            SET @CreatedCategoryID = 'CAT-' + RIGHT('0000' + CAST((@MaxNum + 1) AS VARCHAR(10)), 4);
        END

        INSERT INTO dbo.ServiceCategory (
            ServiceCategoryID, ServiceCategoryName, ServiceCategoryDescription
        )
        VALUES (
            @CreatedCategoryID, @ServiceCategoryName, @ServiceCategoryDescription
        );

        IF OBJECT_ID(N'dbo.sp_AddHistoryLog', N'P') IS NOT NULL
        BEGIN
            EXEC dbo.sp_AddHistoryLog
                @ActionType = 'INSERT',
                @EntityName = 'ServiceCategory',
                @EntityID = @CreatedCategoryID,
                @ActionDetails = 'New service category created via Wizard.',
                @ChangedByType = 'Admin',
                @ActorID = @CreatedByAdminID;
        END

        COMMIT TRANSACTION;

        SELECT ServiceCategoryID, ServiceCategoryName, ServiceCategoryDescription
        FROM dbo.ServiceCategory
        WHERE ServiceCategoryID = @CreatedCategoryID;

    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

CREATE OR ALTER PROCEDURE dbo.sp_CreateService
    @ServiceName           nvarchar(255),
    @ServicesDescription   nvarchar(1000),
    @ServicePrice          decimal(18,2),
    @ServiceCategoryID     nvarchar(30)   = NULL,
    @ServiceImage          varbinary(max) = NULL,
    @ServiceImageMimeType  nvarchar(100)  = 'image/jpeg',
    @CreatedByAdminID      nvarchar(30)   = NULL,
    @CreatedServiceID      nvarchar(30)   = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;
    BEGIN TRY
        DECLARE @MaxNum INT = 0;
        SELECT @MaxNum = ISNULL(MAX(TRY_CAST(SUBSTRING(ServicesID, CHARINDEX('-', ServicesID) + 1, 20) AS INT)), 0)
        FROM dbo.Services WITH (UPDLOCK, HOLDLOCK)
        WHERE ServicesID LIKE 'SRV-%';

        SET @CreatedServiceID = 'SRV-' + RIGHT('0000' + CAST((@MaxNum + 1) AS VARCHAR(10)), 4);

        WHILE EXISTS (SELECT 1 FROM dbo.Services WHERE ServicesID = @CreatedServiceID)
        BEGIN
            SET @MaxNum = @MaxNum + 1;
            SET @CreatedServiceID = 'SRV-' + RIGHT('0000' + CAST((@MaxNum + 1) AS VARCHAR(10)), 4);
        END

        INSERT INTO dbo.Services (
            ServicesID, ServiceName, ServicesDescription, ServicePrice, ServiceImage, ServiceImageMimeType
        )
        VALUES (
            @CreatedServiceID, @ServiceName, @ServicesDescription, @ServicePrice, @ServiceImage, @ServiceImageMimeType
        );

        IF @ServiceCategoryID IS NOT NULL AND RTRIM(LTRIM(@ServiceCategoryID)) <> ''
        BEGIN
            IF EXISTS (SELECT 1 FROM dbo.ServiceCategory WHERE ServiceCategoryID = @ServiceCategoryID)
            BEGIN
                DECLARE @JoinID NVARCHAR(30) = 'SCJ-' + RIGHT('0000' + CAST((@MaxNum + 1) AS VARCHAR(10)), 4);
                IF EXISTS (SELECT 1 FROM dbo.ServiceCategoryJoins WHERE ServiceCategoryJoinID = @JoinID)
                BEGIN
                    SET @JoinID = 'SCJ-' + SUBSTRING(CONVERT(NVARCHAR(36), NEWID()), 1, 8);
                END

                INSERT INTO dbo.ServiceCategoryJoins (
                    ServiceCategoryJoinID, ServiceCategoryID, ServicesID
                )
                VALUES (
                    @JoinID, @ServiceCategoryID, @CreatedServiceID
                );
            END
        END

        IF OBJECT_ID(N'dbo.sp_AddHistoryLog', N'P') IS NOT NULL
        BEGIN
            EXEC dbo.sp_AddHistoryLog
                @ActionType = 'INSERT',
                @EntityName = 'Services',
                @EntityID = @CreatedServiceID,
                @ActionDetails = 'New main service created via Wizard.',
                @ChangedByType = 'Admin',
                @ActorID = @CreatedByAdminID;
        END

        COMMIT TRANSACTION;

        SELECT 
            s.ServicesID, s.ServiceName, s.ServicesDescription, s.ServicePrice, 
            @ServiceCategoryID AS ServiceCategoryID, c.ServiceCategoryName, 
            CAST(CASE WHEN s.ServiceImage IS NOT NULL THEN 1 ELSE 0 END AS bit) AS HasImage
        FROM dbo.Services s
        LEFT JOIN dbo.ServiceCategory c ON c.ServiceCategoryID = @ServiceCategoryID
        WHERE s.ServicesID = @CreatedServiceID;

    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

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
   5. SERVICE & CATALOG IMAGES 
   ===================================================================== */

CREATE OR ALTER PROCEDURE dbo.sp_GetServicesSummary
AS
BEGIN
    SET NOCOUNT ON;
    SELECT 
        ServicesID, ServiceName, ServicesDescription, ServicePrice,
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
    SELECT 
        ServicesID, ServiceName, ServicesDescription, ServicePrice, ServiceImage,
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
            @ActionType = 'IMAGE_UPLOAD', @EntityName = 'Services', @EntityID = @ServicesID, 
            @ActionDetails = 'Service image attached/updated via Image Wizard.', @ChangedByType = @ActorType, @ActorID = @UpdatedByAdminID;
    END
END;
GO

CREATE OR ALTER PROCEDURE dbo.sp_GetNextAdditionalServiceID
    @NextID NVARCHAR(30) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @CurVal SQL_VARIANT;
    SELECT @CurVal = current_value FROM sys.sequences WHERE name = 'Seq_AdditionalServicesID' AND schema_id = SCHEMA_ID('dbo');
    
    DECLARE @SeqVal INT = ISNULL(CAST(@CurVal AS INT), 1);

    WHILE EXISTS (SELECT 1 FROM dbo.AdditionalServices WHERE AdditionalServicesID = 'ADD-' + RIGHT('0000' + CAST(@SeqVal AS VARCHAR(10)), 4))
    BEGIN
        SET @SeqVal = @SeqVal + 1;
    END
    
    SET @NextID = 'ADD-' + RIGHT('0000' + CAST(@SeqVal AS VARCHAR(10)), 4);
    SELECT @NextID AS NextAdditionalServiceID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.sp_CreateAdditionalService
    @AdditionalServiceName nvarchar(255),
    @AdditionalServicesDescription nvarchar(1000),
    @ServicePrice decimal(18,2) = 0.00,
    @CategoryName nvarchar(100) = NULL,
    @AdditionalServiceImage varbinary(max) = NULL,
    @AdditionalServiceImageMimeType nvarchar(100) = 'image/jpeg',
    @CreatedByAdminID nvarchar(30) = NULL,
    @GeneratedID nvarchar(30) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;
    
    DECLARE @SeqVal INT = NEXT VALUE FOR dbo.Seq_AdditionalServicesID;
    DECLARE @NewID NVARCHAR(30) = 'ADD-' + RIGHT('0000' + CAST(@SeqVal AS VARCHAR(10)), 4);

    WHILE EXISTS (SELECT 1 FROM dbo.AdditionalServices WITH (UPDLOCK, HOLDLOCK) WHERE AdditionalServicesID = @NewID)
    BEGIN
        SET @SeqVal = NEXT VALUE FOR dbo.Seq_AdditionalServicesID;
        SET @NewID = 'ADD-' + RIGHT('0000' + CAST(@SeqVal AS VARCHAR(10)), 4);
    END

    INSERT INTO dbo.AdditionalServices (
        AdditionalServicesID, AdditionalServiceName, AdditionalServicesDescription, ServicePrice, CategoryName, AdditionalServiceImage, AdditionalServiceImageMimeType, CreatedAt
    )
    VALUES (
        @NewID, @AdditionalServiceName, @AdditionalServicesDescription, @ServicePrice, @CategoryName, @AdditionalServiceImage, @AdditionalServiceImageMimeType, SYSUTCDATETIME()
    );

    IF @CreatedByAdminID IS NOT NULL
    BEGIN
        DECLARE @ActorType nvarchar(20) = CASE WHEN @CreatedByAdminID LIKE 'SADM-%' THEN 'SystemAdmin' ELSE 'Admin' END;
        IF OBJECT_ID(N'dbo.sp_AddHistoryLog', N'P') IS NOT NULL
        BEGIN
            EXEC dbo.sp_AddHistoryLog 
                @ActionType = 'INSERT', @EntityName = 'AdditionalServices', @EntityID = @NewID, 
                @ActionDetails = 'Created new Additional Service with atomic ADD-#### ID generation and media upload.', @ChangedByType = @ActorType, @ActorID = @CreatedByAdminID;
        END
    END

    COMMIT TRANSACTION;
    SET @GeneratedID = @NewID;
    SELECT @NewID AS AdditionalServicesID;
END;
GO

CREATE OR ALTER PROCEDURE dbo.sp_GetAdditionalServicesSummary
AS
BEGIN
    SET NOCOUNT ON;
    SELECT 
        AdditionalServicesID, AdditionalServiceName, AdditionalServicesDescription,
        ISNULL(ServicePrice, 0.00) AS ServicePrice, ISNULL(CategoryName, 'Add-On') AS CategoryName,
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
        AdditionalServicesID, AdditionalServiceName, AdditionalServicesDescription,
        ISNULL(ServicePrice, 0.00) AS ServicePrice, ISNULL(CategoryName, 'Add-On') AS CategoryName,
        AdditionalServiceImage, ISNULL(AdditionalServiceImageMimeType, 'image/jpeg') AS AdditionalServiceImageMimeType,
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
            @ActionType = 'IMAGE_UPLOAD', @EntityName = 'AdditionalServices', @EntityID = @AdditionalServicesID, 
            @ActionDetails = 'Additional service image attached/updated via Image Wizard.', @ChangedByType = @ActorType, @ActorID = @UpdatedByAdminID;
    END
END;
GO

/* =====================================================================
   6. COMBO PACKAGES
   ===================================================================== */

CREATE OR ALTER PROCEDURE dbo.sp_GetNextComboPackageId
    @NextPackageID nvarchar(30) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM sys.sequences WHERE name = 'Seq_ComboPackageID')
    BEGIN
        DECLARE @CurVal SQL_VARIANT;
        SELECT @CurVal = current_value FROM sys.sequences WHERE name = 'Seq_ComboPackageID';
        DECLARE @NextNum INT;
        IF @CurVal IS NULL
        BEGIN
            SET @NextNum = 1;
        END
        ELSE
        BEGIN
            DECLARE @ExistingCount INT = 0;
            SELECT @ExistingCount = COUNT(*) FROM dbo.ComboPackage WHERE ComboPackageID LIKE 'PKG-%';
            IF @ExistingCount = 0 AND CAST(@CurVal AS INT) <= 1
                SET @NextNum = 1;
            ELSE
                SET @NextNum = CAST(@CurVal AS INT) + 1;
        END;
        SET @NextPackageID = CONCAT('PKG-', FORMAT(@NextNum, 'd4'));
    END
    ELSE
    BEGIN
        DECLARE @MaxNum INT = 0;
        SELECT @MaxNum = ISNULL(MAX(TRY_CAST(SUBSTRING(ComboPackageID, 5, 20) AS INT)), 0)
        FROM dbo.ComboPackage WITH (NOLOCK)
        WHERE ComboPackageID LIKE 'PKG-%';
        SET @NextPackageID = CONCAT('PKG-', FORMAT(@MaxNum + 1, 'd4'));
    END;
END;
GO

CREATE OR ALTER PROCEDURE dbo.sp_GetAvailableServicesForBundling
AS
BEGIN
    SET NOCOUNT ON;
    SELECT 
        s.ServicesID AS ServiceId, s.ServiceName AS ServiceName, s.ServicesDescription AS ServiceDescription,
        CAST(s.ServicePrice AS decimal(18,2)) AS Price, N'Main' AS ServiceType,
        CAST(CASE WHEN s.ServiceImage IS NOT NULL THEN 1 ELSE 0 END AS bit) AS HasImage
    FROM dbo.Services s
    UNION ALL
    SELECT 
        a.AdditionalServicesID AS ServiceId, a.AdditionalServiceName AS ServiceName, a.AdditionalServicesDescription AS ServiceDescription,
        CAST(ISNULL(a.ServicePrice, 0.00) AS decimal(18,2)) AS Price, N'Additional' AS ServiceType,
        CAST(CASE WHEN a.AdditionalServiceImage IS NOT NULL THEN 1 ELSE 0 END AS bit) AS HasImage
    FROM dbo.AdditionalServices a
    ORDER BY ServiceType DESC, ServiceName ASC;
END;
GO

CREATE OR ALTER PROCEDURE dbo.sp_GetServiceCategoriesLookup
AS
BEGIN
    SET NOCOUNT ON;
    IF OBJECT_ID(N'dbo.ServiceCategory', N'U') IS NOT NULL
    BEGIN
        SELECT ServiceCategoryID AS CategoryId, ServiceCategoryName AS CategoryName, ServiceCategoryDescription AS CategoryDescription
        FROM dbo.ServiceCategory
        ORDER BY ServiceCategoryName ASC;
    END
    ELSE
    BEGIN
        SELECT CAST('' AS nvarchar(30)) AS CategoryId, CAST('' AS nvarchar(255)) AS CategoryName, CAST('' AS nvarchar(255)) AS CategoryDescription
        WHERE 1 = 0;
    END
END;
GO

CREATE OR ALTER PROCEDURE dbo.sp_CreateComboPackageWithServices
    @ComboPackageName          nvarchar(255),
    @ComboPackageDescription   nvarchar(max),
    @ComboPackagePrice         decimal(18,2),
    @ServiceCategoryID         nvarchar(30),
    @ComboPackageImage         varbinary(max),
    @ComboPackageImageMimeType nvarchar(100) = 'image/jpeg',
    @SelectedServiceIDsJson    nvarchar(max),
    @CreatedByAdminID          nvarchar(30)  = NULL,
    @GeneratedPackageID        nvarchar(30)  OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @LockResult INT;
        EXEC @LockResult = sp_getapplock @Resource = 'ComboPackage_IdGen_Lock', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;

        IF @LockResult < 0
        BEGIN
            RAISERROR('Failed to acquire application lock for package ID generation.', 16, 1);
            ROLLBACK TRANSACTION;
            RETURN;
        END;

        DECLARE @SeqNum INT;
        IF EXISTS (SELECT 1 FROM sys.sequences WHERE name = 'Seq_ComboPackageID')
        BEGIN
            SET @SeqNum = NEXT VALUE FOR dbo.Seq_ComboPackageID;
        END
        ELSE
        BEGIN
            DECLARE @CurMax INT = 0;
            SELECT @CurMax = ISNULL(MAX(TRY_CAST(SUBSTRING(ComboPackageID, 5, 20) AS INT)), 0)
            FROM dbo.ComboPackage WITH (UPDLOCK, HOLDLOCK)
            WHERE ComboPackageID LIKE 'PKG-%';
            SET @SeqNum = @CurMax + 1;
        END;

        SET @GeneratedPackageID = CONCAT('PKG-', FORMAT(@SeqNum, 'd4'));

        WHILE EXISTS (SELECT 1 FROM dbo.ComboPackage WHERE ComboPackageID = @GeneratedPackageID)
        BEGIN
            IF EXISTS (SELECT 1 FROM sys.sequences WHERE name = 'Seq_ComboPackageID')
                SET @SeqNum = NEXT VALUE FOR dbo.Seq_ComboPackageID;
            ELSE
                SET @SeqNum = @SeqNum + 1;
            SET @GeneratedPackageID = CONCAT('PKG-', FORMAT(@SeqNum, 'd4'));
        END;

        INSERT INTO dbo.ComboPackage (
            ComboPackageID, ComboPackageName, ComboPackageDescription, ComboPackagePrice, ComboPackageImage, ComboPackageImageMimeType
        )
        VALUES (
            @GeneratedPackageID, @ComboPackageName, @ComboPackageDescription, @ComboPackagePrice, @ComboPackageImage, ISNULL(@ComboPackageImageMimeType, 'image/jpeg')
        );

        IF @ServiceCategoryID IS NOT NULL AND LEN(TRIM(@ServiceCategoryID)) > 0
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM dbo.ServiceCategory WHERE ServiceCategoryID = @ServiceCategoryID)
            BEGIN
                INSERT INTO dbo.ServiceCategory (ServiceCategoryID, ServiceCategoryName, ServiceCategoryDescription)
                VALUES (@ServiceCategoryID, @ServiceCategoryID, @ServiceCategoryID);
            END;

            DECLARE @CatJoinID nvarchar(30) = CONCAT('SCJ-', SUBSTRING(REPLACE(CAST(NEWID() AS nvarchar(36)), '-', ''), 1, 10));
            INSERT INTO dbo.ServiceCategoryJoins (ServiceCategoryJoinID, ServiceCategoryID, ServicesID, AdditionalServicesID, ComboPackageID)
            VALUES (@CatJoinID, @ServiceCategoryID, NULL, NULL, @GeneratedPackageID);
        END;

        IF @SelectedServiceIDsJson IS NOT NULL AND ISJSON(@SelectedServiceIDsJson) = 1
        BEGIN
            DECLARE @ParsedServices TABLE (SvcID nvarchar(30) PRIMARY KEY);
            INSERT INTO @ParsedServices (SvcID)
            SELECT DISTINCT TRIM(value) FROM OPENJSON(@SelectedServiceIDsJson)
            WHERE value IS NOT NULL AND LEN(TRIM(value)) > 0;

            INSERT INTO dbo.ComboPackageJoin (ComboPackageJoinID, ComboPackageID, ServicesID, AdditionalServicesID)
            SELECT
                CONCAT('CPJ-', SUBSTRING(REPLACE(CAST(NEWID() AS nvarchar(36)), '-', ''), 1, 10)),
                @GeneratedPackageID,
                CASE WHEN s.ServicesID IS NOT NULL OR a.AdditionalServicesID IS NULL THEN p.SvcID ELSE NULL END,
                CASE WHEN a.AdditionalServicesID IS NOT NULL THEN p.SvcID ELSE NULL END
            FROM @ParsedServices p
            LEFT JOIN dbo.Services s ON s.ServicesID = p.SvcID
            LEFT JOIN dbo.AdditionalServices a ON a.AdditionalServicesID = p.SvcID;
        END;

        IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
        BEGIN
            DECLARE @ActorAdminID nvarchar(30) = COALESCE(@CreatedByAdminID, 'SADM-001');
            DECLARE @ActorRole nvarchar(20) = CASE WHEN @ActorAdminID LIKE 'SADM-%' THEN 'SystemAdmin' ELSE 'Admin' END;

            INSERT INTO dbo.HistoryLog (
                ActionType, EntityName, EntityID, ActionDetails, ChangedByType, ChangedByAdminID, LoggedAt
            )
            VALUES (
                'INSERT', 'ComboPackage', @GeneratedPackageID, CONCAT('Created Combo Package "', @ComboPackageName, '" (ID: ', @GeneratedPackageID, ') with price ', CAST(@ComboPackagePrice AS nvarchar(30))), @ActorRole, @ActorAdminID, SYSUTCDATETIME()
            );
        END;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE dbo.sp_GetComboPackagesSummary
AS
BEGIN
    SET NOCOUNT ON;
    SELECT 
        ComboPackageID, ComboPackageName, ComboPackageDescription,
        ISNULL(ComboPackagePrice, 0.00) AS ComboPackagePrice,
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
        ComboPackageID, ComboPackageName, ComboPackageDescription,
        ISNULL(ComboPackagePrice, 0.00) AS ComboPackagePrice,
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
            @ActionType = 'IMAGE_UPLOAD', @EntityName = 'ComboPackage', @EntityID = @ComboPackageID, 
            @ActionDetails = 'Combo package image attached/updated via Image Wizard.', @ChangedByType = @ActorType, @ActorID = @UpdatedByAdminID;
    END
END;
GO

/* =====================================================================
   7. AUDIT LOGGING (HistoryLog)
   ===================================================================== */

CREATE OR ALTER PROCEDURE dbo.sp_AddHistoryLog
    @ActionType          nvarchar(20),
    @EntityName          nvarchar(128),
    @EntityID            nvarchar(30)  = NULL,
    @ActionDetails       nvarchar(max) = NULL,
    @EventAppointmentID  nvarchar(30)  = NULL,
    @NegotiationID       nvarchar(30)  = NULL,
    @CustomerID          nvarchar(30)  = NULL,
    @EmployeeID          nvarchar(30)  = NULL,
    @ChangedByType       nvarchar(20),
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
        ActionType, EntityName, EntityID, ActionDetails, EventAppointmentID, NegotiationID, CustomerID, EmployeeID,
        ChangedByType, ChangedByCustomerID, ChangedByEmployeeID, ChangedByAdminID
    )
    VALUES (
        @ActionType, @EntityName, @EntityID, @ActionDetails, @EventAppointmentID, @NegotiationID, @CustomerID, @EmployeeID,
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
        SELECT TOP (@TopCount)
            HistoryLogID, ActionType, EntityName, EntityID, ActionDetails, EventAppointmentID, NegotiationID, CustomerID, EmployeeID, ChangedByType, ChangedBy, LoggedAt
        FROM dbo.HistoryLog
        ORDER BY LoggedAt DESC;
    END
    ELSE
    BEGIN
        SELECT TOP (@TopCount)
            HistoryLogID, ActionType, EntityName, EntityID, ActionDetails, EventAppointmentID, NegotiationID, CustomerID, EmployeeID, ChangedByType, ChangedBy, LoggedAt
        FROM dbo.HistoryLog
        WHERE ChangedByAdminID = @RequestingUserID OR EntityName IN ('Services', 'AdditionalServices', 'ComboPackage', 'Equipment', 'EventAppointment')
        ORDER BY LoggedAt DESC;
    END
END;
GO