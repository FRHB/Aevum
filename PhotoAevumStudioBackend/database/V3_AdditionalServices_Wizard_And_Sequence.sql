/* =====================================================================
   V3: Additional Services Wizard, Concurrency-Safe Sequence & Procedures
   Photo Aevum Studio - Blazor Web (.NET 10) & ASP.NET Core Web API
   ===================================================================== */

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- 1. Ensure columns exist on dbo.AdditionalServices
IF COL_LENGTH(N'dbo.AdditionalServices', N'ServicePrice') IS NULL
BEGIN
    ALTER TABLE dbo.AdditionalServices ADD ServicePrice decimal(18,2) NOT NULL CONSTRAINT DF_AdditionalServices_Price DEFAULT 0.00;
    PRINT 'Added ServicePrice column to dbo.AdditionalServices.';
END;
GO

IF COL_LENGTH(N'dbo.AdditionalServices', N'CategoryName') IS NULL
BEGIN
    ALTER TABLE dbo.AdditionalServices ADD CategoryName nvarchar(100) NULL;
    PRINT 'Added CategoryName column to dbo.AdditionalServices.';
END;
GO

IF COL_LENGTH(N'dbo.AdditionalServices', N'CreatedAt') IS NULL
BEGIN
    ALTER TABLE dbo.AdditionalServices ADD CreatedAt datetime2 NOT NULL CONSTRAINT DF_AdditionalServices_CreatedAt DEFAULT SYSUTCDATETIME();
    PRINT 'Added CreatedAt column to dbo.AdditionalServices.';
END;
GO

-- 2. Concurrency-Safe Sequence Object for ADD-#### Generation
-- Prevents race conditions, duplicate key violations, and locks under high concurrency.
IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = 'Seq_AdditionalServicesID' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    DECLARE @MaxVal INT = 0;
    SELECT @MaxVal = ISNULL(MAX(TRY_CAST(SUBSTRING(AdditionalServicesID, 5, 20) AS INT)), 0)
    FROM dbo.AdditionalServices
    WHERE AdditionalServicesID LIKE 'ADD-%';

    DECLARE @StartVal INT = @MaxVal + 1;
    DECLARE @Sql NVARCHAR(MAX) = N'CREATE SEQUENCE dbo.Seq_AdditionalServicesID AS INT START WITH ' + CAST(@StartVal AS NVARCHAR(10)) + N' INCREMENT BY 1;';
    EXEC sp_executesql @Sql;
    PRINT 'Created sequence dbo.Seq_AdditionalServicesID starting at ' + CAST(@StartVal AS NVARCHAR(10));
END;
GO

-- 3. Stored Procedure: Preview Next Additional Service ID (Read-Only)
CREATE OR ALTER PROCEDURE dbo.sp_GetNextAdditionalServiceID
    @NextID NVARCHAR(30) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Peek the current sequence position without prematurely exhausting values
    DECLARE @CurVal SQL_VARIANT;
    SELECT @CurVal = current_value FROM sys.sequences WHERE name = 'Seq_AdditionalServicesID' AND schema_id = SCHEMA_ID('dbo');
    
    DECLARE @SeqVal INT = ISNULL(CAST(@CurVal AS INT), 1);

    -- If this value is already consumed in the table, predict the next value
    WHILE EXISTS (SELECT 1 FROM dbo.AdditionalServices WHERE AdditionalServicesID = 'ADD-' + RIGHT('0000' + CAST(@SeqVal AS VARCHAR(10)), 4))
    BEGIN
        SET @SeqVal = @SeqVal + 1;
    END
    
    SET @NextID = 'ADD-' + RIGHT('0000' + CAST(@SeqVal AS VARCHAR(10)), 4);
    SELECT @NextID AS NextAdditionalServiceID;
END;
GO

