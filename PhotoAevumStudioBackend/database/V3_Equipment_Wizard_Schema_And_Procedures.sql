/* =====================================================================
   PHOTO AEVUM STUDIO - EQUIPMENT WIZARD SCHEMA & STORED PROCEDURES
   Sequential Custom ID Generation (EQP-####) & Media Storage
   Target: Microsoft SQL Server 2016+ / Azure SQL Database
   ===================================================================== */

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- ─────────────────────────────────────────────────────────────────────
-- 1. SCHEMA UPGRADE: Ensure dbo.Equipment has Description & ImagePath
-- ─────────────────────────────────────────────────────────────────────
IF OBJECT_ID(N'dbo.Equipment', N'U') IS NOT NULL
BEGIN
    -- Ensure EquipmentDescription column exists
    IF COL_LENGTH(N'dbo.Equipment', N'EquipmentDescription') IS NULL
    BEGIN
        ALTER TABLE dbo.Equipment ADD EquipmentDescription nvarchar(1000) NULL;
        PRINT 'Column dbo.Equipment.EquipmentDescription added.';
    END;

    -- Ensure ImagePath column exists (relative web path to uploaded image file)
    IF COL_LENGTH(N'dbo.Equipment', N'ImagePath') IS NULL
    BEGIN
        ALTER TABLE dbo.Equipment ADD ImagePath nvarchar(500) NULL;
        PRINT 'Column dbo.Equipment.ImagePath added.';
    END;

    -- Ensure CreatedAt timestamp column exists
    IF COL_LENGTH(N'dbo.Equipment', N'CreatedAt') IS NULL
    BEGIN
        ALTER TABLE dbo.Equipment ADD CreatedAt datetime2 NOT NULL CONSTRAINT DF_Equipment_CreatedAt DEFAULT SYSUTCDATETIME();
        PRINT 'Column dbo.Equipment.CreatedAt added.';
    END;
END;
GO

-- ─────────────────────────────────────────────────────────────────────
-- 2. ATOMIC SEQUENCE OBJECT: dbo.EquipmentIdSequence
--    Provides high-performance, latch-free, thread-safe sequence numbers
--    that eliminate concurrency race conditions across multiple API instances.
-- ─────────────────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = 'EquipmentIdSequence' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    -- Compute starting seed based on existing records to avoid collisions
    DECLARE @MaxSuffix INT = 0;

    IF OBJECT_ID(N'dbo.Equipment', N'U') IS NOT NULL
    BEGIN
        SELECT @MaxSuffix = ISNULL(MAX(TRY_CAST(SUBSTRING(EquipmentID, 5, 10) AS INT)), 0)
        FROM dbo.Equipment
        WHERE EquipmentID LIKE 'EQP-%';
    END;

    DECLARE @StartSeed BIGINT = @MaxSuffix + 1;
    DECLARE @CreateSeqSql NVARCHAR(MAX) = N'
        CREATE SEQUENCE dbo.EquipmentIdSequence
            AS INT
            START WITH ' + CAST(@StartSeed AS NVARCHAR(20)) + N'
            INCREMENT BY 1
            MINVALUE 1
            NO CYCLE
            CACHE 10;';

    EXEC sp_executesql @CreateSeqSql;
    PRINT 'Sequence dbo.EquipmentIdSequence created starting at ' + CAST(@StartSeed AS NVARCHAR(20)) + '.';
END;
GO

-- ─────────────────────────────────────────────────────────────────────
-- 3. STORED PROCEDURE: dbo.sp_GetNextEquipmentIdPreview
--    Returns the projected next sequential Equipment ID for UI display.
--    Safe for read-only inspection without consuming the sequence.
-- ─────────────────────────────────────────────────────────────────────
CREATE OR ALTER PROCEDURE dbo.sp_GetNextEquipmentIdPreview
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @NextVal INT;

    -- Inspect current value of the sequence if initialized
    SELECT @NextVal = CAST(current_value AS INT)
    FROM sys.sequences
    WHERE name = 'EquipmentIdSequence' AND schema_id = SCHEMA_ID('dbo');

    -- If the sequence hasn't generated any value yet, start_value is next
    IF @NextVal IS NULL
    BEGIN
        SELECT @NextVal = CAST(start_value AS INT)
        FROM sys.sequences
        WHERE name = 'EquipmentIdSequence' AND schema_id = SCHEMA_ID('dbo');
    END
    ELSE
    BEGIN
        -- If sequence was already used, the next number is current_value + increment
        DECLARE @IsExhausted BIT = 0;
        SELECT @NextVal = @NextVal + CAST(increment AS INT)
        FROM sys.sequences
        WHERE name = 'EquipmentIdSequence' AND schema_id = SCHEMA_ID('dbo');
    END;

    -- Fallback safety check against actual max in dbo.Equipment
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

