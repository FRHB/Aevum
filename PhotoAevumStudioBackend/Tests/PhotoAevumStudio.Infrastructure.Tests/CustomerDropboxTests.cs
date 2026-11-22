using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PhotoAevumStudio.Application;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Infrastructure.Data;
using PhotoAevumStudio.Infrastructure.Services;
using Xunit;

namespace PhotoAevumStudio.Infrastructure.Tests;

public class CustomerDropboxTests
{
    private static ApplicationDbContext CreateDummyDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=localhost;Database=Dummy;Trusted_Connection=True;")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static string GetRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PhotoAevumStudio.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void CountdownHelper_ComputesExpected30DayCountdownLabels()
    {
        var now = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

        // Immediately after completion (29d 23h remaining) -> "29 Days Left"
        var justCompletedExpires = now.AddDays(30).AddMinutes(-1);
        Assert.Equal(29, DropboxCountdownHelper.ComputeDaysRemaining(justCompletedExpires, now));
        Assert.Equal("29 Days Left", DropboxCountdownHelper.FormatCountdownLabel(justCompletedExpires, now));

        // 1 day left
        var oneDayExpires = now.AddDays(1).AddHours(3);
        Assert.Equal(1, DropboxCountdownHelper.ComputeDaysRemaining(oneDayExpires, now));
        Assert.Equal("1 Day Left", DropboxCountdownHelper.FormatCountdownLabel(oneDayExpires, now));

        // Expired
        var expired = now.AddMinutes(-5);
        Assert.Equal(0, DropboxCountdownHelper.ComputeDaysRemaining(expired, now));
        Assert.Equal("0 Days Left", DropboxCountdownHelper.FormatCountdownLabel(expired, now));
    }

