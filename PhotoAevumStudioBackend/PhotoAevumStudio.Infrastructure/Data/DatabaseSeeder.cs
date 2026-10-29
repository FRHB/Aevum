using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PhotoAevumStudio.Application.Auth;
using System.Data;

namespace PhotoAevumStudio.Infrastructure.Data;

/// <summary>
/// Database maintenance, role seeding, and stored procedure management.
/// All hardcoded seed users, services, and equipment have been removed to enforce strict reliance
/// on the SQL Server database.
/// </summary>
public static class DatabaseSeeder
{
    /// <summary>
    /// Seeds the baseline application roles (Admin, Employee, Customer, SystemAdmin) into AspNetRoles.
    /// Does not seed any mock users or domain entities.
    /// </summary>
    public static async Task SeedRolesAsync(RoleManager<IdentityRole> roleManager)
    {
        foreach (var role in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }
    }

    /// <summary>
    /// Reconciles __EFMigrationsHistory if database tables already exist (e.g. from an imported SQL script or prior restore),
    /// preventing SqlException on machines with pre-existing schema.
    /// </summary>
    public static async Task ReconcileMigrationHistoryAsync(ApplicationDbContext db)
    {
        if (!await db.Database.CanConnectAsync())
            return;

        const string reconcileScript = @"
            IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = '__EFMigrationsHistory')
            BEGIN
                CREATE TABLE [__EFMigrationsHistory] (
                    [MigrationId] nvarchar(150) NOT NULL,
                    [ProductVersion] nvarchar(32) NOT NULL,
                    CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
                );
            END;

