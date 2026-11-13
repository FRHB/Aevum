using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using PhotoAevumStudio.Application.Auth;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Application.Validation;
using PhotoAevumStudio.Infrastructure.Auth;
using PhotoAevumStudio.Web.Models;
using PhotoAevumStudio.Web.Services;
using Xunit;
using AppCustomerRegisterRequest = PhotoAevumStudio.Application.DTOs.CustomerRegisterRequest;
using AppRegisterRequest = PhotoAevumStudio.Application.Auth.RegisterRequest;
using AppRoles = PhotoAevumStudio.Application.Auth.AppRoles;

namespace PhotoAevumStudio.Web.Tests;

public class CrossRoleEmailUniquenessTests
{
    private const string ExpectedErrorMessage = "This email address is already registered under another account role. Please use a different email.";

    private class NonUniqueUserService : UserService
    {
        public override bool IsEmailUnique(string email, string? excludeUserId = null) => false;
        public override Task<bool> IsEmailUniqueAsync(string email, string? excludeUserId = null, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    [Fact]
    public void CustomIdentityErrorDescriber_ReturnsCrossRoleRequiredErrorMessage()
    {
        var describer = new CustomIdentityErrorDescriber();
        var error = describer.DuplicateEmail("existing@photoaevum.com");

        Assert.Equal(nameof(describer.DuplicateEmail), error.Code);
        Assert.Equal(ExpectedErrorMessage, error.Description);
    }

    [Fact]
    public void CustomerRegisterRequestValidator_RejectsDuplicateEmailAcrossRoles()
    {
        var mockSqlAuth = new Mock<ISqlAuthService>();
        mockSqlAuth.Setup(x => x.IsEmailInUseAcrossRoles("employee@photoaevum.com", null)).Returns(true);

        var validator = new CustomerRegisterRequestValidator(mockSqlAuth.Object);
        var request = new AppCustomerRegisterRequest(
            FirstName: "Maria",
            LastName: "Santos",
            MiddleName: null,
            Email: "employee@photoaevum.com",
            Password: "Password123@",
            VerificationCode: "123456",
            PhoneNumber: "09123456789"
        );

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(request.Email) && e.ErrorMessage == ExpectedErrorMessage);
    }

    [Fact]
    public void CustomerRegisterRequestValidator_AllowsUniqueEmailAcrossRoles()
    {
        var mockSqlAuth = new Mock<ISqlAuthService>();
        mockSqlAuth.Setup(x => x.IsEmailInUseAcrossRoles("unique.customer@photoaevum.com", null)).Returns(false);

        var validator = new CustomerRegisterRequestValidator(mockSqlAuth.Object);
        var request = new AppCustomerRegisterRequest(
            FirstName: "Maria",
            LastName: "Santos",
            MiddleName: null,
            Email: "unique.customer@photoaevum.com",
            Password: "Password123@",
            VerificationCode: "123456",
            PhoneNumber: "09123456789"
        );

        var result = validator.Validate(request);

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [Fact]
    public void RegisterRequestValidator_RejectsDuplicateEmailAcrossRoles()
    {
        var mockSqlAuth = new Mock<ISqlAuthService>();
        mockSqlAuth.Setup(x => x.IsEmailInUseAcrossRoles("admin@photoaevum.com", null)).Returns(true);

        var validator = new RegisterRequestValidator(mockSqlAuth.Object);
        var request = new AppRegisterRequest(
            FirstName: "Juan",
            LastName: "Dela Cruz",
            MiddleName: null,
            Email: "admin@photoaevum.com",
            Password: "Password123@",
            Role: AppRoles.Customer
        );

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(request.Email) && e.ErrorMessage == ExpectedErrorMessage);
    }

