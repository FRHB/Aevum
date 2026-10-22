-- =================================================================================
-- Script: ResetAndRecreateDatabase.sql
-- Purpose: Drops and recreates PhotoAevumStudioDB cleanly.
-- Use this on development machines if you wish to start from a blank database
-- and let EF Core MigrateAsync apply all migrations and baseline seeding.
-- =================================================================================

USE master;
GO

IF EXISTS (SELECT name FROM sys.databases WHERE name = N'PhotoAevumStudioDB')
BEGIN
    ALTER DATABASE [PhotoAevumStudioDB] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [PhotoAevumStudioDB];
    PRINT 'Dropped database PhotoAevumStudioDB.';
END
GO

CREATE DATABASE [PhotoAevumStudioDB];
PRINT 'Created empty database PhotoAevumStudioDB. Start PhotoAevumStudio.Api to migrate and seed.';
GO
