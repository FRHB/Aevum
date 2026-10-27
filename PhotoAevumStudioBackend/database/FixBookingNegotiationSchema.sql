-- Migration Script: Reconcile BookingNegotiation, EventAppointment, and Payment schema
SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- 1. Ensure dbo.BookingNegotiation table & columns
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
GO

-- 2. Ensure dbo.EventAppointment table & columns
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
END;
GO

-- 3. Ensure dbo.PaymentTransactions
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
    IF COL_LENGTH(N'dbo.PaymentTransactions', N'CreatedAt') IS NULL
        ALTER TABLE dbo.PaymentTransactions ADD CreatedAt datetime2 NOT NULL CONSTRAINT DF_PaymentTransactions_CreatedAt DEFAULT SYSUTCDATETIME();
END;
GO

-- 4. Ensure dbo.Services has RateType
IF OBJECT_ID(N'dbo.Services', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.Services', N'RateType') IS NULL
        ALTER TABLE dbo.Services ADD RateType nvarchar(50) NOT NULL CONSTRAINT DF_Services_RateType DEFAULT 'Per Hour';
END;
GO

-- 5. Ensure dbo.RateType and initial seed values
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
END;
GO

-- 6. Ensure dbo.ServiceRate
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
GO

-- 7. Ensure sequences
IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = 'Seq_RateTypeID')
BEGIN
    CREATE SEQUENCE dbo.Seq_RateTypeID AS INT START WITH 5 INCREMENT BY 1;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = 'Seq_ServiceRateID')
BEGIN
    CREATE SEQUENCE dbo.Seq_ServiceRateID AS INT START WITH 1 INCREMENT BY 1;
END;
GO
