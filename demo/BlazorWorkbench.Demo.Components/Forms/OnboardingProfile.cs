namespace SampleComponents.Forms;

/// <summary>Answers collected by <see cref="OnboardingWizard"/>.</summary>
public sealed record OnboardingProfile(string Name, string Activity = "Hiking", MeasurementUnits Units = MeasurementUnits.Metric, bool Newsletter = true);

/// <summary>Units used to show distances and temperatures.</summary>
public enum MeasurementUnits { Metric, Imperial }
