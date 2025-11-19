using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;
using System.IO;

namespace Sportarr.Api.Services;

/// <summary>
/// Serilog text formatter that sanitizes sensitive data before writing to output
/// Wraps the standard MessageTemplateTextFormatter and sanitizes the final output
/// </summary>
public class SanitizingTextFormatter : ITextFormatter
{
    private readonly MessageTemplateTextFormatter _innerFormatter;

    public SanitizingTextFormatter(string outputTemplate)
    {
        _innerFormatter = new MessageTemplateTextFormatter(outputTemplate);
    }

    public void Format(LogEvent logEvent, TextWriter output)
    {
        // Format using the inner formatter to a temporary string
        var stringWriter = new StringWriter();
        _innerFormatter.Format(logEvent, stringWriter);
        var formattedMessage = stringWriter.ToString();

        // Sanitize the formatted message
        var sanitized = LogSanitizer.Sanitize(formattedMessage);

        // Write sanitized output
        output.Write(sanitized);
    }
}