    [Fact]
    public void RegisterRequestValidator_AllowsUniqueEmailAcrossRoles()
    {
        var mockSqlAuth = new Mock<ISqlAuthService>();
        mockSqlAuth.Setup(x => x.IsEmailInUseAcrossRoles("newadmin@photoaevum.com", null)).Returns(false);

        var validator = new RegisterRequestValidator(mockSqlAuth.Object);
        var request = new AppRegisterRequest(
            FirstName: "Juan",
            LastName: "Dela Cruz",
            MiddleName: null,
            Email: "newadmin@photoaevum.com",
            Password: "Password123@",
            Role: AppRoles.Admin
        );

        var result = validator.Validate(request);

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [Fact]
    public void UserService_AddCustomer_Throws_WhenEmailAlreadyInUseAcrossRoles()
    {
        var service = new NonUniqueUserService();
        var customer = new Customer
        {
            Id = "CUST-9999",
            Email = "admin@photoaevum.com",
            Name = "Customer Test",
            Phone = "09123456789"
        };

        var ex = Assert.Throws<InvalidOperationException>(() => service.AddCustomer(customer));
        Assert.Equal(ExpectedErrorMessage, ex.Message);
    }

    [Fact]
    public void UserService_AddEmployee_Throws_WhenEmailAlreadyInUseAcrossRoles()
    {
        var service = new NonUniqueUserService();
        var employee = new Employee
        {
            Id = "EMP-999",
            Email = "customer@photoaevum.com",
            Name = "Employee Test",
            Position = "Photographer",
            Department = "Creative"
        };

        var ex = Assert.Throws<InvalidOperationException>(() => service.AddEmployee(employee));
        Assert.Equal(ExpectedErrorMessage, ex.Message);
    }

    [Fact]
    public void UserService_AddAdmin_Throws_WhenEmailAlreadyInUseAcrossRoles()
    {
        var service = new NonUniqueUserService();
        var admin = new Admin
        {
            Id = "ADM-999",
            Email = "employee@photoaevum.com",
            Name = "Admin Test",
            Role = "Admin"
        };

        var ex = Assert.Throws<InvalidOperationException>(() => service.AddAdmin(admin));
        Assert.Equal(ExpectedErrorMessage, ex.Message);
    }

    [Fact]
    public async Task EmployeeService_CreateEmployeeAsync_RejectsDuplicateEmailAcrossRoles()
    {
        var mockUserService = new Mock<IUserService>();
        mockUserService.Setup(u => u.IsEmailUnique("admin@photoaevum.com", "EMP-001")).Returns(false);

        var mockHttpFactory = new Mock<IHttpClientFactory>();
        mockHttpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());
        var mockTokenStore = new Mock<ITokenStore>();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                {"ConnectionStrings:DefaultConnection", "Server=localhost;Database=PhotoAevumStudio;"}
            })
            .Build();

        var service = new EmployeeService(mockHttpFactory.Object, mockTokenStore.Object, mockUserService.Object, config);
        var req = new NewEmployeeRequest(
            "EMP-001", "Jane", "Doe", null, "admin@photoaevum.com",
            new List<string> { "Photography" }, "Pass12345!", "Sunday", "Manila", 1995, null, null);

        var result = await service.CreateEmployeeAsync(req);

