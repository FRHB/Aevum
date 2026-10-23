SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

/* =====================================================================
   1. USER MANAGEMENT (Customer & Employee)
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

    -- Note: Table constraint CK_EventLineItem_ExactlyOne ensures only one of the three IDs is NOT NULL
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

    -- Avoid duplicate assignments gracefully
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

-- Assign a Service, Additional Service, or Combo Package to a Service Category
CREATE OR ALTER PROCEDURE dbo.sp_AddServiceCategoryJoin
    @ServiceCategoryJoinID nvarchar(30),
    @ServiceCategoryID nvarchar(30),
    @ServicesID nvarchar(30) = NULL,
    @AdditionalServicesID nvarchar(30) = NULL,
    @ComboPackageID nvarchar(30) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- Note: Table constraint CK_ServiceCategoryJoins_ExactlyOne ensures only one of the three item IDs is NOT NULL
    INSERT INTO dbo.ServiceCategoryJoins (
        ServiceCategoryJoinID, ServiceCategoryID, ServicesID, AdditionalServicesID, ComboPackageID
    )
    VALUES (
        @ServiceCategoryJoinID, @ServiceCategoryID, @ServicesID, @AdditionalServicesID, @ComboPackageID
    );
END;
GO


/* =====================================================================
   5. STATUS UPDATES & AUDIT LOGGING (HistoryLog Integration)
   ===================================================================== */

-- Update Event Status (Automatically handles the complex HistoryLog requirements)
CREATE OR ALTER PROCEDURE dbo.sp_UpdateEventStatus
    @EventAppointmentID nvarchar(30),
    @NewStatus nvarchar(30),
    @ChangedByType nvarchar(10),  -- 'Customer', 'Employee', 'Admin', or 'System'
    @ActorID nvarchar(30) = NULL  -- The ID of the person making the change (Pass NULL if System)
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @OldStatus nvarchar(30);
    DECLARE @CustomerID nvarchar(30);
    
    -- Get current status and associated customer to log properly
    SELECT @OldStatus = EventStatus, @CustomerID = CustomerID 
    FROM dbo.EventAppointment 
    WHERE EventAppointmentID = @EventAppointmentID;

    IF @OldStatus IS NULL
    BEGIN
        RAISERROR('EventAppointmentID not found.', 16, 1);
        RETURN;
    END

    -- Only update and log if the status is actually changing
    IF @OldStatus <> @NewStatus
    BEGIN
        BEGIN TRY
            BEGIN TRANSACTION;

            -- 1. Update the Status
            UPDATE dbo.EventAppointment
            SET EventStatus = @NewStatus
            WHERE EventAppointmentID = @EventAppointmentID;

            -- 2. Prepare variables for Audit Log constraints
            DECLARE @ChangedByCustomer nvarchar(30) = NULL;
            DECLARE @ChangedByEmployee nvarchar(30) = NULL;
            DECLARE @ChangedByAdmin nvarchar(30) = NULL;

            IF @ChangedByType = 'Customer' SET @ChangedByCustomer = @ActorID;
            ELSE IF @ChangedByType = 'Employee' SET @ChangedByEmployee = @ActorID;
            ELSE IF @ChangedByType = 'Admin' SET @ChangedByAdmin = @ActorID;

            DECLARE @ActionDetails nvarchar(max) = CONCAT('Status changed from ', @OldStatus, ' to ', @NewStatus);

            -- 3. Insert into HistoryLog
            INSERT INTO dbo.HistoryLog (
                ActionType, EntityName, EntityID, ActionDetails, 
                EventAppointmentID, CustomerID, 
                ChangedByType, ChangedByCustomerID, ChangedByEmployeeID, ChangedByAdminID
            )
            VALUES (
                'STATUS_CHANGE', 'EventAppointment', @EventAppointmentID, @ActionDetails,
                @EventAppointmentID, @CustomerID,
                @ChangedByType, @ChangedByCustomer, @ChangedByEmployee, @ChangedByAdmin
            );

            COMMIT TRANSACTION;
        END TRY
        BEGIN CATCH
            ROLLBACK TRANSACTION;
            THROW;
        END CATCH
    END
END;
GO