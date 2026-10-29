using System.Net;



namespace PhotoAevumStudio.Infrastructure.Services;



/// <summary>

/// Generates responsive, luxury transactional HTML email layouts for studio communications.

/// </summary>

public static class StudioEmailTemplates

{

    private const string DarkHeaderBg = "#0b0f19";

    private const string AccentGold = "#d97706";

    private const string AccentEmerald = "#059669";

    private const string CardBg = "#ffffff";

    private const string OuterBg = "#f1f5f9";

    private const string TextPrimary = "#0f172a";

    private const string TextSecondary = "#475569";

    private const string BorderColor = "#e2e8f0";



    public static string WrapLayout(string previewText, string badgeText, string badgeColor, string title, string contentHtml)

    {

        return $@"<!DOCTYPE html>

<html lang=""en"">

<head>

  <meta charset=""utf-8"">

  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">

  <title>{WebUtility.HtmlEncode(title)}</title>

  <!--[if mso]>

  <style type=""text/css"">

    body, table, td, a {{ font-family: Arial, sans-serif !important; }}

  </style>

  <![endif]-->

</head>

<body style=""margin:0;padding:0;background-color:{OuterBg};font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;-webkit-font-smoothing:antialiased;"">

  <span style=""display:none;font-size:1px;color:{OuterBg};line-height:1px;max-height:0px;max-width:0px;opacity:0;overflow:hidden;"">

    {WebUtility.HtmlEncode(previewText)}

  </span>



  <table role=""presentation"" width=""100%"" border=""0"" cellspacing=""0"" cellpadding=""0"" style=""background-color:{OuterBg};padding:32px 16px;"">

    <tr>

      <td align=""center"">

        <table role=""presentation"" width=""100%"" border=""0"" cellspacing=""0"" cellpadding=""0"" style=""max-width:620px;background-color:{CardBg};border-radius:16px;overflow:hidden;box-shadow:0 4px 20px rgba(0,0,0,0.06);border:1px solid {BorderColor};"">

          

          <!-- Header Banner -->

          <tr>

            <td style=""background-color:{DarkHeaderBg};padding:32px 36px;text-align:center;"">

              <table role=""presentation"" width=""100%"" border=""0"" cellspacing=""0"" cellpadding=""0"">

                <tr>

                  <td align=""center"">

                    <div style=""display:inline-block;padding:8px 16px;border:1px solid rgba(217,119,6,0.4);border-radius:9999px;margin-bottom:12px;background:rgba(217,119,6,0.1);"">

                      <span style=""color:#fbbf24;font-size:11px;font-weight:700;letter-spacing:2px;text-transform:uppercase;"">Photo Aevum Studio</span>

                    </div>

                    <h1 style=""margin:0;color:#ffffff;font-size:22px;font-weight:800;letter-spacing:-0.5px;"">AEVUM STUDIO</h1>

                    <p style=""margin:4px 0 0;color:#94a3b8;font-size:12px;letter-spacing:1px;text-transform:uppercase;"">Fine Art Photography &bull; Commercial Studio &bull; Manila</p>

                  </td>

                </tr>

              </table>

            </td>

          </tr>



          <!-- Main Content Body -->

          <tr>

            <td style=""padding:36px;"">

              <div style=""margin-bottom:20px;"">

                <span style=""display:inline-block;padding:4px 12px;border-radius:6px;font-size:11px;font-weight:700;letter-spacing:0.5px;text-transform:uppercase;background-color:{badgeColor};color:#ffffff;"">

                  {WebUtility.HtmlEncode(badgeText)}

                </span>

                <h2 style=""margin:12px 0 6px;color:{TextPrimary};font-size:20px;font-weight:800;line-height:1.3;"">

                  {WebUtility.HtmlEncode(title)}

                </h2>

              </div>



              {contentHtml}



              <!-- Studio Guarantee Notice -->

              <div style=""margin-top:28px;padding:16px;background-color:#f8fafc;border-radius:10px;border-left:3px solid {AccentGold};"">

                <p style=""margin:0;font-size:12px;line-height:1.5;color:#475569;"">

                  <strong>Studio Assurance:</strong> Your appointment is monitored by our creative concierge. For scheduling inquiries or assistance, simply reply to this email or visit our client portal.

                </p>

              </div>

            </td>

          </tr>



          <!-- Footer -->

          <tr>

            <td style=""background-color:#f8fafc;padding:24px 36px;border-top:1px solid {BorderColor};text-align:center;"">

              <p style=""margin:0 0 6px;color:#64748b;font-size:12px;font-weight:600;"">Photo Aevum Studio &bull; Manila, Philippines</p>

              <p style=""margin:0 0 12px;color:#94a3b8;font-size:11px;"">

                This is an official transactional message regarding your studio appointment.

              </p>

              <div style=""font-size:11px;color:#94a3b8;"">

                <a href=""https://photoaevum.com/terms"" style=""color:#64748b;text-decoration:underline;margin:0 8px;"">Terms of Service</a> &bull;

                <a href=""https://photoaevum.com/privacy"" style=""color:#64748b;text-decoration:underline;margin:0 8px;"">Privacy Policy</a> &bull;

                <a href=""https://photoaevum.com"" style=""color:#64748b;text-decoration:underline;margin:0 8px;"">Client Portal</a>

              </div>

              <p style=""margin:12px 0 0;color:#cbd5e1;font-size:10px;"">&copy; 2026 Photo Aevum Studio Inc. All rights reserved.</p>

            </td>

          </tr>



        </table>

      </td>

    </tr>

  </table>

</body>

</html>";

    }