        Assert.False(result.Success);
        Assert.Equal(ExpectedErrorMessage, result.Error);
    }

    [Fact]
    public async Task EmployeeService_UpdateEmployeeAsync_RejectsDuplicateEmailAcrossRoles()
    {
        var mockUserService = new Mock<IUserService>();
        mockUserService.Setup(u => u.IsEmailUnique("customer@photoaevum.com", "EMP-001")).Returns(false);

        var mockHttpFactory = new Mock<IHttpClientFactory>();
        mockHttpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());
        var mockTokenStore = new Mock<ITokenStore>();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                {"ConnectionStrings:DefaultConnection", "Server=localhost;Database=PhotoAevumStudio;"}
            })
            .Build();

        var service = new EmployeeService(mockHttpFactory.Object, mockTokenStore.Object, mockUserService.Object, config);
        var req = new NewEmployeeRequest(
            "EMP-001", "Jane", "Doe", null, "customer@photoaevum.com",
            new List<string> { "Photography" }, "Pass12345!", "Sunday", "Manila", 1995, null, null);

        var result = await service.UpdateEmployeeAsync(req);

        Assert.False(result.Success);
        Assert.Equal(ExpectedErrorMessage, result.Error);
    }

    [Fact]
    public async Task AdminService_CreateAdminAsync_RejectsDuplicateEmailAcrossRoles()
    {
        var mockUserService = new Mock<IUserService>();
        mockUserService.Setup(u => u.IsEmailUnique("customer@photoaevum.com", null)).Returns(false);

        var mockHttpFactory = new Mock<IHttpClientFactory>();
        mockHttpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());
        var mockTokenStore = new Mock<ITokenStore>();
        var mockAuthStateProvider = new Mock<AuthenticationStateProvider>();
        var mockLogger = new Mock<ILogger<AdminService>>();

        var service = new AdminService(mockHttpFactory.Object, mockTokenStore.Object, mockUserService.Object, mockAuthStateProvider.Object, mockLogger.Object);
        var model = new AddAdminWizardModel
        {
            FirstName = "John",
            LastName = "Admin",
            Email = "customer@photoaevum.com",
            Role = "Admin",
            GeneratedId = "ADM-001",
            TemporaryPassword = "Password123!"
        };

        var result = await service.CreateAdminAsync(model);

        Assert.False(result.Success);
        Assert.Equal(ExpectedErrorMessage, result.ErrorMessage);
    }

    [Fact]
    public async Task AdminService_UpdateAdminAsync_RejectsDuplicateEmailAcrossRoles()
    {
        var mockUserService = new Mock<IUserService>();
        mockUserService.Setup(u => u.IsEmailUnique("employee@photoaevum.com", "ADM-001")).Returns(false);

        var mockHttpFactory = new Mock<IHttpClientFactory>();
        mockHttpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());
        var mockTokenStore = new Mock<ITokenStore>();
        var mockAuthStateProvider = new Mock<AuthenticationStateProvider>();
        var mockLogger = new Mock<ILogger<AdminService>>();

        var service = new AdminService(mockHttpFactory.Object, mockTokenStore.Object, mockUserService.Object, mockAuthStateProvider.Object, mockLogger.Object);
        var model = new EditAdminModel
        {
            Id = "ADM-001",
            FirstName = "John",
            LastName = "Admin",
            Email = "employee@photoaevum.com",
            Role = "Admin"
        };

        var result = await service.UpdateAdminAsync("ADM-001", model);

        Assert.False(result.Success);
        Assert.Equal(ExpectedErrorMessage, result.ErrorMessage);
    }

    [Fact]
    public void SourceCodeAudit_VerifyExactErrorMessageInAllTargetFiles()
    {
        var root = GetRepositoryRoot();

        var targetFiles = new[]
        {
            Path.Combine(root, "PhotoAevumStudioBackend", "PhotoAevumStudio.Infrastructure", "Services", "SqlAuthService.cs"),
            Path.Combine(root, "PhotoAevumStudioBackend", "PhotoAevumStudio.Infrastructure", "Services", "AdminManagementService.cs"),
            Path.Combine(root, "PhotoAevumStudioBackend", "PhotoAevumStudio.Infrastructure", "Auth", "CustomIdentityErrorDescriber.cs"),
            Path.Combine(root, "PhotoAevumStudioBackend", "PhotoAevumStudio.Application", "Validation", "RegisterRequestValidator.cs"),
            Path.Combine(root, "PhotoAevumStudioBackend", "PhotoAevumStudio.Application", "Validation", "CustomerRegisterRequestValidator.cs"),
            Path.Combine(root, "PhotoAevumStudioBackend", "database", "PhotoAevumStudioStoredProcedures.sql"),
            Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Services", "UserService.cs"),
            Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Services", "EmployeeService.cs"),
            Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Services", "AdminService.cs"),
            Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Layout", "AuthPortalModal.razor"),
            Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Pages", "AddAdminWizard.razor"),
            Path.Combine(root, "PhotoAevumStudioFrontend", "PhotoAevumStudio.Web", "Components", "Pages", "AddEmployeeWizard.razor")
        };

        foreach (var file in targetFiles)
        {
            Assert.True(File.Exists(file), $"Expected target file to exist: {file}");
            var content = File.ReadAllText(file);
            Assert.Contains(ExpectedErrorMessage, content);
        }
    }

    private static string GetRepositoryRoot()
    {
        var baseDir = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(baseDir);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PhotoAevumStudio.slnx")) && !File.Exists(Path.Combine(dir.FullName, "PhotoAevumStudio.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir.FullName;
    }
}