-- ─────────────────────────────────────────────────────────────────────
-- 4. STORED PROCEDURE: dbo.sp_CreateEquipmentWithCustomId
--    Atomically generates the sequential custom ID (EQP-####),
--    validates unique constraints, inserts the equipment, and writes
--    an audit log entry into dbo.HistoryLog within a single transaction.
-- ─────────────────────────────────────────────────────────────────────
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

    -- Sanitize inputs
    SET @EquipmentName = RTRIM(LTRIM(@EquipmentName));
    SET @EquipmentDescription = NULLIF(RTRIM(LTRIM(@EquipmentDescription)), '');
    SET @EquipmentTypeID = RTRIM(LTRIM(@EquipmentTypeID));
    SET @SerialNo = NULLIF(RTRIM(LTRIM(@SerialNo)), '');
    SET @ImagePath = NULLIF(RTRIM(LTRIM(@ImagePath)), '');

    -- Validation
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

    -- Ensure EquipmentType exists or insert if missing
    IF NOT EXISTS (SELECT 1 FROM dbo.EquipmentType WHERE EquipmentTypeID = @EquipmentTypeID)
    BEGIN
        INSERT INTO dbo.EquipmentType (EquipmentTypeID, EquipmentTypeName)
        VALUES (@EquipmentTypeID, @EquipmentTypeID);
    END;

    BEGIN TRANSACTION;

    BEGIN TRY
        -- Check unique serial number constraint under UPDATE LOCK to prevent concurrent duplicate inserts
        IF @SerialNo IS NOT NULL
        BEGIN
            IF EXISTS (
                SELECT 1 
                FROM dbo.Equipment WITH (UPDLOCK, HOLDLOCK) 
                WHERE SerialNo = @SerialNo
            )
            BEGIN
                RAISERROR('An equipment item with Serial Number ''%s'' already exists in inventory.', 16, 1, @SerialNo);
            END;
        END;

        -- Atomically obtain the next sequence number (Guaranteed race-condition-free)
        DECLARE @NextSequence INT = NEXT VALUE FOR dbo.EquipmentIdSequence;
        SET @AssignedEquipmentID = 'EQP-' + RIGHT('0000' + CAST(@NextSequence AS VARCHAR(10)), 4);

        -- Double check that the ID is not already used (in case of manual historical inserts)
        WHILE EXISTS (SELECT 1 FROM dbo.Equipment WITH (UPDLOCK, HOLDLOCK) WHERE EquipmentID = @AssignedEquipmentID)
        BEGIN
            SET @NextSequence = NEXT VALUE FOR dbo.EquipmentIdSequence;
            SET @AssignedEquipmentID = 'EQP-' + RIGHT('0000' + CAST(@NextSequence AS VARCHAR(10)), 4);
        END;

        -- Insert into dbo.Equipment
        INSERT INTO dbo.Equipment (
            EquipmentID,
            EquipmentName,
            EquipmentDescription,
            EquipmentTypeID,
            EquipmentStatus,
            SerialNo,
            ImagePath,
            CreatedAt
        )
        VALUES (
            @AssignedEquipmentID,
            @EquipmentName,
            @EquipmentDescription,
            @EquipmentTypeID,
            'Available',
            @SerialNo,
            @ImagePath,
            SYSUTCDATETIME()
        );

        -- Insert audit log into dbo.HistoryLog
        IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
        BEGIN
            INSERT INTO dbo.HistoryLog (
                ActionType,
                EntityName,
                ActionDetails,
                ChangedByType,
                ChangedByAdminID,
                LoggedAt
            )
            VALUES (
                'INSERT',
                'Equipment',
                'Registered equipment ''' + @EquipmentName + ''' (' + @AssignedEquipmentID + ') with Category ''' + @EquipmentTypeID + '''.',
                'Admin',
                ISNULL(@CreatedByAdminID, 'ADM-001'),
                SYSUTCDATETIME()
            );
        END;

        COMMIT TRANSACTION;

        -- Return the newly created record
        SELECT 
            e.EquipmentID,
            e.EquipmentName,
            e.EquipmentDescription,
            e.EquipmentTypeID,
            et.EquipmentTypeName,
            e.EquipmentStatus,
            e.SerialNo,
            e.ImagePath,
            e.CreatedAt
        FROM dbo.Equipment e
        LEFT JOIN dbo.EquipmentType et ON et.EquipmentTypeID = e.EquipmentTypeID
        WHERE e.EquipmentID = @AssignedEquipmentID;

        RETURN 0;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;

        DECLARE @ErrorMessage NVARCHAR(4000) = ERROR_MESSAGE();
        DECLARE @ErrorSeverity INT = ERROR_SEVERITY();
        DECLARE @ErrorState INT = ERROR_STATE();

        RAISERROR(@ErrorMessage, @ErrorSeverity, @ErrorState);
        RETURN -99;
    END CATCH;
END;
GO

PRINT 'V3 Equipment Wizard Schema and Procedures installed successfully.';
GO
