CREATE OR ALTER PROCEDURE [dbo].[usp_CreateEventAppointment]
    @CustomerID NVARCHAR(450),
    @EventName NVARCHAR(255),
    @EventDescription NVARCHAR(255) = NULL,
    @EventDate DATE,
    @EventTime TIME,
    @ServicesIDs NVARCHAR(MAX) = NULL,
    @AdditionalServicesIDs NVARCHAR(MAX) = NULL,
    @ComboPackageIDs NVARCHAR(MAX) = NULL,
    @EmployeeIDs NVARCHAR(MAX) = NULL,
    @EquipmentIDs NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- 1. Validate appointment date is not in the past
    IF @EventDate < CAST(GETUTCDATE() AS DATE)
    BEGIN
        THROW 50001, 'Cannot book appointments for past dates.', 1;
    END

    -- 2. Resolve customer by Id, StudioId, or Email
    DECLARE @ActualCustomerId NVARCHAR(450);
    DECLARE @CustomerFullName NVARCHAR(255);

    SELECT TOP 1 @ActualCustomerId = Id, 
                 @CustomerFullName = LTRIM(RTRIM(ISNULL(FirstName, '') + ' ' + ISNULL(LastName, '')))
    FROM AspNetUsers
    WHERE Id = @CustomerID OR StudioId = @CustomerID OR Email = @CustomerID;

    IF @ActualCustomerId IS NULL
    BEGIN
        SET @ActualCustomerId = @CustomerID;
        SET @CustomerFullName = @CustomerID;
    END

    -- 3. Resolve primary service ID
    DECLARE @PrimaryServiceId INT = NULL;
    IF @ServicesIDs IS NOT NULL AND LEN(@ServicesIDs) > 0
    BEGIN
        SELECT TOP 1 @PrimaryServiceId = TRY_CAST(value AS INT)
        FROM STRING_SPLIT(@ServicesIDs, ',')
        WHERE TRY_CAST(value AS INT) IS NOT NULL;
    END

    IF @PrimaryServiceId IS NULL
    BEGIN
        SELECT TOP 1 @PrimaryServiceId = Id FROM Services WHERE IsActive = 1;
        IF @PrimaryServiceId IS NULL
        BEGIN
            INSERT INTO Services (Name, Price, Duration, Description, IsActive)
            VALUES (ISNULL(@EventName, 'Standard Session'), 1500.00, 60, 'Auto-created standard session', 1);
            SET @PrimaryServiceId = SCOPE_IDENTITY();
        END
    END

    -- 4. Check for staff double-booking conflicts, day off, and approved leave
    IF @EmployeeIDs IS NOT NULL AND LEN(@EmployeeIDs) > 0
    BEGIN
        IF EXISTS (
            SELECT 1 
            FROM dbo.Employee e
            INNER JOIN STRING_SPLIT(@EmployeeIDs, ',') s ON (e.EmployeeID = s.value)
            WHERE COL_LENGTH(N'dbo.Employee', N'DaysOff') IS NOT NULL
              AND CHARINDEX(DATENAME(dw, @EventDate), e.DaysOff) > 0
        )
        BEGIN
            THROW 50003, 'Cannot assign staff: One or more selected employees have their scheduled Day Off on this date.', 1;
        END

        IF OBJECT_ID(N'dbo.EmployeeLeaves', N'U') IS NOT NULL
        BEGIN
            IF EXISTS (
                SELECT 1
                FROM dbo.EmployeeLeaves el
                INNER JOIN STRING_SPLIT(@EmployeeIDs, ',') s ON (el.EmployeeStudioId = s.value)
                WHERE el.Status = 'Approved'
                  AND @EventDate BETWEEN el.StartDate AND el.EndDate
            )
            BEGIN
                THROW 50004, 'Cannot assign staff: One or more selected employees are on approved leave on this date.', 1;
            END
        END

        IF EXISTS (
            SELECT 1 
            FROM EmployeeAssignments ea
            INNER JOIN STRING_SPLIT(@EmployeeIDs, ',') s ON ea.EmployeeId = TRY_CAST(s.value AS INT)
            WHERE ea.ScheduleDate = @EventDate AND ea.ScheduleTime = @EventTime
        )
        BEGIN
            THROW 50002, 'One or more selected staff members are already scheduled at this date and time.', 1;
        END
    END

    -- 5. Check for equipment double-booking conflicts
    IF @EquipmentIDs IS NOT NULL AND LEN(@EquipmentIDs) > 0
    BEGIN
        IF EXISTS (
            SELECT 1 
            FROM EquipmentAssignments eqa
            INNER JOIN STRING_SPLIT(@EquipmentIDs, ',') s ON eqa.EquipmentId = TRY_CAST(s.value AS INT)
            WHERE eqa.ScheduleDate = @EventDate AND eqa.ScheduleTime = @EventTime
        )
        BEGIN
            THROW 50003, 'One or more selected equipment items are already reserved at this date and time.', 1;
        END
    END

    -- 6. Insert booking and assignment records atomically
    BEGIN TRANSACTION;

    DECLARE @BookingId INT;
    INSERT INTO Bookings (CustomerId, ServiceId, ScheduleDate, ScheduleTime, Status)
    VALUES (@ActualCustomerId, @PrimaryServiceId, @EventDate, @EventTime, 0);

    SET @BookingId = SCOPE_IDENTITY();

    DECLARE @EmployeesAssigned INT = 0;
    IF @EmployeeIDs IS NOT NULL AND LEN(@EmployeeIDs) > 0
    BEGIN
        INSERT INTO EmployeeAssignments (EmployeeId, BookingId, ScheduleDate, ScheduleTime)
        SELECT TRY_CAST(value AS INT), @BookingId, @EventDate, @EventTime
        FROM STRING_SPLIT(@EmployeeIDs, ',')
        WHERE TRY_CAST(value AS INT) IS NOT NULL;

        SET @EmployeesAssigned = @@ROWCOUNT;
    END

    DECLARE @EquipmentReserved INT = 0;
    IF @EquipmentIDs IS NOT NULL AND LEN(@EquipmentIDs) > 0
    BEGIN
        INSERT INTO EquipmentAssignments (EquipmentId, BookingId, ScheduleDate, ScheduleTime)
        SELECT TRY_CAST(value AS INT), @BookingId, @EventDate, @EventTime
        FROM STRING_SPLIT(@EquipmentIDs, ',')
        WHERE TRY_CAST(value AS INT) IS NOT NULL;

        SET @EquipmentReserved = @@ROWCOUNT;
    END

    COMMIT TRANSACTION;

    DECLARE @LineItemCount INT = 1;
    IF @AdditionalServicesIDs IS NOT NULL AND LEN(@AdditionalServicesIDs) > 0
    BEGIN
        SET @LineItemCount = @LineItemCount + (SELECT COUNT(*) FROM STRING_SPLIT(@AdditionalServicesIDs, ',') WHERE LEN(value) > 0);
    END

    -- 7. Return expected result shape
    SELECT 
        CAST(@BookingId AS NVARCHAR(30)) AS EventAppointmentID,
        ISNULL(@EventName, 'Photo Session') AS EventName,
        @EventDate AS EventDate,
        @EventTime AS EventTime,
        ISNULL(@CustomerFullName, 'Valued Customer') AS CustomerName,
        @LineItemCount AS LineItemCount,
        @EmployeesAssigned AS EmployeesAssigned,
        @EquipmentReserved AS EquipmentReserved;
END
GO
