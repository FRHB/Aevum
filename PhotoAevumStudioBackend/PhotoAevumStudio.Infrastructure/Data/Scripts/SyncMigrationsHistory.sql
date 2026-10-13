-- =================================================================================
-- Script: SyncMigrationsHistory.sql
-- Purpose: Synchronize __EFMigrationsHistory table with existing database objects.
-- Use this if running on a machine where tables (e.g. Services, Bookings) were
-- created manually or imported from a script, causing error:
-- "There is already an object named 'Services' in the database"
-- =================================================================================

USE [PhotoAevumStudioDB];
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = '__EFMigrationsHistory')
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
    PRINT 'Created __EFMigrationsHistory table.';
END
GO

-- 1. AddIdentityAndPhotos
IF OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = '20261008080923_AddIdentityAndPhotos')
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES ('20261008080923_AddIdentityAndPhotos', '10.0.12');
    PRINT 'Recorded migration: 20261008080923_AddIdentityAndPhotos';
END
GO

-- 2. ExpandDomainModels (Services, Employees, Equipment)
IF OBJECT_ID(N'dbo.Services', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = '20261008081827_ExpandDomainModels')
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES ('20261008081827_ExpandDomainModels', '10.0.12');
    PRINT 'Recorded migration: 20261008081827_ExpandDomainModels';
END
GO

-- 3. UpdateBookingKeysAndDate_FixCascade (Bookings, AuditLogs, Assignments)
IF OBJECT_ID(N'dbo.Bookings', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = '20261010090001_UpdateBookingKeysAndDate_FixCascade')
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES ('20261010090001_UpdateBookingKeysAndDate_FixCascade', '10.0.12');
    PRINT 'Recorded migration: 20261010090001_UpdateBookingKeysAndDate_FixCascade';
END
GO

-- 4. AddAdminsView (Admins view)
IF OBJECT_ID(N'dbo.Admins', N'V') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = '20261010100000_AddAdminsView')
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES ('20261010100000_AddAdminsView', '10.0.12');
    PRINT 'Recorded migration: 20261010100000_AddAdminsView';
END
GO

-- 5. AddMustChangePasswordToApplicationUser (MustChangePassword column)
IF COL_LENGTH(N'dbo.AspNetUsers', N'MustChangePassword') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = '20261013034024_AddMustChangePasswordToApplicationUser')
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES ('20261013034024_AddMustChangePasswordToApplicationUser', '10.0.12');
    PRINT 'Recorded migration: 20261013034024_AddMustChangePasswordToApplicationUser';
END
GO

PRINT 'Migration history synchronization complete.';
GO
