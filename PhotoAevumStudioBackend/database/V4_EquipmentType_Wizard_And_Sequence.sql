/* =====================================================================
   V4: Equipment Type Wizard, Concurrency-Safe Sequence & Procedures
   Photo Aevum Studio - Blazor Web (.NET 10) & ASP.NET Core Web API
   ===================================================================== */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- 1. Concurrency-Safe Sequence Object for ETYP-#### Generation
-- Prevents race conditions, duplicate key violations, and locks under high concurrency.
IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = 'Seq_EquipmentType' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    DECLARE @MaxVal INT = 0;
    SELECT @MaxVal = ISNULL(MAX(TRY_CAST(SUBSTRING(EquipmentTypeID, 6, 20) AS INT)), 0)
    FROM dbo.EquipmentType
    WHERE EquipmentTypeID LIKE 'ETYP-%';

    DECLARE @StartVal INT = @MaxVal + 1;
    DECLARE @Sql NVARCHAR(MAX) = N'CREATE SEQUENCE dbo.Seq_EquipmentType AS INT START WITH ' + CAST(@StartVal AS NVARCHAR(10)) + N' INCREMENT BY 1;';
    EXEC sp_executesql @Sql;
    PRINT 'Created sequence dbo.Seq_EquipmentType starting at ' + CAST(@StartVal AS NVARCHAR(10));
END;
GO

-- 2. Stored Procedure: Generate Next Equipment Type ID (ETYP-####) atomically
CREATE OR ALTER PROCEDURE dbo.sp_GetNextEquipmentTypeID
    @NextID NVARCHAR(30) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @SeqVal INT = NEXT VALUE FOR dbo.Seq_EquipmentType;

    -- Concurrency guard: in case of any prior manual ID insertions
    WHILE EXISTS (SELECT 1 FROM dbo.EquipmentType WHERE EquipmentTypeID = 'ETYP-' + RIGHT('0000' + CAST(@SeqVal AS VARCHAR(10)), 4))
    BEGIN
        SET @SeqVal = NEXT VALUE FOR dbo.Seq_EquipmentType;
    END

    SET @NextID = 'ETYP-' + RIGHT('0000' + CAST(@SeqVal AS VARCHAR(10)), 4);
    SELECT @NextID AS NextEquipmentTypeID;
END;
GO

-- 3. Stored Procedure: Atomic Equipment Type Creation with Concurrency Guard & Audit Log
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

        -- Prevent semantic duplicates (case-insensitive & trimmed)
        IF EXISTS (
            SELECT 1 
            FROM dbo.EquipmentType 
            WHERE LOWER(RTRIM(LTRIM(EquipmentTypeName))) = LOWER(RTRIM(LTRIM(@EquipmentTypeName)))
        )
        BEGIN
            THROW 51001, 'An Equipment Type with this name already exists in the catalog.', 1;
        END

        -- If ID is not provided, atomically generate from sequence
        DECLARE @NewID NVARCHAR(30) = @EquipmentTypeID;
        IF @NewID IS NULL OR LTRIM(RTRIM(@NewID)) = ''
        BEGIN
            DECLARE @SeqVal INT = NEXT VALUE FOR dbo.Seq_EquipmentType;
            SET @NewID = 'ETYP-' + RIGHT('0000' + CAST(@SeqVal AS VARCHAR(10)), 4);
        END

        -- Guard against collision with any existing row
        WHILE EXISTS (SELECT 1 FROM dbo.EquipmentType WITH (UPDLOCK, HOLDLOCK) WHERE EquipmentTypeID = @NewID)
        BEGIN
            DECLARE @NextSeqVal INT = NEXT VALUE FOR dbo.Seq_EquipmentType;
            SET @NewID = 'ETYP-' + RIGHT('0000' + CAST(@NextSeqVal AS VARCHAR(10)), 4);
        END

        INSERT INTO dbo.EquipmentType (
            EquipmentTypeID,
            EquipmentTypeName
        )
        VALUES (
            @NewID,
            RTRIM(LTRIM(@EquipmentTypeName))
        );

        -- Audit trail logging into dbo.HistoryLog
        IF @CreatedByAdminID IS NOT NULL AND OBJECT_ID(N'dbo.HistoryLog', N'U') IS NOT NULL
        BEGIN
            INSERT INTO dbo.HistoryLog (
                ActionType,
                EntityName,
                EntityID,
                ActionDetails,
                ChangedByType,
                ChangedByAdminID,
                LoggedAt
            )
            VALUES (
                'INSERT',
                'EquipmentType',
                @NewID,
                'Created new Equipment Type with atomic ETYP-#### ID generation.',
                'Admin',
                @CreatedByAdminID,
                SYSUTCDATETIME()
            );
        END

        COMMIT TRANSACTION;

        SET @GeneratedID = @NewID;

        SELECT 
            EquipmentTypeID,
            EquipmentTypeName
        FROM dbo.EquipmentType
        WHERE EquipmentTypeID = @NewID;

    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO
