using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using PhotoAevumStudio.Application;
using PhotoAevumStudio.Application.Interfaces;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PhotoAevumStudio.Infrastructure.Services;

public class DocumentPdfService : IDocumentPdfService
{
    private const string NavyDark = "#0F172A";
    private const string TextBody = "#1E293B";
    private const string TextMuted = "#475569";
    private const string TextLabel = "#64748B";
    private const string BorderLight = "#CBD5E1";
    private const string BgSubtle = "#F8FAFC";
    private const string BgCallout = "#F1F5F9";

    private readonly StudioProfileSettings _studioDefaults;
    private readonly byte[]? _logoBytes;

    static DocumentPdfService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.ThrowOnMissingFontFamilies = false;
    }

    public DocumentPdfService(
        IOptions<StudioProfileSettings>? studioOptions = null,
        IWebHostEnvironment? env = null)
    {
        _studioDefaults = studioOptions?.Value ?? new StudioProfileSettings();
        _logoBytes = ResolveLogoBytes(env);
    }

    private static byte[]? ResolveLogoBytes(IWebHostEnvironment? env)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(env?.WebRootPath))
        {
            candidates.Add(Path.Combine(env.WebRootPath, "images", "AevumLogo_2.jpg"));
            candidates.Add(Path.Combine(env.WebRootPath, "images", "AevumLogo.jpg"));
        }
        if (!string.IsNullOrWhiteSpace(env?.ContentRootPath))
        {
            candidates.Add(Path.Combine(env.ContentRootPath, "wwwroot", "images", "AevumLogo_2.jpg"));
            candidates.Add(Path.Combine(env.ContentRootPath, "wwwroot", "images", "AevumLogo.jpg"));
        }
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "wwwroot", "images", "AevumLogo_2.jpg"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "Assets", "AevumLogo_2.jpg"));

        foreach (var path in candidates)
        {
            try
            {
                if (File.Exists(path))
                    return File.ReadAllBytes(path);
            }
            catch
            {
                // try next candidate
            }
        }

        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var resName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("AevumLogo_2.jpg", StringComparison.OrdinalIgnoreCase));
            if (resName != null)
            {
                using var s = asm.GetManifestResourceStream(resName);
                if (s != null)
                {
                    using var ms = new MemoryStream();
                    s.CopyTo(ms);
                    return ms.ToArray();
                }
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static string Money(decimal amount) => $"PHP {amount:N2}";
    private static string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);

    private static string FormatTimeSpan(TimeSpan ts) =>
        DateTime.Today.Add(ts).ToString("h:mm tt");

    private static string FormatTimeOnly(TimeOnly t) =>
        t.ToString("h:mm tt");

    // ══════════════════════════════════════════════════════════════════════════
    // 1. PHOTO STUDIO SERVICES AGREEMENT (PDF)
    // ══════════════════════════════════════════════════════════════════════════

    public byte[] GenerateServicesAgreementPdf(BookingContractDto c, TermsVersionDto terms)
    {
        var studioName = string.IsNullOrWhiteSpace(c.StudioName) ? _studioDefaults.StudioName : c.StudioName;
        var studioAddress = string.IsNullOrWhiteSpace(c.StudioAddress) ? _studioDefaults.StudioAddress : c.StudioAddress;
        var studioPhone = string.IsNullOrWhiteSpace(c.StudioPhone) ? _studioDefaults.StudioPhone : c.StudioPhone;
        var studioEmail = string.IsNullOrWhiteSpace(c.StudioEmail) ? _studioDefaults.StudioEmail : c.StudioEmail;
        var repName = string.IsNullOrWhiteSpace(c.RepresentativeName) ? _studioDefaults.RepresentativeName : c.RepresentativeName;
        var agreementDate = c.CreatedAtUtc.ToString("MMMM dd, yyyy");
        var shootDate = c.DateOfShoot.ToString("MMMM dd, yyyy");
        var timeRange = $"{FormatTimeOnly(c.StartTime)} to {FormatTimeOnly(c.EndTime)}";
        var deliveryDays = c.DeliveryBusinessDays > 0 ? c.DeliveryBusinessDays : terms.DeliveryBusinessDays;
        var acceptedBy = c.AcceptedAtUtc.HasValue ? c.ClientName : "Pending Electronic Acceptance";
        var acceptedEmail = c.AcceptedAtUtc.HasValue ? c.ClientEmail : c.ClientEmail;
        var acceptedTs = c.AcceptedAtUtc.HasValue ? $"{c.AcceptedAtUtc.Value:yyyy-MM-dd HH:mm:ss} UTC" : "Pending";
        var acceptedIpRef = c.AcceptedAtUtc.HasValue
            ? $"{(string.IsNullOrWhiteSpace(c.ClientIpAddress) ? "Recorded" : c.ClientIpAddress)} / {(string.IsNullOrWhiteSpace(c.ClickwrapReference) ? c.ContractNumber : c.ClickwrapReference)}"
            : "Pending Checkout Acceptance";

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.MarginTop(36);
                page.MarginBottom(36);
                page.MarginHorizontal(42);
                page.DefaultTextStyle(x => x.FontFamily("Lato").FontSize(9.5f).FontColor(TextBody).LineHeight(1.35f));

                page.Header().Column(h =>
                {
                    RenderMasthead(h, "PHOTO STUDIO SERVICES AGREEMENT", "Booking Contract & Two-Stage Payment Schedule");
                });

                page.Content().PaddingTop(12).Column(col =>
                {
                    col.Spacing(10);

                    // 3-column metadata box
                    col.Item().Border(1).BorderColor(BorderLight).Background(BgSubtle).Table(t =>
                    {
                        t.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(1);
                            cols.RelativeColumn(1);
                            cols.RelativeColumn(1);
                        });

                        MetaCell(t, "AGREEMENT DATE", agreementDate, rightBorder: true);
                        MetaCell(t, "BOOKING REFERENCE", c.EventAppointmentId, rightBorder: true);
                        MetaCell(t, "TERMS VERSION", $"{c.TermsVersionCode}", rightBorder: false);
                    });

                    // Intro paragraph
                    col.Item().Text(txt =>
                    {
                        txt.Span("This Photo Studio Services Agreement (the \"Agreement\") is entered into on ");
                        txt.Span(agreementDate).SemiBold();
                        txt.Span(" by and between the Photo Studio and the Client identified below. By completing the online reservation and accepting the linked ");
                        txt.Span("Standard Terms and Conditions").Italic();
                        txt.Span(", both parties agree to the full scope of services and two-stage payment schedule set forth herein.");
                    });

                    // 1. Contracting Parties
                    col.Item().Element(SectionHeading("1. CONTRACTING PARTIES"));
                    col.Item().Border(1).BorderColor(BorderLight).Table(t =>
                    {
                        t.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(1);
                            cols.RelativeColumn(1);
                        });

                        t.Header(hdr =>
                        {
                            DarkHeaderCell(hdr, "Photo Studio");
                            DarkHeaderCell(hdr, "Client");
                        });

                        t.Cell().BorderRight(1).BorderColor(BorderLight).Padding(8).Column(p =>
                        {
                            p.Spacing(2);
                            KeyValLine(p, "Studio Name:", studioName);
                            KeyValLine(p, "Address:", studioAddress);
                            KeyValLine(p, "Contact:", studioPhone);
                            KeyValLine(p, "Email:", studioEmail);
                        });

                        t.Cell().Padding(8).Column(p =>
                        {
                            p.Spacing(2);
                            KeyValLine(p, "Client Name:", c.ClientName);
                            KeyValLine(p, "Address:", string.IsNullOrWhiteSpace(c.ClientAddress) ? "On File with Studio" : c.ClientAddress);
                            KeyValLine(p, "Contact:", string.IsNullOrWhiteSpace(c.ClientPhone) ? "On File" : c.ClientPhone);
                            KeyValLine(p, "Email:", c.ClientEmail);
                        });
                    });

                    // 2. Scope of Services & Session Schedule
                    col.Item().Element(SectionHeading("2. SCOPE OF SERVICES & SESSION SCHEDULE"));
                    col.Item().Border(1).BorderColor(BorderLight).Table(t =>
                    {
                        t.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(1.2f);
                            cols.RelativeColumn(2.4f);
                        });

                        t.Header(hdr =>
                        {
                            DarkHeaderCell(hdr, "Booking Item");
                            DarkHeaderCell(hdr, "Confirmed Specification");
                        });

                        TwoColRow(t, "Session / Event Type", c.SessionType, shaded: false);
                        TwoColRow(t, "Date of Shoot", shootDate, shaded: true);
                        TwoColRow(t, "Scheduled Time", timeRange, shaded: false);
                        TwoColRow(t, "Location / Venue", c.Venue, shaded: true);
                        TwoColRow(t, "Selected Package", c.PackageName, shaded: false);
                        TwoColRow(t, "Package Inclusions", c.PackageInclusions, shaded: true);
                    });

                    // 3. Two-Stage Payment & Settlement Terms
                    col.Item().Element(SectionHeading("3. TWO-STAGE PAYMENT & SETTLEMENT TERMS"));
                    col.Item().Border(1).BorderColor(BorderLight).Table(t =>
                    {
                        t.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(1.4f);
                            cols.RelativeColumn(1.1f);
                            cols.RelativeColumn(2.1f);
                        });

                        t.Header(hdr =>
                        {
                            DarkHeaderCell(hdr, "Payment Stage");
                            DarkHeaderCell(hdr, "Amount");
                            DarkHeaderCell(hdr, "Due Date & Condition");
                        });

                        ThreeColRow(t, "Total Package Fee", Money(c.TotalPackageAmount), "Full contract value agreed upon at booking", boldAmount: true, shaded: false);
                        ThreeColRow(t, "Stage 1: Reservation Fee", Money(c.ReservationFeeAmount), "Due immediately upon online booking to lock date", boldAmount: true, shaded: true);
                        ThreeColRow(t, "Stage 2: Remaining Balance", Money(c.RemainingBalanceAmount), "Due immediately upon event / session completion", boldAmount: true, shaded: false);
                    });

                    col.Item().PaddingLeft(10).Column(bullets =>
                    {
                        bullets.Spacing(4);
                        BulletPoint(bullets, "Booking Confirmation & Reservation Fee:",
                            "A booking is confirmed only upon receipt of the Stage 1 Reservation Fee and electronic or written acceptance of this Agreement. The Reservation Fee is non-refundable, secures the Studio's exclusive date allocation, and is deducted from the Total Package Fee.");
                        BulletPoint(bullets, "End-of-Event Final Settlement:",
                            "The Stage 2 Remaining Balance, together with any approved on-site overtime or package add-ons requested during the session, becomes due and payable immediately upon wrap-up of the event or photo session.");
                        BulletPoint(bullets, "Deliverable Release Gate:",
                            "The Studio shall not begin final export or release any edited photographs, raw previews, or deliverables until the Stage 2 Remaining Balance and any accrued overtime charges are settled in full (0.00 balance due) and a Final Consolidated Receipt is issued.");
                    });

                    // 4. Incorporation of Standard Terms & Conditions
                    col.Item().Element(SectionHeading("4. INCORPORATION OF STANDARD TERMS & CONDITIONS"));
                    col.Item().Text(txt =>
                    {
                        txt.Span("This Agreement incorporates by reference the Studio's ");
                        txt.Span("Standard Terms and Conditions").Italic();
                        txt.Span($" ({c.TermsVersionCode}), governing cancellation and refund tiers, rescheduling, late arrivals, image delivery timelines ({deliveryDays} business days after full settlement), intellectual property, force majeure, and limitation of liability. This Agreement and the incorporated Terms represent the complete understanding between the parties.");
                    });

                    // 5. Electronic Acceptance & Signatures
                    col.Item().ShowEntire().Column(sig =>
                    {
                        sig.Spacing(8);
                        sig.Item().Element(SectionHeading("5. ELECTRONIC ACCEPTANCE & SIGNATURES"));

                        sig.Item().BorderLeft(3).BorderColor(NavyDark).Background(BgSubtle).Padding(10).Column(box =>
                        {
                            box.Spacing(2);
                            box.Item().Text("Online Clickwrap Record (Populated Automatically Upon Checkout):").Bold().FontColor(NavyDark);
                            box.Item().Text($"Accepted By: {acceptedBy}  |  Account / Email: {acceptedEmail}");
                            box.Item().Text($"Timestamp (UTC): {acceptedTs}  |  IP / Reference: {acceptedIpRef}");
                        });

                        sig.Item().Border(1).BorderColor(BorderLight).Table(t =>
                        {
                            t.ColumnsDefinition(cols =>
                            {
                                cols.RelativeColumn(1);
                                cols.RelativeColumn(1);
                            });

                            t.Header(hdr =>
                            {
                                DarkHeaderCell(hdr, "Client Acknowledgement");
                                DarkHeaderCell(hdr, "Photo Studio Representative");
                            });

                            t.Cell().BorderRight(1).BorderColor(BorderLight).Padding(10).Column(sc =>
                            {
                                sc.Item().PaddingTop(16).LineHorizontal(0.8f).LineColor(TextMuted);
                                sc.Item().PaddingTop(4).Text("Client Signature & Date (or Electronic Acceptance)").FontSize(8.5f).FontColor(TextMuted);
                                sc.Item().PaddingTop(2).Text(txt =>
                                {
                                    txt.Span("Printed Name: ").Bold();
                                    txt.Span(c.ClientName);
                                });
                            });

                            t.Cell().Padding(10).Column(sr =>
                            {
                                sr.Item().PaddingTop(16).LineHorizontal(0.8f).LineColor(TextMuted);
                                sr.Item().PaddingTop(4).Text("Authorized Studio Representative & Date").FontSize(8.5f).FontColor(TextMuted);
                                sr.Item().PaddingTop(2).Text(txt =>
                                {
                                    txt.Span("Printed Name: ").Bold();
                                    txt.Span(repName);
                                });
                            });
                        });
                    });
                });

                page.Footer().PaddingTop(8).BorderTop(0.5f).BorderColor(BorderLight).PaddingTop(4).Row(r =>
                {
                    r.RelativeItem().Text($"{studioName} — Photo Studio Services Agreement").FontSize(8).FontColor(TextLabel);
                    r.RelativeItem().AlignRight().Text(txt =>
                    {
                        txt.DefaultTextStyle(s => s.FontSize(8).FontColor(TextLabel));
                        txt.Span("Page ");
                        txt.CurrentPageNumber();
                        txt.Span(" of ");
                        txt.TotalPages();
                    });
                });
            });
        }).GeneratePdf();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 2. STANDARD TERMS AND CONDITIONS (PDF)
    // ══════════════════════════════════════════════════════════════════════════

    public byte[] GenerateStandardTermsPdf(TermsVersionDto terms, StudioProfileSettings? studio = null)
    {
        var s = studio ?? _studioDefaults;
        var effectiveDateStr = terms.EffectiveDate.ToString("MMMM dd, yyyy");
        var deliveryDays = terms.DeliveryBusinessDays > 0 ? terms.DeliveryBusinessDays : s.DefaultDeliveryBusinessDays;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.MarginTop(36);
                page.MarginBottom(36);
                page.MarginHorizontal(42);
                page.DefaultTextStyle(x => x.FontFamily("Lato").FontSize(9.5f).FontColor(TextBody).LineHeight(1.38f));

                page.Header().Column(h =>
                {
                    RenderMasthead(
                        h,
                        "STANDARD TERMS AND CONDITIONS",
                        $"{s.StudioName} • Policy Document Version: {terms.VersionCode} • Effective Date: {effectiveDateStr}");
                });

                page.Content().PaddingTop(12).Column(col =>
                {
                    col.Spacing(9);

                    col.Item().Text(txt =>
                    {
                        txt.Span("These Standard Terms and Conditions govern all photography and event coverage bookings entered into with ");
                        txt.Span(s.StudioName).Bold();
                        txt.Span(" (\"Studio\") and form an integral part of the Client's executed ");
                        txt.Span("Photo Studio Services Agreement").Italic();
                        txt.Span(".");
                    });

                    col.Item().Element(SectionHeading("1. BOOKING & RESERVATION FEE POLICY"));
                    col.Item().Text(
                        "A booking is secured exclusively upon receipt of the required Reservation Fee and acceptance of the Services Agreement. The Reservation Fee covers date-blocking, administrative preparation, and pre-production, and is credited toward the Total Package Fee upon final settlement.");

                    col.Item().Element(SectionHeading("2. POST-EVENT BALANCE & ON-SITE OVERTIME"));
                    col.Item().PaddingLeft(10).Column(b =>
                    {
                        b.Spacing(4);
                        BulletPoint(b, "End-of-Event Settlement:",
                            "The remaining balance of the Total Package Fee is due immediately upon completion of the event or photo session. Online payment links, QR settlement, or recorded studio payment methods are accepted at wrap-up.");
                        BulletPoint(b, "Overtime & Additional Deliverables:",
                            "Any session extensions, overtime hours, or package upgrades requested by the Client during the event will be billed at the Studio's published rate and added to the Final Balance Statement due at wrap-up.");
                        BulletPoint(b, "Late Payment & Hold on Deliverables:",
                            "No edited images, gallery links, or physical deliverables will be released until the account balance is settled in full.");
                    });

                    col.Item().Element(SectionHeading("3. CANCELLATION AND REFUND POLICY"));
                    col.Item().Border(1).BorderColor(BorderLight).Table(t =>
                    {
                        t.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(1.25f);
                            cols.RelativeColumn(2.45f);
                        });

                        t.Header(hdr =>
                        {
                            DarkHeaderCell(hdr, "Cancellation Scenario");
                            DarkHeaderCell(hdr, "Treatment of Reservation Fee & Payments");
                        });

                        TwoColRow(t, "More than 30 days before shoot",
                            "Reservation Fee is non-refundable but may be transferred once to a future booking within six (6) months, subject to Studio availability.", shaded: false);
                        TwoColRow(t, "15 to 30 days before shoot",
                            "Fifty percent (50%) of payments made are forfeited; any remaining balance paid above the Reservation Fee is refunded or credited.", shaded: true);
                        TwoColRow(t, "Less than 15 days before shoot",
                            "All payments made, including the Reservation Fee, are strictly non-refundable.", shaded: false);
                        TwoColRow(t, "Client No-Show",
                            "Failure of the Client to appear at the scheduled session without prior notice results in immediate forfeiture of all payments made.", shaded: true);
                        TwoColRow(t, "Studio Cancellation",
                            "If the Studio is unable to perform services due to emergency or unforeseen circumstances, all payments received shall be refunded in full.", shaded: false);
                    });

                    col.Item().Element(SectionHeading("4. RESCHEDULING POLICY"));
                    col.Item().Text(
                        "Requests for rescheduling must be submitted in writing at least seven (7) days prior to the scheduled session and are subject to Studio availability. Only one (1) rescheduling request is permitted per booking without penalty, provided the new date falls within six (6) months of the original booking date.");

                    col.Item().Element(SectionHeading("5. LATE ARRIVAL POLICY"));
                    col.Item().Text(
                        "Client delays reduce the available shooting time within the reserved schedule. The Studio is not responsible for compromised coverage or fewer images resulting from shortened schedules. Delays exceeding thirty (30) minutes may incur additional hourly charges or result in session cancellation without refund.");

                    col.Item().Element(SectionHeading("6. IMAGE DELIVERY & RAW FILES"));
                    col.Item().Text(
                        $"Edited and finalized photographs will be delivered within {deliveryDays} business days following both completion of the session and full payment of the remaining balance. Delivery is made through the Studio's designated digital gallery or agreed delivery method. RAW and unedited files remain the exclusive property of the Studio and will not be provided.");

                    col.Item().Element(SectionHeading("7. COPYRIGHT AND USAGE LICENSE"));
                    col.Item().Text(
                        "All photographs remain the intellectual property and copyright of the Studio. Upon full payment, the Client receives a personal-use license permitting them to print, share, and display delivered photographs for non-commercial purposes. Commercial use, third-party licensing, or resale is strictly prohibited without prior written consent from the Studio.");

                    col.Item().Element(SectionHeading("8. FORCE MAJEURE"));
                    col.Item().Text(
                        "Neither party shall be liable for failure or delay in performing obligations under the Agreement if such failure or delay is caused by acts of God, natural disasters, typhoons, government restrictions, pandemics, strikes, or other events strictly beyond reasonable control. In such events, the parties will cooperate in good faith to reschedule the session without additional fee.");

                    col.Item().Element(SectionHeading("9. LIMITATION OF LIABILITY"));
                    col.Item().Text(
                        "In the unlikely event of technical malfunction, equipment failure, loss of digital files, or damage to photographs while in the Studio's custody, the Studio's total liability shall be strictly limited to a full refund of all monies paid by the Client under the Agreement.");
                });

                page.Footer().PaddingTop(8).BorderTop(0.5f).BorderColor(BorderLight).PaddingTop(4).Row(r =>
                {
                    r.RelativeItem().Text($"{s.StudioName} — Standard Terms and Conditions ({terms.VersionCode})").FontSize(8).FontColor(TextLabel);
                    r.RelativeItem().AlignRight().Text(txt =>
                    {
                        txt.DefaultTextStyle(st => st.FontSize(8).FontColor(TextLabel));
                        txt.Span("Page ");
                        txt.CurrentPageNumber();
                        txt.Span(" of ");
                        txt.TotalPages();
                    });
                });
            });
        }).GeneratePdf();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 3. RESERVATION ACKNOWLEDGMENT RECEIPT (SHEET 1 PDF)
    // ══════════════════════════════════════════════════════════════════════════

    public byte[] GenerateReservationReceiptPdf(BookingReceiptDto r, BookingContractDto c, TermsVersionDto terms)
    {
        var studioName = string.IsNullOrWhiteSpace(c.StudioName) ? _studioDefaults.StudioName : c.StudioName;
        var studioAddress = string.IsNullOrWhiteSpace(c.StudioAddress) ? _studioDefaults.StudioAddress : c.StudioAddress;
        var studioPhone = string.IsNullOrWhiteSpace(c.StudioPhone) ? _studioDefaults.StudioPhone : c.StudioPhone;
        var studioEmail = string.IsNullOrWhiteSpace(c.StudioEmail) ? _studioDefaults.StudioEmail : c.StudioEmail;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.MarginTop(36);
                page.MarginBottom(36);
                page.MarginHorizontal(42);
                page.DefaultTextStyle(x => x.FontFamily("Lato").FontSize(9.5f).FontColor(TextBody).LineHeight(1.35f));

                page.Header().Column(h =>
                {
                    RenderMasthead(h, "RESERVATION ACKNOWLEDGMENT RECEIPT", "STAGE 1 PAYMENT — BOOKING CONFIRMATION & DATE LOCK");
                });

                page.Content().PaddingTop(14).Column(col =>
                {
                    col.Spacing(12);

                    col.Item().Border(1).BorderColor(BorderLight).Table(t =>
                    {
                        t.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(1);
                            cols.RelativeColumn(1);
                        });

                        t.Header(hdr =>
                        {
                            DarkHeaderCell(hdr, "Issued By (Photo Studio)");
                            DarkHeaderCell(hdr, "Receipt & Transaction Details");
                        });

                        t.Cell().BorderRight(1).BorderColor(BorderLight).Padding(8).Column(p =>
                        {
                            p.Spacing(2);
                            p.Item().Text(studioName).Bold();
                            p.Item().Text(studioAddress);
                            p.Item().Text($"Phone: {studioPhone}");
                            p.Item().Text($"Email: {studioEmail}");
                        });

                        t.Cell().Padding(8).Column(p =>
                        {
                            p.Spacing(2);
                            KeyValLine(p, "Receipt No.:", r.ReceiptNumber);
                            KeyValLine(p, "Booking Ref.:", r.EventAppointmentId);
                            KeyValLine(p, "Payment Date:", $"{r.IssuedAtUtc:MMMM dd, yyyy HH:mm} UTC");
                            KeyValLine(p, "Payment Method:", r.PaymentMethod);
                            KeyValLine(p, "Gateway Ref. ID:", r.GatewayReferenceId ?? r.ReceiptNumber);
                        });
                    });

                    col.Item().Element(SectionHeading("CLIENT & RESERVED SESSION SUMMARY"));
                    col.Item().Border(1).BorderColor(BorderLight).Table(t =>
                    {
                        t.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(1.2f);
                            cols.RelativeColumn(2.4f);
                        });

                        t.Header(hdr =>
                        {
                            DarkHeaderCell(hdr, "Detail");
                            DarkHeaderCell(hdr, "Recorded Information");
                        });

                        TwoColRow(t, "Client Name & Contact",
                            $"{c.ClientName} • {c.ClientEmail}" + (string.IsNullOrWhiteSpace(c.ClientPhone) ? "" : $" • {c.ClientPhone}"), shaded: false);
                        TwoColRow(t, "Session Type & Package", $"{c.SessionType} — {c.PackageName}", shaded: true);
                        TwoColRow(t, "Reserved Shoot Date & Time", $"{c.DateOfShoot:MMMM dd, yyyy} • {FormatTimeOnly(c.StartTime)} to {FormatTimeOnly(c.EndTime)}", shaded: false);
                        TwoColRow(t, "Location / Venue", c.Venue, shaded: true);
                        TwoColRow(t, "Accepted Agreement", $"Photo Studio Services Agreement & Standard Terms ({c.TermsVersionCode})", shaded: false);
                    });

                    col.Item().Element(SectionHeading("STAGE 1 FINANCIAL BREAKDOWN"));
                    col.Item().Border(1).BorderColor(BorderLight).Table(t =>
                    {
                        t.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(2.7f);
                            cols.RelativeColumn(1f);
                        });

                        t.Header(hdr =>
                        {
                            DarkHeaderCell(hdr, "Description");
                            DarkHeaderCellAlignRight(hdr, "Amount");
                        });

                        TwoColRightAmountRow(t, $"Total Contracted Package Fee ({c.PackageName})", Money(r.TotalContractCharges), bold: false, shaded: false);
                        TwoColRightAmountRow(t, $"Less: Stage 1 Reservation Fee Paid Today (Ref: {r.GatewayReferenceId ?? r.ReceiptNumber})", $"− {Money(r.Stage1PaidAmount)}", bold: false, shaded: true);
                        TwoColRightAmountRow(t, "Remaining Balance Due Upon Event Completion", Money(r.RemainingBalanceDue), bold: true, shaded: false);
                    });

                    col.Item().BorderLeft(3).BorderColor(NavyDark).Background(BgSubtle).Padding(10).Text(txt =>
                    {
                        txt.Span("Payment & Delivery Notice: ").Bold().FontColor(NavyDark);
                        txt.Span($"This receipt confirms payment of your non-refundable Reservation Fee and locks your scheduled date. The remaining balance of {Money(r.RemainingBalanceDue)} (plus any on-site overtime or add-ons) is due immediately upon completion of your session. Final edited photographs will be released upon full settlement.");
                    });
                });

                page.Footer().PaddingTop(8).BorderTop(0.5f).BorderColor(BorderLight).PaddingTop(4).Row(row =>
                {
                    row.RelativeItem().Text($"{studioName} — Reservation Acknowledgment Receipt").FontSize(8).FontColor(TextLabel);
                    row.RelativeItem().AlignRight().Text("Issued Electronically • Stage 1 of 2").FontSize(8).FontColor(TextLabel);
                });
            });
        }).GeneratePdf();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 4. FINAL CONSOLIDATED PAYMENT RECEIPT (SHEET 2 PDF)
    // ══════════════════════════════════════════════════════════════════════════

    public byte[] GenerateFinalConsolidatedReceiptPdf(
        BookingReceiptDto r,
        BookingContractDto c,
        TermsVersionDto terms,
        string? stage1DateRef,
        string? stage2DateRef,
        string? overtimeNote)
    {
        var studioName = string.IsNullOrWhiteSpace(c.StudioName) ? _studioDefaults.StudioName : c.StudioName;
        var studioAddress = string.IsNullOrWhiteSpace(c.StudioAddress) ? _studioDefaults.StudioAddress : c.StudioAddress;
        var studioPhone = string.IsNullOrWhiteSpace(c.StudioPhone) ? _studioDefaults.StudioPhone : c.StudioPhone;
        var studioEmail = string.IsNullOrWhiteSpace(c.StudioEmail) ? _studioDefaults.StudioEmail : c.StudioEmail;
        var deliveryDays = c.DeliveryBusinessDays > 0 ? c.DeliveryBusinessDays : terms.DeliveryBusinessDays;
        var basePackage = c.TotalPackageAmount > 0 ? c.TotalPackageAmount : (r.TotalContractCharges - r.OvertimeAddOnAmount);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.MarginTop(36);
                page.MarginBottom(36);
                page.MarginHorizontal(42);
                page.DefaultTextStyle(x => x.FontFamily("Lato").FontSize(9.5f).FontColor(TextBody).LineHeight(1.35f));

                page.Header().Column(h =>
                {
                    RenderMasthead(h, "FINAL CONSOLIDATED PAYMENT RECEIPT", "STAGE 2 SETTLEMENT — PAID IN FULL & DELIVERABLES UNLOCKED");
                });

                page.Content().PaddingTop(14).Column(col =>
                {
                    col.Spacing(12);

                    col.Item().Border(1).BorderColor(BorderLight).Table(t =>
                    {
                        t.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(1);
                            cols.RelativeColumn(1);
                        });

                        t.Header(hdr =>
                        {
                            DarkHeaderCell(hdr, "Issued By (Photo Studio)");
                            DarkHeaderCell(hdr, "Final Receipt & Settlement Details");
                        });

                        t.Cell().BorderRight(1).BorderColor(BorderLight).Padding(8).Column(p =>
                        {
                            p.Spacing(2);
                            p.Item().Text(studioName).Bold();
                            p.Item().Text(studioAddress);
                            p.Item().Text($"Phone: {studioPhone}");
                            p.Item().Text($"Email: {studioEmail}");
                        });

                        t.Cell().Padding(8).Column(p =>
                        {
                            p.Spacing(2);
                            KeyValLine(p, "Final Receipt No.:", r.ReceiptNumber);
                            KeyValLine(p, "Booking Ref.:", r.EventAppointmentId);
                            KeyValLine(p, "Settlement Date:", $"{r.IssuedAtUtc:MMMM dd, yyyy HH:mm} UTC");
                            KeyValLine(p, "Payment Method:", r.PaymentMethod);
                            KeyValLine(p, "Settlement Ref. ID:", r.GatewayReferenceId ?? r.ReceiptNumber);
                        });
                    });

                    col.Item().Element(SectionHeading("CLIENT & COMPLETED SESSION DETAILS"));
                    col.Item().Border(1).BorderColor(BorderLight).Table(t =>
                    {
                        t.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(1.2f);
                            cols.RelativeColumn(2.4f);
                        });

                        t.Header(hdr =>
                        {
                            DarkHeaderCell(hdr, "Detail");
                            DarkHeaderCell(hdr, "Recorded Information");
                        });

                        TwoColRow(t, "Client Name & Contact",
                            $"{c.ClientName} • {c.ClientEmail}" + (string.IsNullOrWhiteSpace(c.ClientPhone) ? "" : $" • {c.ClientPhone}"), shaded: false);
                        TwoColRow(t, "Completed Session & Date", $"{c.SessionType} — {c.PackageName} on {c.DateOfShoot:MMMM dd, yyyy}", shaded: true);
                        TwoColRow(t, "Estimated Delivery Window", $"Within {deliveryDays} business days from {r.IssuedAtUtc:MMMM dd, yyyy}", shaded: false);
                    });

                    col.Item().Element(SectionHeading("CONSOLIDATED PAYMENT LEDGER"));
                    col.Item().Border(1).BorderColor(BorderLight).Table(t =>
                    {
                        t.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(1.8f);
                            cols.RelativeColumn(1.3f);
                            cols.RelativeColumn(1.0f);
                        });

                        t.Header(hdr =>
                        {
                            DarkHeaderCell(hdr, "Line Item / Payment Entry");
                            DarkHeaderCell(hdr, "Date & Reference");
                            DarkHeaderCellAlignRight(hdr, "Amount");
                        });

                        ThreeColRightAmountRow(t, $"Contracted Package Fee ({c.PackageName})", $"Booking #{r.EventAppointmentId}", Money(basePackage), bold: false, shaded: false);
                        ThreeColRightAmountRow(t, "Approved On-Site Overtime / Add-Ons (if applicable)",
                            string.IsNullOrWhiteSpace(overtimeNote) ? (r.OvertimeAddOnAmount > 0 ? "Approved On-Site Add-On" : "None") : overtimeNote,
                            Money(r.OvertimeAddOnAmount), bold: false, shaded: true);
                        ThreeColRightAmountRow(t, "Total Contract Charges", "—", Money(r.TotalContractCharges), bold: true, shaded: false);
                        ThreeColRightAmountRow(t, "Payment 1: Stage 1 Reservation Fee",
                            string.IsNullOrWhiteSpace(stage1DateRef) ? "Stage 1 Settled" : stage1DateRef,
                            $"− {Money(r.Stage1PaidAmount)}", bold: false, shaded: true);
                        ThreeColRightAmountRow(t, "Payment 2: Stage 2 Final Event Settlement",
                            string.IsNullOrWhiteSpace(stage2DateRef) ? $"{r.IssuedAtUtc:yyyy-MM-dd} • {r.GatewayReferenceId ?? r.ReceiptNumber}" : stage2DateRef,
                            $"− {Money(r.Stage2PaidAmount)}", bold: false, shaded: false);
                        ThreeColRightAmountRow(t, "Remaining Balance Due", "STATUS: PAID IN FULL", "0.00", bold: true, shaded: true);
                    });

                    col.Item().BorderLeft(3).BorderColor(NavyDark).Background(BgSubtle).Padding(10).Text(txt =>
                    {
                        txt.Span("Account Settled & Delivery Queue Active: ").Bold().FontColor(NavyDark);
                        txt.Span($"Thank you for choosing {studioName}. Your account is now settled in full. Your final edited photographs are queued for delivery within {deliveryDays} business days in accordance with your Services Agreement.");
                    });
                });

                page.Footer().PaddingTop(8).BorderTop(0.5f).BorderColor(BorderLight).PaddingTop(4).Row(row =>
                {
                    row.RelativeItem().Text($"{studioName} — Final Consolidated Receipt").FontSize(8).FontColor(TextLabel);
                    row.RelativeItem().AlignRight().Text("Issued Electronically • Paid in Full").FontSize(8).FontColor(TextLabel);
                });
            });
        }).GeneratePdf();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // QUESTPDF SHARED LAYOUT HELPERS
    // ══════════════════════════════════════════════════════════════════════════

    private void RenderMasthead(ColumnDescriptor headerCol, string title, string subtitle)
    {
        headerCol.Item().Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                left.Item().Text(title).FontSize(17.5f).Bold().FontColor(NavyDark);
                left.Item().PaddingTop(2).Text(subtitle).FontSize(9.5f).FontColor(TextMuted);
            });

            if (_logoBytes is { Length: > 0 })
            {
                row.ConstantItem(58).AlignRight().AlignMiddle().Image(_logoBytes).FitWidth();
            }
        });

        headerCol.Item().PaddingTop(8).LineHorizontal(2f).LineColor(NavyDark);
    }

    private static Action<IContainer> SectionHeading(string text) => container =>
        container.PaddingTop(4).PaddingBottom(2).Text(text).FontSize(10.5f).Bold().FontColor(NavyDark);

    private static void MetaCell(TableDescriptor t, string label, string value, bool rightBorder)
    {
        var cell = t.Cell().Padding(8);
        if (rightBorder)
            cell = cell.BorderRight(1).BorderColor(BorderLight);
        cell.Column(col =>
        {
            col.Item().Text(label).FontSize(7.5f).Bold().FontColor(TextLabel);
            col.Item().PaddingTop(2).Text(value).FontSize(9.5f).FontColor(TextBody);
        });
    }

    private static void DarkHeaderCell(TableCellDescriptor hdr, string text)
    {
        hdr.Cell().Background(NavyDark).PaddingHorizontal(8).PaddingVertical(6)
            .Text(text).FontSize(9).Bold().FontColor(Colors.White);
    }

    private static void DarkHeaderCellAlignRight(TableCellDescriptor hdr, string text)
    {
        hdr.Cell().Background(NavyDark).PaddingHorizontal(8).PaddingVertical(6).AlignRight()
            .Text(text).FontSize(9).Bold().FontColor(Colors.White);
    }

    private static void KeyValLine(ColumnDescriptor col, string label, string value)
    {
        col.Item().Text(t =>
        {
            t.Span(label + " ").Bold().FontColor(TextBody);
            t.Span(value);
        });
    }

    private static void TwoColRow(TableDescriptor t, string label, string value, bool shaded)
    {
        var bg = shaded ? BgSubtle : "#FFFFFF";
        t.Cell().Background(bg).BorderTop(0.5f).BorderRight(0.5f).BorderColor(BorderLight).PaddingHorizontal(8).PaddingVertical(6)
            .Text(label).Bold().FontColor(TextBody);
        t.Cell().Background(bg).BorderTop(0.5f).BorderColor(BorderLight).PaddingHorizontal(8).PaddingVertical(6)
            .Text(value);
    }

    private static void TwoColRightAmountRow(TableDescriptor t, string description, string amount, bool bold, bool shaded)
    {
        var bg = shaded ? BgSubtle : "#FFFFFF";
        var descCell = t.Cell().Background(bg).BorderTop(0.5f).BorderRight(0.5f).BorderColor(BorderLight).PaddingHorizontal(8).PaddingVertical(6);
        var amtCell = t.Cell().Background(bg).BorderTop(0.5f).BorderColor(BorderLight).PaddingHorizontal(8).PaddingVertical(6).AlignRight();
        if (bold)
        {
            descCell.Text(description).Bold();
            amtCell.Text(amount).Bold();
        }
        else
        {
            descCell.Text(description);
            amtCell.Text(amount);
        }
    }

    private static void ThreeColRow(TableDescriptor t, string c1, string c2, string c3, bool boldAmount, bool shaded)
    {
        var bg = shaded ? BgSubtle : "#FFFFFF";
        t.Cell().Background(bg).BorderTop(0.5f).BorderRight(0.5f).BorderColor(BorderLight).PaddingHorizontal(8).PaddingVertical(6)
            .Text(c1).Bold();
        var mid = t.Cell().Background(bg).BorderTop(0.5f).BorderRight(0.5f).BorderColor(BorderLight).PaddingHorizontal(8).PaddingVertical(6);
        if (boldAmount) mid.Text(c2).Bold(); else mid.Text(c2);
        t.Cell().Background(bg).BorderTop(0.5f).BorderColor(BorderLight).PaddingHorizontal(8).PaddingVertical(6)
            .Text(c3);
    }

    private static void ThreeColRightAmountRow(TableDescriptor t, string c1, string c2, string c3, bool bold, bool shaded)
    {
        var bg = shaded ? BgSubtle : "#FFFFFF";
        var cell1 = t.Cell().Background(bg).BorderTop(0.5f).BorderRight(0.5f).BorderColor(BorderLight).PaddingHorizontal(8).PaddingVertical(6);
        var cell2 = t.Cell().Background(bg).BorderTop(0.5f).BorderRight(0.5f).BorderColor(BorderLight).PaddingHorizontal(8).PaddingVertical(6);
        var cell3 = t.Cell().Background(bg).BorderTop(0.5f).BorderColor(BorderLight).PaddingHorizontal(8).PaddingVertical(6).AlignRight();
        if (bold)
        {
            cell1.Text(c1).Bold();
            cell2.Text(c2).Bold();
            cell3.Text(c3).Bold();
        }
        else
        {
            cell1.Text(c1);
            cell2.Text(c2);
            cell3.Text(c3);
        }
    }

    private static void BulletPoint(ColumnDescriptor col, string boldLead, string body)
    {
        col.Item().Row(r =>
        {
            r.ConstantItem(12).Text("•").Bold().FontColor(NavyDark);
            r.RelativeItem().Text(t =>
            {
                t.Span(boldLead + " ").Bold();
                t.Span(body);
            });
        });
    }

    // ══════════════════════════════════════════════════════════════════════════
    // HTML SNAPSHOT RENDERERS (Preview & Immutable DB Snapshots)
    // ══════════════════════════════════════════════════════════════════════════

    public string RenderServicesAgreementHtml(BookingContractDto c, TermsVersionDto terms)
    {
        var studioName = E(string.IsNullOrWhiteSpace(c.StudioName) ? _studioDefaults.StudioName : c.StudioName);
        var studioAddress = E(string.IsNullOrWhiteSpace(c.StudioAddress) ? _studioDefaults.StudioAddress : c.StudioAddress);
        var studioPhone = E(string.IsNullOrWhiteSpace(c.StudioPhone) ? _studioDefaults.StudioPhone : c.StudioPhone);
        var studioEmail = E(string.IsNullOrWhiteSpace(c.StudioEmail) ? _studioDefaults.StudioEmail : c.StudioEmail);
        var repName = E(string.IsNullOrWhiteSpace(c.RepresentativeName) ? _studioDefaults.RepresentativeName : c.RepresentativeName);
        var agreementDate = E(c.CreatedAtUtc.ToString("MMMM dd, yyyy"));
        var shootDate = E(c.DateOfShoot.ToString("MMMM dd, yyyy"));
        var timeRange = E($"{FormatTimeOnly(c.StartTime)} to {FormatTimeOnly(c.EndTime)}");
        var deliveryDays = c.DeliveryBusinessDays > 0 ? c.DeliveryBusinessDays : terms.DeliveryBusinessDays;
        var acceptedTs = c.AcceptedAtUtc.HasValue ? E($"{c.AcceptedAtUtc.Value:yyyy-MM-dd HH:mm:ss} UTC") : "Pending";
        var ipRef = c.AcceptedAtUtc.HasValue
            ? E($"{(string.IsNullOrWhiteSpace(c.ClientIpAddress) ? "Recorded" : c.ClientIpAddress)} / {(string.IsNullOrWhiteSpace(c.ClickwrapReference) ? c.ContractNumber : c.ClickwrapReference)}")
            : "Pending Checkout Acceptance";

        return $@"
<div style=""font-family:Arial,Helvetica,sans-serif;color:#1e293b;max-width:780px;margin:0 auto;font-size:13px;line-height:1.5;"">
  <div style=""display:flex;justify-content:space-between;align-items:flex-start;border-bottom:2px solid #0f172a;padding-bottom:10px;margin-bottom:14px;"">
    <div>
      <h2 style=""margin:0;color:#0f172a;font-size:20px;letter-spacing:0.03em;"">PHOTO STUDIO SERVICES AGREEMENT</h2>
      <p style=""margin:3px 0 0;color:#475569;font-size:12px;"">Booking Contract &amp; Two-Stage Payment Schedule</p>
    </div>
    <img src=""/images/AevumLogo_2.jpg"" alt=""Photo Aevum Studio"" style=""height:48px;width:auto;"" />
  </div>
  <table style=""width:100%;border-collapse:collapse;background:#f8fafc;border:1px solid #cbd5e1;margin-bottom:14px;"">
    <tr>
      <td style=""padding:8px;border-right:1px solid #cbd5e1;width:33%;""><div style=""font-size:10px;font-weight:700;color:#64748b;"">AGREEMENT DATE</div><div>{agreementDate}</div></td>
      <td style=""padding:8px;border-right:1px solid #cbd5e1;width:33%;""><div style=""font-size:10px;font-weight:700;color:#64748b;"">BOOKING REFERENCE</div><div>{E(c.EventAppointmentId)} ({E(c.ContractNumber)})</div></td>
      <td style=""padding:8px;width:34%;""><div style=""font-size:10px;font-weight:700;color:#64748b;"">TERMS VERSION</div><div>{E(c.TermsVersionCode)}</div></td>
    </tr>
  </table>
  <p>This Photo Studio Services Agreement (the &ldquo;Agreement&rdquo;) is entered into on <strong>{agreementDate}</strong> by and between the Photo Studio and the Client identified below. By completing the online reservation and accepting the linked <em>Standard Terms and Conditions</em>, both parties agree to the full scope of services and two-stage payment schedule set forth herein.</p>
  <h3 style=""color:#0f172a;font-size:13px;margin:16px 0 6px;"">1. CONTRACTING PARTIES</h3>
  <table style=""width:100%;border-collapse:collapse;border:1px solid #cbd5e1;"">
    <tr style=""background:#0f172a;color:#fff;""><th style=""text-align:left;padding:7px 10px;"">Photo Studio</th><th style=""text-align:left;padding:7px 10px;"">Client</th></tr>
    <tr>
      <td style=""padding:8px 10px;border-right:1px solid #cbd5e1;vertical-align:top;"">
        <div><strong>Studio Name:</strong> {studioName}</div>
        <div><strong>Address:</strong> {studioAddress}</div>
        <div><strong>Contact:</strong> {studioPhone}</div>
        <div><strong>Email:</strong> {studioEmail}</div>
      </td>
      <td style=""padding:8px 10px;vertical-align:top;"">
        <div><strong>Client Name:</strong> {E(c.ClientName)}</div>
        <div><strong>Address:</strong> {E(string.IsNullOrWhiteSpace(c.ClientAddress) ? "On File with Studio" : c.ClientAddress)}</div>
        <div><strong>Contact:</strong> {E(string.IsNullOrWhiteSpace(c.ClientPhone) ? "On File" : c.ClientPhone)}</div>
        <div><strong>Email:</strong> {E(c.ClientEmail)}</div>
      </td>
    </tr>
  </table>
  <h3 style=""color:#0f172a;font-size:13px;margin:16px 0 6px;"">2. SCOPE OF SERVICES &amp; SESSION SCHEDULE</h3>
  <table style=""width:100%;border-collapse:collapse;border:1px solid #cbd5e1;"">
    <tr style=""background:#0f172a;color:#fff;""><th style=""text-align:left;padding:7px 10px;width:35%;"">Booking Item</th><th style=""text-align:left;padding:7px 10px;"">Confirmed Specification</th></tr>
    <tr><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;font-weight:700;"">Session / Event Type</td><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;"">{E(c.SessionType)}</td></tr>
    <tr style=""background:#f8fafc;""><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;font-weight:700;"">Date of Shoot</td><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;"">{shootDate}</td></tr>
    <tr><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;font-weight:700;"">Scheduled Time</td><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;"">{timeRange}</td></tr>
    <tr style=""background:#f8fafc;""><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;font-weight:700;"">Location / Venue</td><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;"">{E(c.Venue)}</td></tr>
    <tr><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;font-weight:700;"">Selected Package</td><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;"">{E(c.PackageName)}</td></tr>
    <tr style=""background:#f8fafc;""><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;font-weight:700;"">Package Inclusions</td><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;"">{E(c.PackageInclusions)}</td></tr>
  </table>
  <h3 style=""color:#0f172a;font-size:13px;margin:16px 0 6px;"">3. TWO-STAGE PAYMENT &amp; SETTLEMENT TERMS</h3>
  <table style=""width:100%;border-collapse:collapse;border:1px solid #cbd5e1;"">
    <tr style=""background:#0f172a;color:#fff;""><th style=""text-align:left;padding:7px 10px;"">Payment Stage</th><th style=""text-align:left;padding:7px 10px;"">Amount</th><th style=""text-align:left;padding:7px 10px;"">Due Date &amp; Condition</th></tr>
    <tr><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;font-weight:700;"">Total Package Fee</td><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;font-weight:700;"">{Money(c.TotalPackageAmount)}</td><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;"">Full contract value agreed upon at booking</td></tr>
    <tr style=""background:#f8fafc;""><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;font-weight:700;"">Stage 1: Reservation Fee</td><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;font-weight:700;"">{Money(c.ReservationFeeAmount)}</td><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;"">Due immediately upon online booking to lock date</td></tr>
    <tr><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;font-weight:700;"">Stage 2: Remaining Balance</td><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;font-weight:700;"">{Money(c.RemainingBalanceAmount)}</td><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;"">Due immediately upon event / session completion</td></tr>
  </table>
  <ul style=""margin:10px 0 12px;padding-left:18px;"">
    <li><strong>Booking Confirmation &amp; Reservation Fee:</strong> A booking is confirmed only upon receipt of the Stage 1 Reservation Fee and electronic or written acceptance of this Agreement. The Reservation Fee is non-refundable, secures the Studio&rsquo;s exclusive date allocation, and is deducted from the Total Package Fee.</li>
    <li><strong>End-of-Event Final Settlement:</strong> The Stage 2 Remaining Balance, together with any approved on-site overtime or package add-ons requested during the session, becomes due and payable immediately upon wrap-up of the event or photo session.</li>
    <li><strong>Deliverable Release Gate:</strong> The Studio shall not begin final export or release any edited photographs, raw previews, or deliverables until the Stage 2 Remaining Balance and any accrued overtime charges are settled in full (0.00 balance due) and a Final Consolidated Receipt is issued.</li>
  </ul>
  <h3 style=""color:#0f172a;font-size:13px;margin:14px 0 6px;"">4. INCORPORATION OF STANDARD TERMS &amp; CONDITIONS</h3>
  <p>This Agreement incorporates by reference the Studio&rsquo;s <em>Standard Terms and Conditions</em> ({E(c.TermsVersionCode)}), governing cancellation and refund tiers, rescheduling, late arrivals, image delivery timelines ({deliveryDays} business days after full settlement), intellectual property, force majeure, and limitation of liability.</p>
  <h3 style=""color:#0f172a;font-size:13px;margin:14px 0 6px;"">5. ELECTRONIC ACCEPTANCE &amp; SIGNATURES</h3>
  <div style=""border-left:3px solid #0f172a;background:#f8fafc;padding:10px 12px;margin-bottom:10px;"">
    <div><strong>Online Clickwrap Record (Populated Automatically Upon Checkout):</strong></div>
    <div>Accepted By: {E(c.ClientName)} | Account / Email: {E(c.ClientEmail)}</div>
    <div>Timestamp (UTC): {acceptedTs} | IP / Reference: {ipRef}</div>
  </div>
  <table style=""width:100%;border-collapse:collapse;border:1px solid #cbd5e1;"">
    <tr style=""background:#0f172a;color:#fff;""><th style=""text-align:left;padding:7px 10px;"">Client Acknowledgement</th><th style=""text-align:left;padding:7px 10px;"">Photo Studio Representative</th></tr>
    <tr>
      <td style=""padding:14px 10px;border-right:1px solid #cbd5e1;""><div style=""border-top:1px solid #475569;padding-top:4px;margin-top:18px;font-size:11px;color:#475569;"">Client Signature &amp; Date (or Electronic Acceptance)</div><div><strong>Printed Name:</strong> {E(c.ClientName)}</div></td>
      <td style=""padding:14px 10px;""><div style=""border-top:1px solid #475569;padding-top:4px;margin-top:18px;font-size:11px;color:#475569;"">Authorized Studio Representative &amp; Date</div><div><strong>Printed Name:</strong> {repName}</div></td>
    </tr>
  </table>
</div>";
    }

    public string RenderStandardTermsHtml(TermsVersionDto terms, StudioProfileSettings? studio = null)
    {
        if (!string.IsNullOrWhiteSpace(terms.TermsBodyHtml))
            return terms.TermsBodyHtml;

        var s = studio ?? _studioDefaults;
        var days = terms.DeliveryBusinessDays > 0 ? terms.DeliveryBusinessDays : s.DefaultDeliveryBusinessDays;
        return BuildDefaultStandardTermsHtml(s.StudioName, terms.VersionCode, terms.EffectiveDate, days);
    }

    public static string BuildDefaultStandardTermsHtml(string studioName, string versionCode, DateOnly effectiveDate, int deliveryDays)
    {
        var name = E(studioName);
        var ver = E(versionCode);
        var eff = E(effectiveDate.ToString("MMMM dd, yyyy"));
        return $@"
<div style=""font-family:Arial,Helvetica,sans-serif;color:#1e293b;max-width:780px;margin:0 auto;font-size:13px;line-height:1.5;"">
  <div style=""display:flex;justify-content:space-between;align-items:flex-start;border-bottom:2px solid #0f172a;padding-bottom:10px;margin-bottom:14px;"">
    <div>
      <h2 style=""margin:0;color:#0f172a;font-size:20px;"">STANDARD TERMS AND CONDITIONS</h2>
      <p style=""margin:3px 0 0;color:#475569;font-size:12px;"">{name} &bull; Policy Document Version: {ver} &bull; Effective Date: {eff}</p>
    </div>
    <img src=""/images/AevumLogo_2.jpg"" alt=""Photo Aevum Studio"" style=""height:48px;width:auto;"" />
  </div>
  <p>These Standard Terms and Conditions govern all photography and event coverage bookings entered into with <strong>{name}</strong> (&ldquo;Studio&rdquo;) and form an integral part of the Client&rsquo;s executed <em>Photo Studio Services Agreement</em>.</p>
  <h3 style=""color:#0f172a;font-size:13px;margin:14px 0 4px;"">1. BOOKING &amp; RESERVATION FEE POLICY</h3>
  <p>A booking is secured exclusively upon receipt of the required Reservation Fee and acceptance of the Services Agreement. The Reservation Fee covers date-blocking, administrative preparation, and pre-production, and is credited toward the Total Package Fee upon final settlement.</p>
  <h3 style=""color:#0f172a;font-size:13px;margin:14px 0 4px;"">2. POST-EVENT BALANCE &amp; ON-SITE OVERTIME</h3>
  <ul style=""padding-left:18px;"">
    <li><strong>End-of-Event Settlement:</strong> The remaining balance of the Total Package Fee is due immediately upon completion of the event or photo session. Online payment links, QR settlement, or recorded studio payment methods are accepted at wrap-up.</li>
    <li><strong>Overtime &amp; Additional Deliverables:</strong> Any session extensions, overtime hours, or package upgrades requested by the Client during the event will be billed at the Studio&rsquo;s published rate and added to the Final Balance Statement due at wrap-up.</li>
    <li><strong>Late Payment &amp; Hold on Deliverables:</strong> No edited images, gallery links, or physical deliverables will be released until the account balance is settled in full.</li>
  </ul>
  <h3 style=""color:#0f172a;font-size:13px;margin:14px 0 6px;"">3. CANCELLATION AND REFUND POLICY</h3>
  <table style=""width:100%;border-collapse:collapse;border:1px solid #cbd5e1;"">
    <tr style=""background:#0f172a;color:#fff;""><th style=""text-align:left;padding:7px 10px;width:35%;"">Cancellation Scenario</th><th style=""text-align:left;padding:7px 10px;"">Treatment of Reservation Fee &amp; Payments</th></tr>
    <tr><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;font-weight:700;"">More than 30 days before shoot</td><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;"">Reservation Fee is non-refundable but may be transferred once to a future booking within six (6) months, subject to Studio availability.</td></tr>
    <tr style=""background:#f8fafc;""><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;font-weight:700;"">15 to 30 days before shoot</td><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;"">Fifty percent (50%) of payments made are forfeited; any remaining balance paid above the Reservation Fee is refunded or credited.</td></tr>
    <tr><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;font-weight:700;"">Less than 15 days before shoot</td><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;"">All payments made, including the Reservation Fee, are strictly non-refundable.</td></tr>
    <tr style=""background:#f8fafc;""><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;font-weight:700;"">Client No-Show</td><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;"">Failure of the Client to appear at the scheduled session without prior notice results in immediate forfeiture of all payments made.</td></tr>
    <tr><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;font-weight:700;"">Studio Cancellation</td><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;"">If the Studio is unable to perform services due to emergency or unforeseen circumstances, all payments received shall be refunded in full.</td></tr>
  </table>
  <h3 style=""color:#0f172a;font-size:13px;margin:14px 0 4px;"">4. RESCHEDULING POLICY</h3>
  <p>Requests for rescheduling must be submitted in writing at least seven (7) days prior to the scheduled session and are subject to Studio availability. Only one (1) rescheduling request is permitted per booking without penalty, provided the new date falls within six (6) months of the original booking date.</p>
  <h3 style=""color:#0f172a;font-size:13px;margin:14px 0 4px;"">5. LATE ARRIVAL POLICY</h3>
  <p>Client delays reduce the available shooting time within the reserved schedule. The Studio is not responsible for compromised coverage or fewer images resulting from shortened schedules. Delays exceeding thirty (30) minutes may incur additional hourly charges or result in session cancellation without refund.</p>
  <h3 style=""color:#0f172a;font-size:13px;margin:14px 0 4px;"">6. IMAGE DELIVERY &amp; RAW FILES</h3>
  <p>Edited and finalized photographs will be delivered within {deliveryDays} business days following both completion of the session and full payment of the remaining balance. Delivery is made through the Studio&rsquo;s designated digital gallery or agreed delivery method. RAW and unedited files remain the exclusive property of the Studio and will not be provided.</p>
  <h3 style=""color:#0f172a;font-size:13px;margin:14px 0 4px;"">7. COPYRIGHT AND USAGE LICENSE</h3>
  <p>All photographs remain the intellectual property and copyright of the Studio. Upon full payment, the Client receives a personal-use license permitting them to print, share, and display delivered photographs for non-commercial purposes. Commercial use, third-party licensing, or resale is strictly prohibited without prior written consent from the Studio.</p>
  <h3 style=""color:#0f172a;font-size:13px;margin:14px 0 4px;"">8. FORCE MAJEURE</h3>
  <p>Neither party shall be liable for failure or delay in performing obligations under the Agreement if such failure or delay is caused by acts of God, natural disasters, typhoons, government restrictions, pandemics, strikes, or other events strictly beyond reasonable control. In such events, the parties will cooperate in good faith to reschedule the session without additional fee.</p>
  <h3 style=""color:#0f172a;font-size:13px;margin:14px 0 4px;"">9. LIMITATION OF LIABILITY</h3>
  <p>In the unlikely event of technical malfunction, equipment failure, loss of digital files, or damage to photographs while in the Studio&rsquo;s custody, the Studio&rsquo;s total liability shall be strictly limited to a full refund of all monies paid by the Client under the Agreement.</p>
</div>";
    }

    public string RenderReservationReceiptHtml(BookingReceiptDto r, BookingContractDto c, TermsVersionDto terms)
    {
        var studioName = E(string.IsNullOrWhiteSpace(c.StudioName) ? _studioDefaults.StudioName : c.StudioName);
        return $@"
<div style=""font-family:Arial,Helvetica,sans-serif;color:#1e293b;max-width:780px;margin:0 auto;font-size:13px;line-height:1.5;"">
  <div style=""display:flex;justify-content:space-between;align-items:flex-start;border-bottom:2px solid #0f172a;padding-bottom:10px;margin-bottom:14px;"">
    <div>
      <h2 style=""margin:0;color:#0f172a;font-size:20px;"">RESERVATION ACKNOWLEDGMENT RECEIPT</h2>
      <p style=""margin:3px 0 0;color:#475569;font-size:12px;"">STAGE 1 PAYMENT &mdash; BOOKING CONFIRMATION &amp; DATE LOCK</p>
    </div>
    <img src=""/images/AevumLogo_2.jpg"" alt=""Photo Aevum Studio"" style=""height:48px;width:auto;"" />
  </div>
  <table style=""width:100%;border-collapse:collapse;border:1px solid #cbd5e1;margin-bottom:12px;"">
    <tr style=""background:#0f172a;color:#fff;""><th style=""text-align:left;padding:7px 10px;"">Issued By (Photo Studio)</th><th style=""text-align:left;padding:7px 10px;"">Receipt &amp; Transaction Details</th></tr>
    <tr>
      <td style=""padding:8px 10px;border-right:1px solid #cbd5e1;""><strong>{studioName}</strong><br/>{E(c.StudioAddress)}<br/>Phone: {E(c.StudioPhone)}<br/>Email: {E(c.StudioEmail)}</td>
      <td style=""padding:8px 10px;""><strong>Receipt No.:</strong> {E(r.ReceiptNumber)}<br/><strong>Booking Ref.:</strong> {E(r.EventAppointmentId)}<br/><strong>Payment Date:</strong> {r.IssuedAtUtc:MMMM dd, yyyy HH:mm} UTC<br/><strong>Payment Method:</strong> {E(r.PaymentMethod)}<br/><strong>Gateway Ref. ID:</strong> {E(r.GatewayReferenceId ?? r.ReceiptNumber)}</td>
    </tr>
  </table>
  <h3 style=""color:#0f172a;font-size:13px;margin:14px 0 6px;"">STAGE 1 FINANCIAL BREAKDOWN</h3>
  <table style=""width:100%;border-collapse:collapse;border:1px solid #cbd5e1;"">
    <tr style=""background:#0f172a;color:#fff;""><th style=""text-align:left;padding:7px 10px;"">Description</th><th style=""text-align:right;padding:7px 10px;"">Amount</th></tr>
    <tr><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;"">Total Contracted Package Fee ({E(c.PackageName)})</td><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;text-align:right;"">{Money(r.TotalContractCharges)}</td></tr>
    <tr style=""background:#f8fafc;""><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;"">Less: Stage 1 Reservation Fee Paid Today (Ref: {E(r.GatewayReferenceId ?? r.ReceiptNumber)})</td><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;text-align:right;"">&minus; {Money(r.Stage1PaidAmount)}</td></tr>
    <tr><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;font-weight:700;"">Remaining Balance Due Upon Event Completion</td><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;text-align:right;font-weight:700;"">{Money(r.RemainingBalanceDue)}</td></tr>
  </table>
  <div style=""border-left:3px solid #0f172a;background:#f8fafc;padding:10px 12px;margin-top:12px;"">
    <strong>Payment &amp; Delivery Notice:</strong> This receipt confirms payment of your non-refundable Reservation Fee and locks your scheduled date. The remaining balance of {Money(r.RemainingBalanceDue)} (plus any on-site overtime or add-ons) is due immediately upon completion of your session. Final edited photographs will be released upon full settlement.
  </div>
</div>";
    }

    public string RenderFinalConsolidatedReceiptHtml(
        BookingReceiptDto r,
        BookingContractDto c,
        TermsVersionDto terms,
        string? stage1DateRef,
        string? stage2DateRef,
        string? overtimeNote)
    {
        var studioName = E(string.IsNullOrWhiteSpace(c.StudioName) ? _studioDefaults.StudioName : c.StudioName);
        var deliveryDays = c.DeliveryBusinessDays > 0 ? c.DeliveryBusinessDays : terms.DeliveryBusinessDays;
        var basePkg = c.TotalPackageAmount > 0 ? c.TotalPackageAmount : (r.TotalContractCharges - r.OvertimeAddOnAmount);

        return $@"
<div style=""font-family:Arial,Helvetica,sans-serif;color:#1e293b;max-width:780px;margin:0 auto;font-size:13px;line-height:1.5;"">
  <div style=""display:flex;justify-content:space-between;align-items:flex-start;border-bottom:2px solid #0f172a;padding-bottom:10px;margin-bottom:14px;"">
    <div>
      <h2 style=""margin:0;color:#0f172a;font-size:20px;"">FINAL CONSOLIDATED PAYMENT RECEIPT</h2>
      <p style=""margin:3px 0 0;color:#475569;font-size:12px;"">STAGE 2 SETTLEMENT &mdash; PAID IN FULL &amp; DELIVERABLES UNLOCKED</p>
    </div>
    <img src=""/images/AevumLogo_2.jpg"" alt=""Photo Aevum Studio"" style=""height:48px;width:auto;"" />
  </div>
  <table style=""width:100%;border-collapse:collapse;border:1px solid #cbd5e1;margin-bottom:12px;"">
    <tr style=""background:#0f172a;color:#fff;""><th style=""text-align:left;padding:7px 10px;"">Issued By (Photo Studio)</th><th style=""text-align:left;padding:7px 10px;"">Final Receipt &amp; Settlement Details</th></tr>
    <tr>
      <td style=""padding:8px 10px;border-right:1px solid #cbd5e1;""><strong>{studioName}</strong><br/>{E(c.StudioAddress)}<br/>Phone: {E(c.StudioPhone)}<br/>Email: {E(c.StudioEmail)}</td>
      <td style=""padding:8px 10px;""><strong>Final Receipt No.:</strong> {E(r.ReceiptNumber)}<br/><strong>Booking Ref.:</strong> {E(r.EventAppointmentId)}<br/><strong>Settlement Date:</strong> {r.IssuedAtUtc:MMMM dd, yyyy HH:mm} UTC<br/><strong>Payment Method:</strong> {E(r.PaymentMethod)}<br/><strong>Settlement Ref. ID:</strong> {E(r.GatewayReferenceId ?? r.ReceiptNumber)}</td>
    </tr>
  </table>
  <h3 style=""color:#0f172a;font-size:13px;margin:14px 0 6px;"">CONSOLIDATED PAYMENT LEDGER</h3>
  <table style=""width:100%;border-collapse:collapse;border:1px solid #cbd5e1;"">
    <tr style=""background:#0f172a;color:#fff;""><th style=""text-align:left;padding:7px 10px;"">Line Item / Payment Entry</th><th style=""text-align:left;padding:7px 10px;"">Date &amp; Reference</th><th style=""text-align:right;padding:7px 10px;"">Amount</th></tr>
    <tr><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;"">Contracted Package Fee ({E(c.PackageName)})</td><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;"">Booking #{E(r.EventAppointmentId)}</td><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;text-align:right;"">{Money(basePkg)}</td></tr>
    <tr style=""background:#f8fafc;""><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;"">Approved On-Site Overtime / Add-Ons (if applicable)</td><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;"">{E(string.IsNullOrWhiteSpace(overtimeNote) ? (r.OvertimeAddOnAmount > 0 ? "Approved On-Site Add-On" : "None") : overtimeNote)}</td><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;text-align:right;"">{Money(r.OvertimeAddOnAmount)}</td></tr>
    <tr><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;font-weight:700;"">Total Contract Charges</td><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;font-weight:700;"">&mdash;</td><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;text-align:right;font-weight:700;"">{Money(r.TotalContractCharges)}</td></tr>
    <tr style=""background:#f8fafc;""><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;"">Payment 1: Stage 1 Reservation Fee</td><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;"">{E(stage1DateRef ?? "Stage 1 Settled")}</td><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;text-align:right;"">&minus; {Money(r.Stage1PaidAmount)}</td></tr>
    <tr><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;"">Payment 2: Stage 2 Final Event Settlement</td><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;"">{E(stage2DateRef ?? $"{r.IssuedAtUtc:yyyy-MM-dd} • {r.GatewayReferenceId ?? r.ReceiptNumber}")}</td><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;text-align:right;"">&minus; {Money(r.Stage2PaidAmount)}</td></tr>
    <tr style=""background:#f8fafc;""><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;font-weight:700;"">Remaining Balance Due</td><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;font-weight:700;"">STATUS: PAID IN FULL</td><td style=""padding:7px 10px;border-top:1px solid #cbd5e1;text-align:right;font-weight:700;"">0.00</td></tr>
  </table>
  <div style=""border-left:3px solid #0f172a;background:#f8fafc;padding:10px 12px;margin-top:12px;"">
    <strong>Account Settled &amp; Delivery Queue Active:</strong> Thank you for choosing {studioName}. Your account is now settled in full. Your final edited photographs are queued for delivery within {deliveryDays} business days in accordance with your Services Agreement.
  </div>
</div>";
    }
}
