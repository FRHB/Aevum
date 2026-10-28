-- Migration Script: Reconcile PortfolioCategory, PortfolioCategoryService, and Portfolios schema
SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- 1. Ensure foreign key on PortfolioCategoryService is dropped before rename
IF OBJECT_ID(N'dbo.FK_PortfolioCategoryService_Category', N'F') IS NOT NULL
    ALTER TABLE dbo.PortfolioCategoryService DROP CONSTRAINT FK_PortfolioCategoryService_Category;
GO

-- 2. Rename CategoryCode to CategoryID if not already renamed
IF COL_LENGTH(N'dbo.PortfolioCategory', N'CategoryCode') IS NOT NULL AND COL_LENGTH(N'dbo.PortfolioCategory', N'CategoryID') IS NULL
    EXEC sp_rename 'dbo.PortfolioCategory.CategoryCode', 'CategoryID', 'COLUMN';
GO

IF COL_LENGTH(N'dbo.PortfolioCategoryService', N'CategoryCode') IS NOT NULL AND COL_LENGTH(N'dbo.PortfolioCategoryService', N'CategoryID') IS NULL
    EXEC sp_rename 'dbo.PortfolioCategoryService.CategoryCode', 'CategoryID', 'COLUMN';
GO

IF COL_LENGTH(N'dbo.Portfolios', N'CategoryCode') IS NOT NULL AND COL_LENGTH(N'dbo.Portfolios', N'CategoryID') IS NULL
    EXEC sp_rename 'dbo.Portfolios.CategoryCode', 'CategoryID', 'COLUMN';
GO

-- 3. Align column lengths to nvarchar(50)
ALTER TABLE dbo.PortfolioCategory ALTER COLUMN CategoryID nvarchar(50) NOT NULL;
ALTER TABLE dbo.PortfolioCategoryService ALTER COLUMN CategoryID nvarchar(50) NOT NULL;
ALTER TABLE dbo.Portfolios ALTER COLUMN CategoryID nvarchar(50) NOT NULL;
GO

-- 4. Recreate Foreign Keys
IF OBJECT_ID(N'dbo.FK_PortfolioCategoryService_Category', N'F') IS NULL
    ALTER TABLE dbo.PortfolioCategoryService ADD CONSTRAINT FK_PortfolioCategoryService_Category
        FOREIGN KEY (CategoryID) REFERENCES dbo.PortfolioCategory(CategoryID) ON DELETE CASCADE;
GO

IF OBJECT_ID(N'dbo.FK_Portfolios_PortfolioCategory', N'F') IS NULL
    ALTER TABLE dbo.Portfolios ADD CONSTRAINT FK_Portfolios_PortfolioCategory
        FOREIGN KEY (CategoryID) REFERENCES dbo.PortfolioCategory(CategoryID);
GO

-- 5. Stored Procedure for Portfolio Category Sequential IDs
CREATE OR ALTER PROCEDURE dbo.sp_GetNextPortfolioCategoryID
    @NextCategoryID nvarchar(50) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @MaxNum INT = 0;
    SELECT @MaxNum = ISNULL(MAX(TRY_CAST(SUBSTRING(CategoryID, CHARINDEX('-', CategoryID) + 1, 20) AS INT)), 0)
    FROM dbo.PortfolioCategory WITH (UPDLOCK, HOLDLOCK)
    WHERE CategoryID LIKE 'PCAT-%';

    SET @NextCategoryID = 'PCAT-' + RIGHT('0000' + CAST((@MaxNum + 1) AS VARCHAR(10)), 4);
    SELECT @NextCategoryID AS NextCategoryID;
END;
GO

-- 6. Stored Procedure for Portfolio Showcase IDs
CREATE OR ALTER PROCEDURE dbo.sp_GetNextPortfolioID
    @NextPortfolioID nvarchar(50) = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @MaxNum INT = 0;
    SELECT @MaxNum = ISNULL(MAX(TRY_CAST(SUBSTRING(PortfolioId, CHARINDEX('-', PortfolioId) + 1, 20) AS INT)), 0)
    FROM dbo.Portfolios WITH (UPDLOCK, HOLDLOCK)
    WHERE PortfolioId LIKE 'PRF-%';

    SET @NextPortfolioID = 'PRF-' + RIGHT('0000' + CAST((@MaxNum + 1) AS VARCHAR(10)), 4);
    SELECT @NextPortfolioID AS NextPortfolioID;
END;
GO