    [Fact]
    public void R2StorageService_ConfiguresDropboxBucket_AndGeneratesAttachmentPresignedUrl()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CloudflareR2:AccessKey"] = "test-access-key",
                ["CloudflareR2:SecretKey"] = "test-secret-key",
                ["CloudflareR2:AccountId"] = "testaccount123",
                ["CloudflareR2:Buckets:Dropbox"] = "aevum-dropbox-test"
            })
            .Build();

        var service = new R2StorageService(config, NullLogger<R2StorageService>.Instance);

        Assert.True(service.IsConfigured);
        Assert.Equal("aevum-dropbox-test", service.DropboxBucket);

        var url = service.GetPresignedDownloadUrl(
            service.DropboxBucket,
            "customers/CUST-0001/events/EVT-0001/archive.zip",
            "wedding-photos.zip",
            TimeSpan.FromMinutes(15));

        Assert.False(string.IsNullOrWhiteSpace(url));
        Assert.Contains("aevum-dropbox-test", url);
        Assert.Contains("wedding-photos.zip", url);
        Assert.Contains("X-Amz-Signature", url);
    }

    [Fact]
    public void SignedDownloadToken_ValidatesOwnershipAndRejectsTampering()
    {
        using var db = CreateDummyDbContext();
        var r2 = new Mock<IR2StorageService>();
        r2.SetupGet(x => x.DropboxBucket).Returns("dropbox");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dropbox:SigningKey"] = "UnitTest-Super-Secret-Key-With-Enough-Bytes-12345"
            })
            .Build();

        var svc = new CustomerDropboxService(db, r2.Object, config, NullLogger<CustomerDropboxService>.Instance);

        var token = svc.CreateSignedDownloadToken("DRP-0001", "CUST-0001", TimeSpan.FromMinutes(10));
        var (valid, dropboxId, customerId) = svc.ValidateSignedDownloadToken(token);

        Assert.True(valid);
        Assert.Equal("DRP-0001", dropboxId);
        Assert.Equal("CUST-0001", customerId);

        // Tamper token
        var tampered = token[..^2] + (token.EndsWith("AA") ? "BB" : "AA");
        var (tamperedValid, _, _) = svc.ValidateSignedDownloadToken(tampered);
        Assert.False(tamperedValid);

        // Expired token
        var expiredToken = svc.CreateSignedDownloadToken("DRP-0001", "CUST-0001", TimeSpan.FromSeconds(-5));
        var (expiredValid, _, _) = svc.ValidateSignedDownloadToken(expiredToken);
        Assert.False(expiredValid);
    }

    [Fact]
    public async Task UploadZipAsync_RejectsNonZipFilesAndInvalidZipSignatures()
    {
        using var db = CreateDummyDbContext();
        var r2 = new Mock<IR2StorageService>();
        r2.SetupGet(x => x.DropboxBucket).Returns("dropbox");
        var config = new ConfigurationBuilder().Build();
        var svc = new CustomerDropboxService(db, r2.Object, config, NullLogger<CustomerDropboxService>.Instance);

        using var fakeStream = new MemoryStream(Encoding.UTF8.GetBytes("not a zip"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UploadZipAsync("EVT-0001", fakeStream, "photos.rar", fakeStream.Length, "application/octet-stream", "ADM-001"));

        using var invalidSignatureZip = new MemoryStream(Encoding.UTF8.GetBytes("NOTPK_FAKE_ZIP"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UploadZipAsync("EVT-0001", invalidSignatureZip, "photos.zip", invalidSignatureZip.Length, "application/zip", "ADM-001"));

        r2.Verify(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void CanonicalSqlFiles_DefineCustomerDropboxSchemaAndProcedures()
    {
        var root = GetRepoRoot();
        var dbDir = Path.Combine(root, "PhotoAevumStudioBackend", "database");

        var masterSql = File.ReadAllText(Path.Combine(dbDir, "PhotoAevumStudio.sql"));
        var idempotentSql = File.ReadAllText(Path.Combine(dbDir, "PhotoAevumStudioIdempotent.sql"));
        var procsSql = File.ReadAllText(Path.Combine(dbDir, "PhotoAevumStudioStoredProcedures.sql"));

        Assert.Contains("CREATE TABLE dbo.CustomerDropbox", masterSql);
        Assert.Contains("Seq_DropboxID", masterSql);
        Assert.Contains("FK_CustomerDropbox_EventAppointment", masterSql);
        Assert.Contains("FK_CustomerDropbox_Customer", masterSql);

        Assert.Contains("CREATE TABLE dbo.CustomerDropbox", idempotentSql);
        Assert.Contains("Seq_DropboxID", idempotentSql);
        Assert.Contains("FK_CustomerDropbox_EventAppointment", idempotentSql);
        Assert.Contains("FK_CustomerDropbox_Customer", idempotentSql);

        Assert.Contains("dbo.sp_GetNextDropboxID", procsSql);
        Assert.Contains("dbo.usp_SyncCustomerDropboxFolders", procsSql);
    }

    [Fact]
    public void CustomerDropboxService_HistoryLogInserts_AvoidComputedChangedByAndInvalidCheckValues()
    {
        var root = GetRepoRoot();
        var servicePath = Path.Combine(root, "PhotoAevumStudioBackend", "PhotoAevumStudio.Infrastructure", "Services", "CustomerDropboxService.cs");
        var source = File.ReadAllText(servicePath);

        Assert.DoesNotContain("'UPLOAD'", source);
        Assert.DoesNotContain("'AUTO_DELETE'", source);
        Assert.DoesNotContain("'Staff'", source);
        Assert.DoesNotContain("ChangedBy,", source);
        Assert.Contains("'IMAGE_UPLOAD'", source);
        Assert.Contains("'DELETE'", source);
        Assert.Contains("ChangedByEmployeeID, ChangedByAdminID", source);
    }

    [Fact]
    public async Task R2StorageService_UploadFileAsync_ThrowsClearMessageWhenUnconfigured()
    {
        var config = new ConfigurationBuilder().Build();
        var service = new R2StorageService(config, NullLogger<R2StorageService>.Instance);

        Assert.False(service.IsConfigured);

        using var ms = new MemoryStream(new byte[] { 0x50, 0x4B, 0x03, 0x04 });
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UploadFileAsync("dropbox", "customers/C1/events/E1/test.zip", ms, "application/zip"));

        Assert.Contains("not configured", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