-- 4. Stored Procedure: Atomic Add-On Creation with ID Generation & Image Storage
CREATE OR ALTER PROCEDURE dbo.sp_CreateAdditionalService
    @AdditionalServiceName nvarchar(255),
    @AdditionalServicesDescription nvarchar(255),
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
    
    -- Atomic allocation from sequence object
    DECLARE @SeqVal INT = NEXT VALUE FOR dbo.Seq_AdditionalServicesID;
    DECLARE @NewID NVARCHAR(30) = 'ADD-' + RIGHT('0000' + CAST(@SeqVal AS VARCHAR(10)), 4);

    -- Concurrency guard: in case of any prior manual ID insertions
    WHILE EXISTS (SELECT 1 FROM dbo.AdditionalServices WITH (UPDLOCK, HOLDLOCK) WHERE AdditionalServicesID = @NewID)
    BEGIN
        SET @SeqVal = NEXT VALUE FOR dbo.Seq_AdditionalServicesID;
        SET @NewID = 'ADD-' + RIGHT('0000' + CAST(@SeqVal AS VARCHAR(10)), 4);
    END

    INSERT INTO dbo.AdditionalServices (
        AdditionalServicesID,
        AdditionalServiceName,
        AdditionalServicesDescription,
        ServicePrice,
        CategoryName,
        AdditionalServiceImage,
        AdditionalServiceImageMimeType,
        CreatedAt
    )
    VALUES (
        @NewID,
        @AdditionalServiceName,
        @AdditionalServicesDescription,
        @ServicePrice,
        @CategoryName,
        @AdditionalServiceImage,
        @AdditionalServiceImageMimeType,
        SYSUTCDATETIME()
    );

    -- Audit trail logging
    IF @CreatedByAdminID IS NOT NULL
    BEGIN
        DECLARE @ActorType nvarchar(20) = CASE WHEN @CreatedByAdminID LIKE 'SADM-%' THEN 'SystemAdmin' ELSE 'Admin' END;
        IF OBJECT_ID(N'dbo.sp_AddHistoryLog', N'P') IS NOT NULL
        BEGIN
            EXEC dbo.sp_AddHistoryLog 
                @ActionType = 'CREATE', 
                @EntityName = 'AdditionalServices', 
                @EntityID = @NewID, 
                @ActionDetails = 'Created new Additional Service with atomic ADD-#### ID generation and media upload.',
                @ChangedByType = @ActorType, 
                @ActorID = @CreatedByAdminID;
        END
    END

    COMMIT TRANSACTION;

    SET @GeneratedID = @NewID;
    SELECT @NewID AS AdditionalServicesID;
END;
GO

-- 5. Stored Procedure: Summary of Additional Services with Price & Category
CREATE OR ALTER PROCEDURE dbo.sp_GetAdditionalServicesSummary
AS
BEGIN
    SET NOCOUNT ON;
    SELECT 
        AdditionalServicesID, 
        AdditionalServiceName, 
        AdditionalServicesDescription,
        ISNULL(ServicePrice, 0.00) AS ServicePrice,
        ISNULL(CategoryName, 'Add-On') AS CategoryName,
        CAST(CASE WHEN AdditionalServiceImage IS NOT NULL THEN 1 ELSE 0 END AS bit) AS HasImage
    FROM dbo.AdditionalServices
    ORDER BY AdditionalServicesID;
END;
GO

-- 6. Stored Procedure: Detail with Image Stream
CREATE OR ALTER PROCEDURE dbo.sp_GetAdditionalServiceDetailWithImage
    @AdditionalServicesID nvarchar(30)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT 
        AdditionalServicesID, 
        AdditionalServiceName, 
        AdditionalServicesDescription,
        ISNULL(ServicePrice, 0.00) AS ServicePrice,
        ISNULL(CategoryName, 'Add-On') AS CategoryName,
        AdditionalServiceImage,
        ISNULL(AdditionalServiceImageMimeType, 'image/jpeg') AS AdditionalServiceImageMimeType,
        CAST(CASE WHEN AdditionalServiceImage IS NOT NULL THEN 1 ELSE 0 END AS bit) AS HasImage
    FROM dbo.AdditionalServices
    WHERE AdditionalServicesID = @AdditionalServicesID;
END;
GO