            IF OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = '20261008080923_AddIdentityAndPhotos')
            BEGIN
                INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
                VALUES ('20261008080923_AddIdentityAndPhotos', '10.0.12');
            END;

            IF OBJECT_ID(N'dbo.Services', N'U') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = '20261008081827_ExpandDomainModels')
            BEGIN
                INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
                VALUES ('20261008081827_ExpandDomainModels', '10.0.12');
            END;

            IF (OBJECT_ID(N'dbo.Bookings', N'U') IS NOT NULL OR OBJECT_ID(N'dbo.EventAppointment', N'U') IS NOT NULL)
               AND NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = '20261010090001_UpdateBookingKeysAndDate_FixCascade')
            BEGIN
                IF OBJECT_ID(N'dbo.AuditLogs', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[AuditLogs] (
                        [Id] int NOT NULL IDENTITY,
                        [UserId] nvarchar(max) NOT NULL,
                        [UserEmail] nvarchar(max) NOT NULL,
                        [Action] nvarchar(max) NOT NULL,
                        [Resource] nvarchar(max) NOT NULL,
                        [ResourceId] nvarchar(max) NULL,
                        [Timestamp] datetime2 NOT NULL,
                        [Details] nvarchar(max) NULL,
                        CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id])
                    );
                END;

                INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
                VALUES ('20261010090001_UpdateBookingKeysAndDate_FixCascade', '10.0.12');
            END;

            IF OBJECT_ID(N'dbo.Admins', N'V') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = '20261010100000_AddAdminsView')
            BEGIN
                INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
                VALUES ('20261010100000_AddAdminsView', '10.0.12');
            END;

            IF COL_LENGTH(N'dbo.AspNetUsers', N'MustChangePassword') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = '20261013034024_AddMustChangePasswordToApplicationUser')
            BEGIN
                INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
                VALUES ('20261013034024_AddMustChangePasswordToApplicationUser', '10.0.12');
            END;

            IF COL_LENGTH(N'dbo.EmployeeSpecialization', N'SpecializationDescription') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = '20261026081300_AddSpecializationMissingColumns')
            BEGIN
                INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
                VALUES ('20261026081300_AddSpecializationMissingColumns', '10.0.12');
            END;
        ";

        try
        {
            await db.Database.ExecuteSqlRawAsync(reconcileScript);
        }
        catch
        {
            // Best effort
        }
    }

    /// <summary>
    /// Ensures that all required schema updates and stored procedures exist in the database.
    /// </summary>
    public static async Task EnsureStoredProceduresAsync(ApplicationDbContext db)
    {
        if (!await db.Database.CanConnectAsync())
            return;

        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync();

            await using var setCmd = conn.CreateCommand();
            setCmd.CommandText = @"SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
                IF OBJECT_ID(N'dbo.Admin', N'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH(N'dbo.Admin', N'isFirstLogin') IS NULL
                    BEGIN
                        ALTER TABLE dbo.Admin ADD isFirstLogin bit NOT NULL CONSTRAINT DF_Admin_isFirstLogin DEFAULT 1;
                    END;
                    IF COL_LENGTH(N'dbo.Admin', N'Email') IS NULL
                    BEGIN
                        ALTER TABLE dbo.Admin ADD Email nvarchar(255) NULL;
                    END;
                END;

                IF OBJECT_ID(N'dbo.SystemAdmin', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.SystemAdmin (
                        SystemAdminID nvarchar(30) NOT NULL PRIMARY KEY,
                        FirstName nvarchar(255) NOT NULL,
                        LastName nvarchar(255) NOT NULL,
                        MiddleName nvarchar(255) NULL,
                        PasswordHash nvarchar(255) NOT NULL,
                        CreatedAt datetime2 NOT NULL CONSTRAINT DF_SystemAdmin_CreatedAt DEFAULT SYSUTCDATETIME()
                    );
                END;

                IF OBJECT_ID(N'dbo.Employees', N'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH(N'dbo.Employees', N'isFirstLogin') IS NULL
                    BEGIN
                        ALTER TABLE dbo.Employees ADD isFirstLogin bit NOT NULL CONSTRAINT DF_Employees_isFirstLogin DEFAULT 1;
                    END;
                    IF COL_LENGTH(N'dbo.Employees', N'DaysOff') IS NULL
                    BEGIN
                        ALTER TABLE dbo.Employees ADD DaysOff nvarchar(50) NOT NULL CONSTRAINT DF_Employees_DaysOff DEFAULT 'Sunday';
                    END;
                    IF COL_LENGTH(N'dbo.Employees', N'Id') IS NULL
                    BEGIN
                        ALTER TABLE dbo.Employees ADD Id int IDENTITY(1,1);
                    END;
                    IF COL_LENGTH(N'dbo.Employees', N'IdentityUserId') IS NULL
                    BEGIN
                        ALTER TABLE dbo.Employees ADD IdentityUserId nvarchar(450) NULL;
                    END;
                    IF COL_LENGTH(N'dbo.Employees', N'Status') IS NULL
                    BEGIN
                        ALTER TABLE dbo.Employees ADD Status nvarchar(50) NOT NULL CONSTRAINT DF_Employees_EF_Status DEFAULT 'Active';
                    END;
                    IF COL_LENGTH(N'dbo.Employees', N'Specializations') IS NULL
                    BEGIN
                        ALTER TABLE dbo.Employees ADD Specializations nvarchar(max) NOT NULL CONSTRAINT DF_Employees_EF_Specializations DEFAULT '[]';
                    END;
                    EXEC(N'UPDATE dbo.Employees SET IdentityUserId = EmployeeID WHERE IdentityUserId IS NULL; UPDATE dbo.Employees SET Status = EmployeeStatus WHERE Status IS NULL;');
                END;

                IF OBJECT_ID(N'dbo.EmployeeLeaves', N'U') IS NULL AND OBJECT_ID(N'dbo.EmployeeLeave', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.EmployeeLeaves (
                        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        LeaveId nvarchar(50) NOT NULL,
                        EmployeeStudioId nvarchar(50) NOT NULL,
                        EmployeeName nvarchar(255) NOT NULL,
                        LeaveType nvarchar(50) NOT NULL,
                        StartDate date NOT NULL,
                        EndDate date NOT NULL,
                        Reason nvarchar(500) NULL,
                        Status nvarchar(20) NOT NULL CONSTRAINT DF_Leave_Status DEFAULT 'Pending',
                        ApprovedBy nvarchar(255) NULL,
                        ApprovedAt datetime2 NULL,
                        RejectionReason nvarchar(500) NULL,
                        CreatedAt datetime2 NOT NULL CONSTRAINT DF_Leave_CreatedAt DEFAULT SYSUTCDATETIME()
                    );
                END;

                IF OBJECT_ID(N'dbo.EmployeeTimesheets', N'U') IS NULL AND OBJECT_ID(N'dbo.EmployeeTimesheet', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.EmployeeTimesheets (
                        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        TimesheetId nvarchar(50) NOT NULL,
                        EmployeeStudioId nvarchar(50) NOT NULL,
                        EmployeeName nvarchar(255) NOT NULL,
                        WorkDate date NOT NULL,
                        ClockIn datetime2 NOT NULL,
                        ClockOut datetime2 NULL,
                        TotalHours float NOT NULL CONSTRAINT DF_Timesheet_Hours DEFAULT 0.0,
                        Status nvarchar(20) NOT NULL CONSTRAINT DF_Timesheet_Status DEFAULT 'Open',
                        IsDayOffWork bit NOT NULL CONSTRAINT DF_Timesheet_DayOff DEFAULT 0,
                        Notes nvarchar(500) NULL,
                        AdjustedBy nvarchar(255) NULL,
                        AdjustmentReason nvarchar(500) NULL,
                        CreatedAt datetime2 NOT NULL CONSTRAINT DF_Timesheet_CreatedAt DEFAULT SYSUTCDATETIME()
                    );
                END;

                IF OBJECT_ID(N'dbo.Services', N'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH(N'dbo.Services', N'ServiceImage') IS NULL
                        ALTER TABLE dbo.Services ADD ServiceImage varbinary(max) NULL;
                    IF COL_LENGTH(N'dbo.Services', N'ServiceImageMimeType') IS NULL
                        ALTER TABLE dbo.Services ADD ServiceImageMimeType nvarchar(100) NULL CONSTRAINT DF_Services_MimeType DEFAULT 'image/jpeg';
                    ALTER TABLE dbo.Services ALTER COLUMN ServicesDescription nvarchar(1000) NOT NULL;
                    IF COL_LENGTH(N'dbo.Services', N'RateType') IS NULL
                        ALTER TABLE dbo.Services ADD RateType nvarchar(50) NOT NULL CONSTRAINT DF_Services_RateType DEFAULT 'Per Hour';
                END;

                IF OBJECT_ID(N'dbo.RateType', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.RateType (
                        RateTypeID nvarchar(30) NOT NULL PRIMARY KEY,
                        RateTypeName nvarchar(50) NOT NULL,
                        RateTypeCode nvarchar(30) NOT NULL UNIQUE,
                        RateDescription nvarchar(255) NULL,
                        CreatedAt datetime2 NOT NULL CONSTRAINT DF_RateType_CreatedAt DEFAULT SYSUTCDATETIME()
                    );
                END;

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
                END;

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
                END;

                IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = 'Seq_RateTypeID')
                BEGIN
                    CREATE SEQUENCE dbo.Seq_RateTypeID AS INT START WITH 5 INCREMENT BY 1;
                END;

                IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = 'Seq_ServiceRateID')
                BEGIN
                    CREATE SEQUENCE dbo.Seq_ServiceRateID AS INT START WITH 1 INCREMENT BY 1;
                END;

                IF OBJECT_ID(N'dbo.ServiceCategory', N'U') IS NOT NULL
                BEGIN
                    ALTER TABLE dbo.ServiceCategory ALTER COLUMN ServiceCategoryDescription nvarchar(1000) NOT NULL;
                END;

                IF OBJECT_ID(N'dbo.AdditionalServices', N'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH(N'dbo.AdditionalServices', N'AdditionalServiceImage') IS NULL
                        ALTER TABLE dbo.AdditionalServices ADD AdditionalServiceImage varbinary(max) NULL;
                    IF COL_LENGTH(N'dbo.AdditionalServices', N'AdditionalServiceImageMimeType') IS NULL
                        ALTER TABLE dbo.AdditionalServices ADD AdditionalServiceImageMimeType nvarchar(100) NULL CONSTRAINT DF_AdditionalServices_MimeType DEFAULT 'image/jpeg';
                    ALTER TABLE dbo.AdditionalServices ALTER COLUMN AdditionalServicesDescription nvarchar(1000) NULL;
                END;

                IF OBJECT_ID(N'dbo.ComboPackage', N'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH(N'dbo.ComboPackage', N'ComboPackageImage') IS NULL
                        ALTER TABLE dbo.ComboPackage ADD ComboPackageImage varbinary(max) NULL;
                    IF COL_LENGTH(N'dbo.ComboPackage', N'ComboPackageImageMimeType') IS NULL
                        ALTER TABLE dbo.ComboPackage ADD ComboPackageImageMimeType nvarchar(100) NULL CONSTRAINT DF_ComboPackage_MimeType DEFAULT 'image/jpeg';
                    IF COL_LENGTH(N'dbo.ComboPackage', N'ComboPackagePrice') IS NULL
                        ALTER TABLE dbo.ComboPackage ADD ComboPackagePrice decimal(18,2) NOT NULL CONSTRAINT DF_ComboPackage_Price DEFAULT 0.00;
                    ALTER TABLE dbo.ComboPackage ALTER COLUMN ComboPackageDescription nvarchar(1000) NOT NULL;
                END;

                IF OBJECT_ID(N'dbo.ComboPackageJoin', N'U') IS NOT NULL
                BEGIN
                    ALTER TABLE dbo.ComboPackageJoin ALTER COLUMN ServicesID nvarchar(30) NULL;
                END;

                IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = 'Seq_ComboPackageID')
                BEGIN
                    DECLARE @InitMaxVal INT = 0;
                    IF OBJECT_ID(N'dbo.ComboPackage', N'U') IS NOT NULL
                    BEGIN
                        SELECT @InitMaxVal = ISNULL(MAX(TRY_CAST(SUBSTRING(ComboPackageID, 5, 20) AS INT)), 0)
                        FROM dbo.ComboPackage
                        WHERE ComboPackageID LIKE 'PKG-%';
                    END;
                    DECLARE @CreateSeqSql NVARCHAR(MAX) = N'CREATE SEQUENCE dbo.Seq_ComboPackageID AS INT START WITH ' 
                        + CAST((@InitMaxVal + 1) AS NVARCHAR(20)) + N' INCREMENT BY 1;';
                    EXEC sp_executesql @CreateSeqSql;
                END;

                IF OBJECT_ID(N'dbo.Equipment', N'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH(N'dbo.Equipment', N'EquipmentDescription') IS NULL
                        ALTER TABLE dbo.Equipment ADD EquipmentDescription nvarchar(1000) NULL;
                    IF COL_LENGTH(N'dbo.Equipment', N'ImagePath') IS NULL
                        ALTER TABLE dbo.Equipment ADD ImagePath nvarchar(500) NULL;
                    IF COL_LENGTH(N'dbo.Equipment', N'CreatedAt') IS NULL
                        ALTER TABLE dbo.Equipment ADD CreatedAt datetime2 NOT NULL CONSTRAINT DF_Equipment_CreatedAt DEFAULT SYSUTCDATETIME();
                END;

                IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = 'EquipmentIdSequence' AND schema_id = SCHEMA_ID('dbo'))
                BEGIN
                    DECLARE @InitEqpMaxVal INT = 0;
                    IF OBJECT_ID(N'dbo.Equipment', N'U') IS NOT NULL
                    BEGIN
                        SELECT @InitEqpMaxVal = ISNULL(MAX(TRY_CAST(SUBSTRING(EquipmentID, 5, 10) AS INT)), 0)
                        FROM dbo.Equipment
                        WHERE EquipmentID LIKE 'EQP-%';
                    END;
                    DECLARE @CreateEqpSeqSql NVARCHAR(MAX) = N'CREATE SEQUENCE dbo.EquipmentIdSequence AS INT START WITH ' 
                        + CAST((@InitEqpMaxVal + 1) AS NVARCHAR(20)) + N' INCREMENT BY 1 MINVALUE 1 NO CYCLE CACHE 10;';
                    EXEC sp_executesql @CreateEqpSeqSql;
                END;
            ";
            await setCmd.ExecuteNonQueryAsync();

            await using var xenditSchemaCmd = conn.CreateCommand();
            xenditSchemaCmd.CommandText = @"
                IF OBJECT_ID(N'dbo.BookingNegotiation', N'U') IS NOT NULL
                BEGIN
                    ALTER TABLE dbo.BookingNegotiation ALTER COLUMN EventAppointmentID nvarchar(30) NULL;

                    IF COL_LENGTH(N'dbo.BookingNegotiation', N'ConsultationDate') IS NULL
                        ALTER TABLE dbo.BookingNegotiation ADD ConsultationDate date NULL;

                    IF COL_LENGTH(N'dbo.BookingNegotiation', N'ConsultationTime') IS NULL
                        ALTER TABLE dbo.BookingNegotiation ADD ConsultationTime time NULL;

                    IF COL_LENGTH(N'dbo.BookingNegotiation', N'MeetingLink') IS NULL
                        ALTER TABLE dbo.BookingNegotiation ADD MeetingLink nvarchar(500) NULL;

                    IF COL_LENGTH(N'dbo.BookingNegotiation', N'InquiryNotes') IS NULL
                        ALTER TABLE dbo.BookingNegotiation ADD InquiryNotes nvarchar(1000) NULL;

                    IF COL_LENGTH(N'dbo.BookingNegotiation', N'NegotiationStatus') IS NULL
                        ALTER TABLE dbo.BookingNegotiation ADD NegotiationStatus nvarchar(50) NOT NULL CONSTRAINT DF_BookingNegotiation_Status DEFAULT 'Pending_Call';

                    IF COL_LENGTH(N'dbo.BookingNegotiation', N'CreatedAt') IS NULL
                        ALTER TABLE dbo.BookingNegotiation ADD CreatedAt datetime2 NOT NULL CONSTRAINT DF_BookingNegotiation_Created DEFAULT SYSUTCDATETIME();
                END
                ELSE
                BEGIN
                    CREATE TABLE dbo.BookingNegotiation (
                        NegotiationID nvarchar(30) NOT NULL PRIMARY KEY,
                        EventAppointmentID nvarchar(30) NULL,
                        CustomerID nvarchar(30) NOT NULL,
                        EmployeeID nvarchar(30) NOT NULL, 
                        ConsultationDate date NULL,
                        ConsultationTime time NULL,
                        MeetingLink nvarchar(500) NULL,
                        InquiryNotes nvarchar(1000) NULL,
                        NegotiationStatus nvarchar(50) NOT NULL CONSTRAINT DF_BookingNegotiation_Status DEFAULT 'Pending_Call',
                        CreatedAt datetime2 NOT NULL CONSTRAINT DF_BookingNegotiation_Created DEFAULT SYSUTCDATETIME()
                    );
                END;

                IF OBJECT_ID(N'dbo.EventAppointment', N'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH(N'dbo.EventAppointment', N'EventName') IS NULL
                        ALTER TABLE dbo.EventAppointment ADD EventName nvarchar(255) NOT NULL CONSTRAINT DF_EventAppointment_Name DEFAULT 'Photoshoot Event';
                    IF COL_LENGTH(N'dbo.EventAppointment', N'EventDescription') IS NULL
                        ALTER TABLE dbo.EventAppointment ADD EventDescription nvarchar(255) NULL;
                    IF COL_LENGTH(N'dbo.EventAppointment', N'TotalAmount') IS NULL
                        ALTER TABLE dbo.EventAppointment ADD TotalAmount decimal(18,2) NOT NULL CONSTRAINT DF_EventAppointment_Total DEFAULT 0.00;
                    IF COL_LENGTH(N'dbo.EventAppointment', N'DownpaymentAmount') IS NULL
                        ALTER TABLE dbo.EventAppointment ADD DownpaymentAmount decimal(18,2) NOT NULL CONSTRAINT DF_EventAppointment_Downpayment DEFAULT 0.00;
                    IF COL_LENGTH(N'dbo.EventAppointment', N'FinalBalanceAmount') IS NULL
                        ALTER TABLE dbo.EventAppointment ADD FinalBalanceAmount decimal(18,2) NOT NULL CONSTRAINT DF_EventAppointment_Balance DEFAULT 0.00;
                    IF COL_LENGTH(N'dbo.EventAppointment', N'DownpaymentStatus') IS NULL
                        ALTER TABLE dbo.EventAppointment ADD DownpaymentStatus nvarchar(30) NOT NULL CONSTRAINT DF_EventAppointment_DownpaymentStatus DEFAULT 'Pending';
                    IF COL_LENGTH(N'dbo.EventAppointment', N'DownpaymentPaidAt') IS NULL
                        ALTER TABLE dbo.EventAppointment ADD DownpaymentPaidAt datetime2 NULL;
                    IF COL_LENGTH(N'dbo.EventAppointment', N'HoldExpiresAt') IS NULL
                        ALTER TABLE dbo.EventAppointment ADD HoldExpiresAt datetime2 NULL;
                    IF COL_LENGTH(N'dbo.EventAppointment', N'XenditInvoiceId') IS NULL
                        ALTER TABLE dbo.EventAppointment ADD XenditInvoiceId nvarchar(100) NULL;
                    IF COL_LENGTH(N'dbo.EventAppointment', N'XenditExternalId') IS NULL
                        ALTER TABLE dbo.EventAppointment ADD XenditExternalId nvarchar(100) NULL;
                    IF COL_LENGTH(N'dbo.EventAppointment', N'XenditInvoiceUrl') IS NULL
                        ALTER TABLE dbo.EventAppointment ADD XenditInvoiceUrl nvarchar(500) NULL;
                    IF COL_LENGTH(N'dbo.EventAppointment', N'CreatedAt') IS NULL
                        ALTER TABLE dbo.EventAppointment ADD CreatedAt datetime2 NOT NULL CONSTRAINT DF_EventAppointment_Created DEFAULT SYSUTCDATETIME();
                END
                ELSE
                BEGIN
                    CREATE TABLE dbo.EventAppointment (
                        EventAppointmentID nvarchar(30) NOT NULL PRIMARY KEY,
                        EventName nvarchar(255) NOT NULL,
                        EventDescription nvarchar(255) NULL,
                        CustomerID nvarchar(30) NOT NULL,
                        EventDate date NOT NULL,
                        EventTime time NOT NULL,
                        EventStatus nvarchar(30) NOT NULL CONSTRAINT DF_EventAppointment_Status DEFAULT 'Pending',
                        TotalAmount decimal(18,2) NOT NULL CONSTRAINT DF_EventAppointment_Total DEFAULT 0.00,
                        DownpaymentAmount decimal(18,2) NOT NULL CONSTRAINT DF_EventAppointment_Downpayment DEFAULT 0.00,
                        FinalBalanceAmount decimal(18,2) NOT NULL CONSTRAINT DF_EventAppointment_Balance DEFAULT 0.00,
                        DownpaymentStatus nvarchar(30) NOT NULL CONSTRAINT DF_EventAppointment_DownpaymentStatus DEFAULT 'Pending',
                        DownpaymentPaidAt datetime2 NULL,
                        HoldExpiresAt datetime2 NULL,
                        XenditInvoiceId nvarchar(100) NULL,
                        XenditExternalId nvarchar(100) NULL,
                        XenditInvoiceUrl nvarchar(500) NULL,
                        CreatedAt datetime2 NOT NULL CONSTRAINT DF_EventAppointment_Created DEFAULT SYSUTCDATETIME()
                    );
                END;

                IF OBJECT_ID(N'dbo.EventEmployee', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.EventEmployee (
                        EventEmployeeID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        EventAppointmentID nvarchar(30) NOT NULL,
                        EmployeeID nvarchar(30) NOT NULL,
                        IsTeamLeader bit NOT NULL CONSTRAINT DF_EventEmployee_IsTeamLeader DEFAULT 0
                    );
                END;

                IF OBJECT_ID(N'dbo.EventEquipment', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.EventEquipment (
                        EventEquipmentID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        EventAppointmentID nvarchar(30) NOT NULL,
                        EquipmentID nvarchar(30) NOT NULL
                    );
                END;

                IF OBJECT_ID(N'dbo.EventOvertimeCharges', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.EventOvertimeCharges (
                        OvertimeID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        EventAppointmentID nvarchar(30) NOT NULL,
                        ExtraHours decimal(5,2) NOT NULL,
                        HourlyRate decimal(18,2) NOT NULL,
                        SurchargeAmount decimal(18,2) NOT NULL,
                        Notes nvarchar(500) NULL,
                        LoggedByEmployeeID nvarchar(30) NOT NULL,
                        CreatedAt datetime2 NOT NULL CONSTRAINT DF_EventOvertime_CreatedAt DEFAULT SYSUTCDATETIME()
                    );
                END;

                IF OBJECT_ID(N'dbo.PaymentTransactions', N'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH(N'dbo.PaymentTransactions', N'PaymentType') IS NULL
                        ALTER TABLE dbo.PaymentTransactions ADD PaymentType nvarchar(50) NOT NULL CONSTRAINT DF_PaymentTransactions_Type DEFAULT 'Downpayment_25';
                    IF COL_LENGTH(N'dbo.PaymentTransactions', N'PaymentMethod') IS NULL
                        ALTER TABLE dbo.PaymentTransactions ADD PaymentMethod nvarchar(50) NOT NULL CONSTRAINT DF_PaymentTransactions_Method DEFAULT 'Xendit_Card';
                    IF COL_LENGTH(N'dbo.PaymentTransactions', N'Amount') IS NULL
                        ALTER TABLE dbo.PaymentTransactions ADD Amount decimal(18,2) NOT NULL CONSTRAINT DF_PaymentTransactions_Amount DEFAULT 0.00;
                    IF COL_LENGTH(N'dbo.PaymentTransactions', N'XenditInvoiceId') IS NULL
                        ALTER TABLE dbo.PaymentTransactions ADD XenditInvoiceId nvarchar(100) NULL;
                    IF COL_LENGTH(N'dbo.PaymentTransactions', N'XenditPaymentId') IS NULL
                        ALTER TABLE dbo.PaymentTransactions ADD XenditPaymentId nvarchar(100) NULL;
                    IF COL_LENGTH(N'dbo.PaymentTransactions', N'XenditPaymentChannel') IS NULL
                        ALTER TABLE dbo.PaymentTransactions ADD XenditPaymentChannel nvarchar(100) NULL;
                    IF COL_LENGTH(N'dbo.PaymentTransactions', N'Status') IS NULL
                        ALTER TABLE dbo.PaymentTransactions ADD Status nvarchar(30) NOT NULL CONSTRAINT DF_PaymentTransactions_Status DEFAULT 'Pending';
                    IF COL_LENGTH(N'dbo.PaymentTransactions', N'ExternalReference') IS NULL
                        ALTER TABLE dbo.PaymentTransactions ADD ExternalReference nvarchar(100) NULL;
                    IF COL_LENGTH(N'dbo.PaymentTransactions', N'ReceivedByEmployeeID') IS NULL
                        ALTER TABLE dbo.PaymentTransactions ADD ReceivedByEmployeeID nvarchar(30) NULL;
                    IF COL_LENGTH(N'dbo.PaymentTransactions', N'PaidAt') IS NULL
                        ALTER TABLE dbo.PaymentTransactions ADD PaidAt datetime2 NULL;
                    IF COL_LENGTH(N'dbo.PaymentTransactions', N'CustomerPaymentStatus') IS NULL
                        ALTER TABLE dbo.PaymentTransactions ADD CustomerPaymentStatus nvarchar(40) NULL;
                    IF COL_LENGTH(N'dbo.PaymentTransactions', N'EmployeeRemittanceStatus') IS NULL
                        ALTER TABLE dbo.PaymentTransactions ADD EmployeeRemittanceStatus nvarchar(40) NULL;
                    IF COL_LENGTH(N'dbo.PaymentTransactions', N'AdminApproverID') IS NULL
                        ALTER TABLE dbo.PaymentTransactions ADD AdminApproverID nvarchar(50) NULL;
                    IF COL_LENGTH(N'dbo.PaymentTransactions', N'ApprovalTimestamp') IS NULL
                        ALTER TABLE dbo.PaymentTransactions ADD ApprovalTimestamp datetime2 NULL;
                    IF COL_LENGTH(N'dbo.PaymentTransactions', N'CreatedAt') IS NULL
                        ALTER TABLE dbo.PaymentTransactions ADD CreatedAt datetime2 NOT NULL CONSTRAINT DF_PaymentTransactions_CreatedAt DEFAULT SYSUTCDATETIME();
                END
                ELSE
                BEGIN
                    CREATE TABLE dbo.PaymentTransactions (
                        PaymentTransactionID nvarchar(50) NOT NULL PRIMARY KEY,
                        EventAppointmentID nvarchar(30) NOT NULL,
                        PaymentType nvarchar(50) NOT NULL,
                        PaymentMethod nvarchar(50) NOT NULL,
                        Amount decimal(18,2) NOT NULL,
                        XenditPaymentId nvarchar(100) NULL,
                        XenditInvoiceId nvarchar(100) NULL,
                        XenditPaymentChannel nvarchar(100) NULL,
                        Status nvarchar(30) NOT NULL CONSTRAINT DF_PaymentTransactions_Status DEFAULT 'Pending',
                        CustomerPaymentStatus nvarchar(40) NULL,
                        EmployeeRemittanceStatus nvarchar(40) NULL,
                        AdminApproverID nvarchar(50) NULL,
                        ApprovalTimestamp datetime2 NULL,
                        ExternalReference nvarchar(100) NULL,
                        ReceivedByEmployeeID nvarchar(30) NULL,
                        PaidAt datetime2 NULL,
                        CreatedAt datetime2 NOT NULL CONSTRAINT DF_PaymentTransactions_CreatedAt DEFAULT SYSUTCDATETIME()
                    );
                END;

                IF OBJECT_ID(N'dbo.HistoryLog', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.HistoryLog (
                        LogID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        ActionType nvarchar(50) NOT NULL,
                        EntityName nvarchar(50) NOT NULL,
                        EntityID nvarchar(50) NOT NULL,
                        ActionDetails nvarchar(1000) NOT NULL,
                        NegotiationID nvarchar(30) NULL,
                        EventAppointmentID nvarchar(30) NULL,
                        CustomerID nvarchar(30) NULL,
                        EmployeeID nvarchar(30) NULL,
                        ChangedByType nvarchar(50) NOT NULL,
                        ChangedByCustomerID nvarchar(30) NULL,
                        ChangedByEmployeeID nvarchar(30) NULL,
                        LoggedAt datetime2 NOT NULL CONSTRAINT DF_HistoryLog_LoggedAt DEFAULT SYSUTCDATETIME()
                    );
                END;
            ";
            await xenditSchemaCmd.ExecuteNonQueryAsync();

            var candidatePaths = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "database", "PhotoAevumStudioStoredProcedures.sql"),
                Path.Combine(Directory.GetCurrentDirectory(), "database", "PhotoAevumStudioStoredProcedures.sql"),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "database", "PhotoAevumStudioStoredProcedures.sql")),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "database", "PhotoAevumStudioStoredProcedures.sql")),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PhotoAevumStudioBackend", "database", "PhotoAevumStudioStoredProcedures.sql")),
                Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "PhotoAevumStudioBackend", "database", "PhotoAevumStudioStoredProcedures.sql")),
                @"c:\Aevum\PhotoAevumStudioBackend\database\PhotoAevumStudioStoredProcedures.sql"
            };

            string? script = null;
            foreach (var p in candidatePaths)
            {
                if (File.Exists(p))
                {
                    script = await File.ReadAllTextAsync(p);
                    break;
                }
            }

            if (!string.IsNullOrWhiteSpace(script))
            {
                var batches = System.Text.RegularExpressions.Regex.Split(
                    script,
                    @"^\s*GO\s*$",
                    System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                foreach (var batch in batches)
                {
                    var trimmed = batch.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed)) continue;

                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SET QUOTED_IDENTIFIER ON;\nSET ANSI_NULLS ON;\n" + trimmed;
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }
        catch
        {
            // Stored procedure initialization errors are non-fatal on startup
        }
    }
}
