namespace PhotoAevumStudio.Application.Models;

public class EmailConfig
{
    public SmtpSettings Primary { get; set; } = new();
    public SmtpSettings Fallback { get; set; } = new();
}
