using System;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PhotoAevumStudio.Api.Controllers;
using PhotoAevumStudio.Application;
using PhotoAevumStudio.Application.Auth;
using PhotoAevumStudio.Application.Interfaces;
using PhotoAevumStudio.Application.Models;
using Xunit;

namespace PhotoAevumStudio.Web.Tests;

public class PaymentsContractGateTests
{
    [Fact]
    public async Task PaymentsController_GetQrPayment_GatesQrStringUntilContractAccepted()
    {
        var pendingMock = new Mock<IPendingPaymentService>();
        pendingMock.Setup(p => p.GetQrPaymentAsync("EVT-1001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QrPaymentInfoDto
            {
                AppointmentId = "EVT-1001",
                CustomerId = "CUST-100",
                EventName = "Portrait Shoot",
                EventDate = new DateTime(2026, 12, 1),
                EventTime = new TimeSpan(10, 0, 0),
                EventStatus = "Pending Payment",
                Amount = 1500m,
                QrString = "000201010212MOCKQR",
                ContractAccepted = false
            });

        var controller = new PaymentsController(
            Mock.Of<IConsultationNegotiationService>(),
            Mock.Of<IPaymentService>(),
            Options.Create(new XenditSettings()),
            NullLogger<PaymentsController>.Instance,
            pendingMock.Object,
            Mock.Of<IPaymentNotifier>(),
            new ConfigurationBuilder().Build(),
            Mock.Of<IHttpClientFactory>());

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.Role, AppRoles.Customer),
                    new Claim("studio_id", "CUST-100")
                }, "TestAuth"))
            }
        };

        var unacceptedResult = await controller.GetQrPayment("EVT-1001", CancellationToken.None);
        var okUnaccepted = Assert.IsType<OkObjectResult>(unacceptedResult);
        var dtoUnaccepted = Assert.IsType<QrPaymentInfoDto>(okUnaccepted.Value);
        Assert.False(dtoUnaccepted.ContractAccepted);
        Assert.Null(dtoUnaccepted.QrString);

        pendingMock.Setup(p => p.GetQrPaymentAsync("EVT-1001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QrPaymentInfoDto
            {
                AppointmentId = "EVT-1001",
                CustomerId = "CUST-100",
                EventName = "Portrait Shoot",
                EventDate = new DateTime(2026, 12, 1),
                EventTime = new TimeSpan(10, 0, 0),
                EventStatus = "Pending Payment",
                Amount = 1500m,
                QrString = "000201010212MOCKQR",
                ContractAccepted = true
            });

        var acceptedResult = await controller.GetQrPayment("EVT-1001", CancellationToken.None);
        var okAccepted = Assert.IsType<OkObjectResult>(acceptedResult);
        var dtoAccepted = Assert.IsType<QrPaymentInfoDto>(okAccepted.Value);
        Assert.True(dtoAccepted.ContractAccepted);
        Assert.Equal("000201010212MOCKQR", dtoAccepted.QrString);
    }
}
