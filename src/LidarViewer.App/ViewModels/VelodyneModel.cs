namespace LidarViewer.App.ViewModels;

/// <summary>
/// Model czujnika Velodyne z wbudowana kalibracja. Inne modele (VLP-32, HDL-64E)
/// wymagaja pliku db.xml.
/// </summary>
public enum VelodyneModel
{
    Vlp16,
    Hdl32E,
}
