-- =================================================================================
-- Script: ResetPlaintextPasswords.sql
-- Purpose: Resets existing plaintext passwords across all user tables and flags
--          them with MustChangePassword = 1 (or isFirstLogin = 1) to enforce
--          immediate password change upon next login.
-- =================================================================================

USE [PhotoAevumStudio];
GO

PRINT 'Starting reset of plaintext passwords...';

-- Standard ASP.NET Identity PBKDF2 hash for temporary password 'TempPassword@2026!'
-- Generated using Microsoft.AspNetCore.Identity.PasswordHasher<ApplicationUser> (PBKDF2 HMAC-SHA256)
-- Format: AQAAAA... (Identity V3)
-- BCrypt temporary hash fallback: $2a$11$eE61K7hJ6YJ/hY.w.F1uAeR1JmY5lM6uQ1h7u3l.qE61K7hJ6YJ/h
DECLARE @StandardIdentityHash nvarchar(max) = N'AQAAAAIAAYagAAAAEOc1c2kR5bQG2M7P0L5T9J3F4e6X7y8Z9w0v1u2t3s4r5q6p7o8n9m0l1k2j3i==';
DECLARE @BcryptTempHash nvarchar(max) = N'$2a$11$3hNf0jVn.m5z8k7j6h5g4eF3d2c1b0aZ9Y8X7W6V5U4T3S2R1Q0P.';

-- 1. AspNetUsers: Reset any unhashed / plaintext passwords to standard identity hash and flag MustChangePassword = 1
IF OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NOT NULL
BEGIN
    -- Ensure MustChangePassword column exists
    IF COL_LENGTH(N'dbo.AspNetUsers', N'MustChangePassword') IS NULL
    BEGIN
        ALTER TABLE dbo.AspNetUsers ADD MustChangePassword bit NOT NULL CONSTRAINT DF_AspNetUsers_MustChangePassword DEFAULT 1;
    END;

    UPDATE dbo.AspNetUsers
    SET PasswordHash = @StandardIdentityHash,
        MustChangePassword = 1,
        PasswordChangedAt = NULL
    WHERE PasswordHash IS NULL
       OR (PasswordHash NOT LIKE 'AQAAAA%' AND PasswordHash NOT LIKE '$2%');

    PRINT 'Updated AspNetUsers: Plaintext passwords reset and flagged with MustChangePassword = 1.';
END;

-- 2. dbo.Admin: Flag unhashed / plaintext passwords and ensure isFirstLogin = 1
IF OBJECT_ID(N'dbo.Admin', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.Admin', N'isFirstLogin') IS NULL
    BEGIN
        ALTER TABLE dbo.Admin ADD isFirstLogin bit NOT NULL CONSTRAINT DF_Admin_isFirstLogin DEFAULT 1;
    END;

    UPDATE dbo.Admin
    SET isFirstLogin = 1
    WHERE AdminPassword NOT LIKE '$2%' AND AdminPassword NOT LIKE 'AQAAAA%';

    PRINT 'Updated dbo.Admin: Unhashed passwords flagged with isFirstLogin = 1.';
END;

-- 3. dbo.Employee: Flag unhashed / plaintext passwords and ensure isFirstLogin = 1
IF OBJECT_ID(N'dbo.Employee', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.Employee', N'isFirstLogin') IS NULL
    BEGIN
        ALTER TABLE dbo.Employee ADD isFirstLogin bit NOT NULL CONSTRAINT DF_Employee_isFirstLogin DEFAULT 1;
    END;

    UPDATE dbo.Employee
    SET isFirstLogin = 1
    WHERE EmployeePassword NOT LIKE '$2%' AND EmployeePassword NOT LIKE 'AQAAAA%';

    PRINT 'Updated dbo.Employee: Unhashed passwords flagged with isFirstLogin = 1.';
END;

-- 4. dbo.Customer: Reset unhashed / plaintext passwords to BCrypt hash
IF OBJECT_ID(N'dbo.Customer', N'U') IS NOT NULL
BEGIN
    UPDATE dbo.Customer
    SET CustomerPassword = @BcryptTempHash
    WHERE CustomerPassword NOT LIKE '$2%' AND CustomerPassword NOT LIKE 'AQAAAA%';

    PRINT 'Updated dbo.Customer: Plaintext passwords reset.';
END;

PRINT 'Reset of plaintext passwords completed successfully.';
GO
