-- Migration Script: Reconcile Services RateType, RateType table, and ServiceRate table
SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- 1. Ensure dbo.Services has RateType
IF OBJECT_ID(N'dbo.Services', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.Services', N'RateType') IS NULL
    BEGIN
        ALTER TABLE dbo.Services ADD RateType nvarchar(50) NOT NULL CONSTRAINT DF_Services_RateType DEFAULT 'Per Hour';
        PRINT 'Added RateType column to dbo.Services.';
    END
    ELSE
    BEGIN
        PRINT 'RateType column already exists on dbo.Services.';
    END;
END;
GO

-- 2. Ensure dbo.RateType table and seed baseline catalog
IF OBJECT_ID(N'dbo.RateType', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RateType (
        RateTypeID nvarchar(30) NOT NULL PRIMARY KEY,
        RateTypeName nvarchar(50) NOT NULL,
        RateTypeCode nvarchar(30) NOT NULL UNIQUE,
        RateDescription nvarchar(255) NULL,
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_RateType_CreatedAt DEFAULT SYSUTCDATETIME()
    );
    PRINT 'Created dbo.RateType table.';
END;
GO

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
    PRINT 'Seeded baseline records into dbo.RateType.';
END;
GO

-- 3. Ensure dbo.ServiceRate table
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
    PRINT 'Created dbo.ServiceRate table.';
END;
GO

-- 4. Ensure sequences
IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = 'Seq_RateTypeID')
BEGIN
    CREATE SEQUENCE dbo.Seq_RateTypeID AS INT START WITH 5 INCREMENT BY 1;
    PRINT 'Created sequence dbo.Seq_RateTypeID.';
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = 'Seq_ServiceRateID')
BEGIN
    CREATE SEQUENCE dbo.Seq_ServiceRateID AS INT START WITH 1 INCREMENT BY 1;
    PRINT 'Created sequence dbo.Seq_ServiceRateID.';
END;
GO
