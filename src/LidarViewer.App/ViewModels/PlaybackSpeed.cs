namespace LidarViewer.App.ViewModels;

/// <summary>
/// Predkosc odtwarzania - mnoznik czasu i etykieta do combo boxa.
/// </summary>
public sealed record PlaybackSpeed(double Multiplier, string Label);
