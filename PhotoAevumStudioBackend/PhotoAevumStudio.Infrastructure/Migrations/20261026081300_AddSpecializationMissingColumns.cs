using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoAevumStudio.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSpecializationMissingColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                -- 1. EmployeeSpecialization missing columns / table creation
                IF OBJECT_ID(N'dbo.EmployeeSpecialization', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.EmployeeSpecialization (
                        SpecializationID nvarchar(30) NOT NULL PRIMARY KEY,
                        SpecializationName nvarchar(255) NOT NULL,
                        SpecializationDescription nvarchar(1000) NULL,
                        Specialization nvarchar(255) NOT NULL,
                        Description nvarchar(1000) NULL,
                        ColorTag nvarchar(50) NOT NULL CONSTRAINT DF_EmployeeSpecialization_ColorTag DEFAULT 'Blue',
                        CreatedAt datetime2 NOT NULL CONSTRAINT DF_EmployeeSpecialization_CreatedAt DEFAULT SYSUTCDATETIME()
                    );
                END
                ELSE
                BEGIN
                    IF COL_LENGTH(N'dbo.EmployeeSpecialization', N'SpecializationDescription') IS NULL
                        ALTER TABLE dbo.EmployeeSpecialization ADD SpecializationDescription nvarchar(1000) NULL;

                    IF COL_LENGTH(N'dbo.EmployeeSpecialization', N'Description') IS NULL
                        ALTER TABLE dbo.EmployeeSpecialization ADD Description nvarchar(1000) NULL;

                    IF COL_LENGTH(N'dbo.EmployeeSpecialization', N'ColorTag') IS NULL
                        ALTER TABLE dbo.EmployeeSpecialization ADD ColorTag nvarchar(50) NOT NULL CONSTRAINT DF_EmployeeSpecialization_ColorTag DEFAULT 'Blue';

                    IF COL_LENGTH(N'dbo.EmployeeSpecialization', N'CreatedAt') IS NULL
                        ALTER TABLE dbo.EmployeeSpecialization ADD CreatedAt datetime2 NOT NULL CONSTRAINT DF_EmployeeSpecialization_CreatedAt DEFAULT SYSUTCDATETIME();
                END

                -- 2. ServiceCategory table
                IF OBJECT_ID(N'dbo.ServiceCategory', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.ServiceCategory (
                        ServiceCategoryID nvarchar(30) NOT NULL PRIMARY KEY,
                        ServiceCategoryName nvarchar(255) NOT NULL,
                        ServiceCategoryDescription nvarchar(1000) NOT NULL
                    );
                END

                -- 3. ServiceCategoryJoins table
                IF OBJECT_ID(N'dbo.ServiceCategoryJoins', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.ServiceCategoryJoins (
                        ServiceCategoryJoinID nvarchar(30) NOT NULL PRIMARY KEY,
                        ServiceCategoryID nvarchar(30) NOT NULL,
                        ServicesID nvarchar(30) NULL,
                        AdditionalServicesID nvarchar(30) NULL,
                        ComboPackageID nvarchar(30) NULL,
                        CONSTRAINT FK_ServiceCategoryJoins_ServiceCategory FOREIGN KEY (ServiceCategoryID) REFERENCES dbo.ServiceCategory(ServiceCategoryID) ON DELETE CASCADE
                    );
                    CREATE INDEX IX_ServiceCategoryJoins_ServiceCategoryID ON dbo.ServiceCategoryJoins(ServiceCategoryID);
                END

                -- 4. EmployeeLeaves table
                IF OBJECT_ID(N'dbo.EmployeeLeaves', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.EmployeeLeaves (
                        Id int NOT NULL IDENTITY(1, 1) PRIMARY KEY,
                        LeaveId nvarchar(max) NOT NULL,
                        EmployeeStudioId nvarchar(max) NOT NULL,
                        EmployeeName nvarchar(max) NOT NULL,
                        LeaveType nvarchar(max) NOT NULL,
                        StartDate date NOT NULL,
                        EndDate date NOT NULL,
                        Reason nvarchar(max) NULL,
                        Status nvarchar(max) NOT NULL,
                        ApprovedBy nvarchar(max) NULL,
                        ApprovedAt datetime2 NULL,
                        RejectionReason nvarchar(max) NULL,
                        CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
                    );
                END

                -- 5. EmployeeTimesheets table
                IF OBJECT_ID(N'dbo.EmployeeTimesheets', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.EmployeeTimesheets (
                        Id int NOT NULL IDENTITY(1, 1) PRIMARY KEY,
                        TimesheetId nvarchar(max) NOT NULL,
                        EmployeeStudioId nvarchar(max) NOT NULL,
                        EmployeeName nvarchar(max) NOT NULL,
                        WorkDate date NOT NULL,
                        ClockIn datetime2 NOT NULL,
                        ClockOut datetime2 NULL,
                        TotalHours float NOT NULL,
                        Status nvarchar(max) NOT NULL,
                        IsDayOffWork bit NOT NULL,
                        Notes nvarchar(max) NULL,
                        AdjustedBy nvarchar(max) NULL,
                        AdjustmentReason nvarchar(max) NULL,
                        CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
                    );
                END

                -- 6. Equipment table columns
                IF OBJECT_ID(N'dbo.Equipment', N'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH(N'dbo.Equipment', N'CreatedAt') IS NULL
                        ALTER TABLE dbo.Equipment ADD CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME();
                    IF COL_LENGTH(N'dbo.Equipment', N'Description') IS NULL
                        ALTER TABLE dbo.Equipment ADD Description nvarchar(max) NULL;
                    IF COL_LENGTH(N'dbo.Equipment', N'EquipmentID') IS NULL
                        ALTER TABLE dbo.Equipment ADD EquipmentID nvarchar(max) NOT NULL DEFAULT '';
                    IF COL_LENGTH(N'dbo.Equipment', N'EquipmentTypeID') IS NULL
                        ALTER TABLE dbo.Equipment ADD EquipmentTypeID nvarchar(max) NOT NULL DEFAULT '';
                    IF COL_LENGTH(N'dbo.Equipment', N'ImagePath') IS NULL
                        ALTER TABLE dbo.Equipment ADD ImagePath nvarchar(max) NULL;
                END

                -- 7. Employees / Employee DaysOff column
                IF OBJECT_ID(N'dbo.Employees', N'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH(N'dbo.Employees', N'DaysOff') IS NULL
                        ALTER TABLE dbo.Employees ADD DaysOff nvarchar(max) NOT NULL DEFAULT '';
                END

                IF OBJECT_ID(N'dbo.Employee', N'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH(N'dbo.Employee', N'DaysOff') IS NULL
                        ALTER TABLE dbo.Employee ADD DaysOff nvarchar(max) NOT NULL DEFAULT 'Sunday';
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF OBJECT_ID(N'dbo.EmployeeSpecialization', N'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH(N'dbo.EmployeeSpecialization', N'CreatedAt') IS NOT NULL
                    BEGIN
                        DECLARE @DfCreatedAt nvarchar(128);
                        SELECT @DfCreatedAt = d.name
                        FROM sys.default_constraints d
                        JOIN sys.columns c ON c.default_object_id = d.object_id
                        WHERE d.parent_object_id = OBJECT_ID(N'dbo.EmployeeSpecialization') AND c.name = 'CreatedAt';
                        IF @DfCreatedAt IS NOT NULL
                            EXEC('ALTER TABLE dbo.EmployeeSpecialization DROP CONSTRAINT ' + @DfCreatedAt);
                        ALTER TABLE dbo.EmployeeSpecialization DROP COLUMN CreatedAt;
                    END

                    IF COL_LENGTH(N'dbo.EmployeeSpecialization', N'ColorTag') IS NOT NULL
                    BEGIN
                        DECLARE @DfColorTag nvarchar(128);
                        SELECT @DfColorTag = d.name
                        FROM sys.default_constraints d
                        JOIN sys.columns c ON c.default_object_id = d.object_id
                        WHERE d.parent_object_id = OBJECT_ID(N'dbo.EmployeeSpecialization') AND c.name = 'ColorTag';
                        IF @DfColorTag IS NOT NULL
                            EXEC('ALTER TABLE dbo.EmployeeSpecialization DROP CONSTRAINT ' + @DfColorTag);
                        ALTER TABLE dbo.EmployeeSpecialization DROP COLUMN ColorTag;
                    END

                    IF COL_LENGTH(N'dbo.EmployeeSpecialization', N'Description') IS NOT NULL
                        ALTER TABLE dbo.EmployeeSpecialization DROP COLUMN Description;

                    IF COL_LENGTH(N'dbo.EmployeeSpecialization', N'SpecializationDescription') IS NOT NULL
                        ALTER TABLE dbo.EmployeeSpecialization DROP COLUMN SpecializationDescription;
                END

                IF OBJECT_ID(N'dbo.EmployeeLeaves', N'U') IS NOT NULL
                    DROP TABLE dbo.EmployeeLeaves;

                IF OBJECT_ID(N'dbo.EmployeeTimesheets', N'U') IS NOT NULL
                    DROP TABLE dbo.EmployeeTimesheets;
            ");
        }
    }
}