    public static string ReservationPaymentRequired(

        string customerName,

        string appointmentId,

        string eventName,

        DateOnly eventDate,

        TimeOnly eventTime,

        string venue,

        decimal totalAmount,

        decimal downpaymentAmount,

        DateTime holdExpiresAt,

        string invoiceUrl,

        string paymentMethod = "Online Card or QR Ph")

    {

        var preview = $"Action Required: Settle 15% Reservation Fee for {eventName} (#{appointmentId})";

        var badge = "Reservation Payment Required";

        var title = $"Secure Your Shoot Date for {eventName}";



        var expiresText = holdExpiresAt.ToLocalTime().ToString("h:mm tt");



        var content = $@"

<p style=""margin:0 0 16px;color:{TextSecondary};font-size:14px;line-height:1.6;"">

  Dear <strong>{WebUtility.HtmlEncode(customerName)}</strong>,

</p>

<p style=""margin:0 0 20px;color:{TextSecondary};font-size:14px;line-height:1.6;"">

  Your event photoshoot appointment (<strong>#{WebUtility.HtmlEncode(appointmentId)}</strong>) has been configured following your consultation. To formally confirm your reservation and hold your creative crew and studio space, please settle the reservation fee within <strong>30 minutes</strong>.

</p>



<!-- Summary Card -->

<div style=""background-color:#f8fafc;border:1px solid {BorderColor};border-radius:12px;padding:20px;margin-bottom:24px;"">

  <table role=""presentation"" width=""100%"" border=""0"" cellspacing=""0"" cellpadding=""0"">

    <tr>

      <td style=""padding:6px 0;font-size:13px;color:#64748b;font-weight:600;"">Event:</td>

      <td align=""right"" style=""padding:6px 0;font-size:13px;color:{TextPrimary};font-weight:700;"">{WebUtility.HtmlEncode(eventName)}</td>

    </tr>

    <tr>

      <td style=""padding:6px 0;font-size:13px;color:#64748b;font-weight:600;"">Date & Time:</td>

      <td align=""right"" style=""padding:6px 0;font-size:13px;color:{TextPrimary};font-weight:700;"">{eventDate:dddd, MMMM d, yyyy} at {eventTime:h:mm tt}</td>

    </tr>

    <tr>

      <td style=""padding:6px 0;font-size:13px;color:#64748b;font-weight:600;"">Venue / Space:</td>

      <td align=""right"" style=""padding:6px 0;font-size:13px;color:{TextPrimary};font-weight:700;"">{(string.IsNullOrWhiteSpace(venue) ? "Studio A (Main Hall)" : WebUtility.HtmlEncode(venue))}</td>

    </tr>

    <tr>

      <td style=""padding:6px 0;font-size:13px;color:#64748b;font-weight:600;"">Accepted Channels:</td>

      <td align=""right"" style=""padding:6px 0;font-size:13px;color:{TextPrimary};font-weight:700;"">{WebUtility.HtmlEncode(paymentMethod)}</td>

    </tr>

    <tr>

      <td colspan=""2"" style=""border-top:1px dashed {BorderColor};padding-top:12px;margin-top:8px;""></td>

    </tr>

    <tr>

      <td style=""padding:4px 0;font-size:13px;color:#64748b;"">Total Package Value:</td>

      <td align=""right"" style=""padding:4px 0;font-size:13px;color:{TextPrimary};font-weight:700;"">₱{totalAmount:N2}</td>

    </tr>

    <tr>

      <td style=""padding:8px 0;font-size:15px;color:{AccentEmerald};font-weight:800;"">Reservation Deposit Due:</td>

      <td align=""right"" style=""padding:8px 0;font-size:18px;color:{AccentEmerald};font-weight:900;"">₱{downpaymentAmount:N2}</td>

    </tr>

    <tr>

      <td style=""padding:4px 0;font-size:12px;color:#dc2626;font-weight:700;"">Hold Reservation Expires:</td>

      <td align=""right"" style=""padding:4px 0;font-size:12px;color:#dc2626;font-weight:700;"">{expiresText} (30 mins)</td>

    </tr>

  </table>

</div>



<!-- CTA Button -->

<div style=""text-align:center;margin:32px 0 24px;"">

  <a href=""{invoiceUrl}"" target=""_blank""

     style=""display:inline-block;background-color:{AccentEmerald};color:#ffffff;padding:16px 36px;border-radius:10px;text-decoration:none;font-weight:800;font-size:15px;letter-spacing:0.3px;box-shadow:0 4px 12px rgba(5,150,105,0.25);"">

    Pay Reservation Fee Now &rarr;

  </a>

</div>



<p style=""margin:0;font-size:12px;color:#94a3b8;text-align:center;"">

  Powered by Xendit Secure Checkout &bull; Visa, Mastercard, GCash, Maya, QR Ph

</p>";



        return WrapLayout(preview, badge, AccentEmerald, title, content);

    }



    public static string ConsultationRescheduled(

        string customerName,

        string negotiationId,

        DateOnly newDate,

        TimeOnly newTime,

        string clerkName,

        string meetingLink,

        string? notes)

    {

        var preview = $"Your consultation #{negotiationId} has been rescheduled to {newDate:MMM d} at {newTime:h:mm tt}";

        var badge = "Consultation Rescheduled";

        var title = "Updated Consultation Schedule";



        var meetLinkHtml = string.IsNullOrWhiteSpace(meetingLink) ? "" : $@"

<div style=""text-align:center;margin:28px 0 20px;"">

  <a href=""{meetingLink}"" target=""_blank""

     style=""display:inline-block;background-color:#0f172a;color:#ffffff;padding:14px 28px;border-radius:10px;text-decoration:none;font-weight:700;font-size:14px;"">

    📹 Join Video Consultation

  </a>

</div>

<p style=""font-size:12px;color:#64748b;text-align:center;word-break:break-all;"">Direct Link: <a href=""{meetingLink}"" style=""color:#0284c7;"">{meetingLink}</a></p>";



        var notesHtml = string.IsNullOrWhiteSpace(notes) ? "" : $@"

<div style=""margin-top:12px;padding:12px;background:#f1f5f9;border-radius:8px;font-size:12px;color:#334155;"">

  <strong>Notes from Clerk:</strong> {WebUtility.HtmlEncode(notes)}

</div>";



        var content = $@"

<p style=""margin:0 0 16px;color:{TextSecondary};font-size:14px;line-height:1.6;"">

  Dear <strong>{WebUtility.HtmlEncode(customerName)}</strong>,

</p>

<p style=""margin:0 0 20px;color:{TextSecondary};font-size:14px;line-height:1.6;"">

  Your online schedule consultation (<strong>#{WebUtility.HtmlEncode(negotiationId)}</strong>) with Photo Aevum Studio has been rescheduled to the updated time below.

</p>



<div style=""background-color:#f8fafc;border:1px solid {BorderColor};border-radius:12px;padding:20px;margin-bottom:20px;"">

  <table role=""presentation"" width=""100%"" border=""0"" cellspacing=""0"" cellpadding=""0"">

    <tr>

      <td style=""padding:6px 0;font-size:13px;color:#64748b;font-weight:600;"">New Date:</td>

      <td align=""right"" style=""padding:6px 0;font-size:13px;color:{TextPrimary};font-weight:700;"">{newDate:dddd, MMMM d, yyyy}</td>

    </tr>

    <tr>

      <td style=""padding:6px 0;font-size:13px;color:#64748b;font-weight:600;"">New Time:</td>

      <td align=""right"" style=""padding:6px 0;font-size:13px;color:{TextPrimary};font-weight:700;"">{newTime:h:mm tt}</td>

    </tr>

    <tr>

      <td style=""padding:6px 0;font-size:13px;color:#64748b;font-weight:600;"">Assigned Concierge:</td>

      <td align=""right"" style=""padding:6px 0;font-size:13px;color:{TextPrimary};font-weight:700;"">{WebUtility.HtmlEncode(clerkName)}</td>

    </tr>

    <tr>

      <td style=""padding:6px 0;font-size:13px;color:#64748b;font-weight:600;"">Platform:</td>

      <td align=""right"" style=""padding:6px 0;font-size:13px;color:{TextPrimary};font-weight:700;"">Google Meet</td>

    </tr>

  </table>

  {notesHtml}

</div>



{meetLinkHtml}";



        return WrapLayout(preview, badge, "#2563eb", title, content);

    }

    public static string ConsultationScheduled(

        string customerName,

        string negotiationId,

        DateOnly consultationDate,

        TimeOnly consultationTime,

        string clerkName,

        string meetingLink,

        string? notes)

    {

        var preview = $"Your consultation #{negotiationId} is confirmed for {consultationDate:MMM d} at {consultationTime:h:mm tt}";

        var badge = "Consultation Scheduled";

        var title = "Studio Consultation Scheduled";



        var meetLinkHtml = string.IsNullOrWhiteSpace(meetingLink) ? "" : $@"

<div style=""text-align:center;margin:28px 0 20px;"">

  <a href=""{meetingLink}"" target=""_blank""

     style=""display:inline-block;background-color:#0f172a;color:#ffffff;padding:14px 28px;border-radius:10px;text-decoration:none;font-weight:700;font-size:14px;"">

    📹 Join Video Consultation

  </a>

</div>

<p style=""font-size:12px;color:#64748b;text-align:center;word-break:break-all;"">Direct Link: <a href=""{meetingLink}"" style=""color:#0284c7;"">{meetingLink}</a></p>";



        var notesHtml = string.IsNullOrWhiteSpace(notes) ? "" : $@"

<div style=""margin-top:12px;padding:12px;background:#f1f5f9;border-radius:8px;font-size:12px;color:#334155;"">

  <strong>Client Inquiry Notes:</strong> {WebUtility.HtmlEncode(notes)}

</div>";



        var content = $@"

<p style=""margin:0 0 16px;color:{TextSecondary};font-size:14px;line-height:1.6;"">

  Dear <strong>{WebUtility.HtmlEncode(customerName)}</strong>,

</p>

<p style=""margin:0 0 20px;color:{TextSecondary};font-size:14px;line-height:1.6;"">

  Your online schedule consultation (<strong>#{WebUtility.HtmlEncode(negotiationId)}</strong>) with Photo Aevum Studio has been confirmed.

</p>



<div style=""background-color:#f8fafc;border:1px solid {BorderColor};border-radius:12px;padding:20px;margin-bottom:20px;"">

  <table role=""presentation"" width=""100%"" border=""0"" cellspacing=""0"" cellpadding=""0"">

    <tr>

      <td style=""padding:6px 0;font-size:13px;color:#64748b;font-weight:600;"">Date:</td>

      <td align=""right"" style=""padding:6px 0;font-size:13px;color:{TextPrimary};font-weight:700;"">{consultationDate:dddd, MMMM d, yyyy}</td>

    </tr>

    <tr>

      <td style=""padding:6px 0;font-size:13px;color:#64748b;font-weight:600;"">Time:</td>

      <td align=""right"" style=""padding:6px 0;font-size:13px;color:{TextPrimary};font-weight:700;"">{consultationTime:h:mm tt}</td>

    </tr>

    <tr>

      <td style=""padding:6px 0;font-size:13px;color:#64748b;font-weight:600;"">Assigned Concierge:</td>

      <td align=""right"" style=""padding:6px 0;font-size:13px;color:{TextPrimary};font-weight:700;"">{WebUtility.HtmlEncode(clerkName)}</td>

    </tr>

    <tr>

      <td style=""padding:6px 0;font-size:13px;color:#64748b;font-weight:600;"">Platform:</td>

      <td align=""right"" style=""padding:6px 0;font-size:13px;color:{TextPrimary};font-weight:700;"">Google Meet</td>

    </tr>

  </table>

  {notesHtml}

</div>



{meetLinkHtml}";



        return WrapLayout(preview, badge, "#059669", title, content);

    }

    public static string DownpaymentConfirmed(
        string customerName,
        string appointmentId,
        string eventName,
        DateOnly eventDate,
        TimeOnly eventTime,
        decimal totalAmount,
        decimal downpaymentAmount,
        decimal balanceAmount,
        string? venue = null)
    {
        var preview = $"Confirmed: 15% Reservation Deposit Received for {eventName} (#{appointmentId})";
        var badge = "Reservation Confirmed";
        var title = "Your Studio Photoshoot Date is Locked!";

        var content = $@"
<p style=""margin:0 0 16px;color:{TextSecondary};font-size:14px;line-height:1.6;"">
  Dear <strong>{WebUtility.HtmlEncode(customerName)}</strong>,
</p>
<p style=""margin:0 0 20px;color:{TextSecondary};font-size:14px;line-height:1.6;"">
  We have successfully received your 15% reservation deposit of <strong>₱{downpaymentAmount:N2}</strong> via Xendit checkout for <strong>{WebUtility.HtmlEncode(eventName)}</strong> (<strong>#{WebUtility.HtmlEncode(appointmentId)}</strong>). Your creative crew and studio gear have been officially assigned and confirmed!
</p>

<div style=""background-color:#f0fdf4;border:1px solid #bbf7d0;border-radius:12px;padding:20px;margin-bottom:24px;"">
  <table role=""presentation"" width=""100%"" border=""0"" cellspacing=""0"" cellpadding=""0"">
    <tr>
      <td style=""padding:6px 0;font-size:13px;color:#166534;font-weight:600;"">Event:</td>
      <td align=""right"" style=""padding:6px 0;font-size:13px;color:#14532d;font-weight:700;"">{WebUtility.HtmlEncode(eventName)}</td>
    </tr>
    <tr>
      <td style=""padding:6px 0;font-size:13px;color:#166534;font-weight:600;"">Date &amp; Time:</td>
      <td align=""right"" style=""padding:6px 0;font-size:13px;color:#14532d;font-weight:700;"">{eventDate:dddd, MMMM d, yyyy} at {eventTime:h:mm tt}</td>
    </tr>
    <tr>
      <td style=""padding:6px 0;font-size:13px;color:#166534;font-weight:600;"">Studio Status:</td>
      <td align=""right"" style=""padding:6px 0;font-size:13px;color:#047857;font-weight:800;"">&#10004; Confirmed &amp; Calendar Locked</td>
    </tr>
    <tr>
      <td colspan=""2"" style=""border-top:1px dashed #bbf7d0;padding-top:12px;margin-top:8px;""></td>
    </tr>
    <tr>
      <td style=""padding:4px 0;font-size:13px;color:#166534;"">Total Package Value:</td>
      <td align=""right"" style=""padding:4px 0;font-size:13px;color:#14532d;font-weight:700;"">₱{totalAmount:N2}</td>
    </tr>
    <tr>
      <td style=""padding:4px 0;font-size:13px;color:#15803d;font-weight:700;"">15% Deposit Paid:</td>
      <td align=""right"" style=""padding:4px 0;font-size:14px;color:#15803d;font-weight:800;"">₱{downpaymentAmount:N2}</td>
    </tr>
    <tr>
      <td style=""padding:6px 0;font-size:13px;color:#64748b;font-weight:600;"">Remaining Balance (85% Due on shoot day):</td>
      <td align=""right"" style=""padding:6px 0;font-size:14px;color:#0f172a;font-weight:800;"">₱{balanceAmount:N2}</td>
    </tr>
  </table>
</div>

<div style=""text-align:center;margin:28px 0 20px;"">
  <a href=""https://localhost:7128/my/bookings""
     style=""display:inline-block;background-color:#0f172a;color:#ffffff;padding:14px 28px;border-radius:10px;text-decoration:none;font-weight:700;font-size:14px;"">
    View In Client Portal &rarr;
  </a>
</div>

<p style=""margin:0;font-size:12px;color:#94a3b8;text-align:center;"">
  Our team lead and lead photographer will reach out prior to your shoot day to review the shot list.
</p>";

        return WrapLayout(preview, badge, "#059669", title, content);
    }
}