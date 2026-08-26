using FluentValidation;
using Sportarr.Api.Models;

namespace Sportarr.Api.Validators;

/// <summary>
/// Interface settings reach both the database and config.xml, and several of
/// them are read back as fixed vocabularies rather than free text. A value
/// outside the set the UI offers renders as a broken page rather than a
/// rejected save, so it is refused here.
/// </summary>
public class UISettingsRequestValidator : AbstractValidator<UISettings>
{
    private static readonly string[] DaysOfWeek = { "sunday", "monday" };
    private static readonly string[] Themes = { "auto", "light", "dark" };
    private static readonly string[] ViewModes = { "auto", "compact", "spacious" };

    public UISettingsRequestValidator()
    {
        RuleFor(x => x.FirstDayOfWeek)
            .Must(v => DaysOfWeek.Contains(v, StringComparer.OrdinalIgnoreCase))
            .WithMessage("First day of week must be sunday or monday.");

        RuleFor(x => x.Theme)
            .Must(v => Themes.Contains(v, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Theme must be auto, light or dark.");

        RuleFor(x => x.EventViewMode)
            .Must(v => ViewModes.Contains(v, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Event view mode must be auto, compact or spacious.");

        RuleFor(x => x.CalendarWeekColumnHeader)
            .NotEmpty().WithMessage("Calendar week column header is required.")
            .MaximumLength(50);

        RuleFor(x => x.ShortDateFormat)
            .NotEmpty().WithMessage("Short date format is required.")
            .MaximumLength(50);

        RuleFor(x => x.LongDateFormat)
            .NotEmpty().WithMessage("Long date format is required.")
            .MaximumLength(50);

        RuleFor(x => x.TimeFormat)
            .NotEmpty().WithMessage("Time format is required.")
            .MaximumLength(50);

        RuleFor(x => x.UILanguage)
            .NotEmpty().WithMessage("Language is required.")
            .MaximumLength(20);

        // The interface page offers one second to ten minutes.
        RuleFor(x => x.QueryBackoffCapMs)
            .InclusiveBetween(1000, 600000)
            .WithMessage("Backoff cap must be between 1 and 600 seconds.");

        // Empty means follow the host. Anything else has to be a zone this
        // machine can actually resolve, or every displayed time falls back
        // silently and the setting looks ignored.
        RuleFor(x => x.TimeZone)
            .Must(BeAKnownTimeZone)
            .WithMessage("Time zone must be empty or a known IANA time zone id.");
    }

    private static bool BeAKnownTimeZone(string? timeZone)
    {
        if (string.IsNullOrWhiteSpace(timeZone)) return true;

        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(timeZone);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }
}
