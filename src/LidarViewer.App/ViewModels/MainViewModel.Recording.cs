using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LidarViewer.App.Streaming;
using LidarViewer.Core.Export;
using LidarViewer.Core.Models;
using LidarViewer.Core.Playback;
using LidarViewer.Core.Readers;
using LidarViewer.Core.Recording;
using LidarViewer.Core.Rendering;
using LidarViewer.Core.Streaming;

namespace LidarViewer.App.ViewModels;

public partial class MainViewModel
{
    private SequenceRecorder? _recorder;

    /// <summary>
    /// Odstep miedzy nagranymi klatkami w sekundach. Klatka to cala chmura
    /// widoczna w danym momencie, wiec jedna na sekunde daje zapis podobny
    /// do filmu zamiast zalewy plikow.
    /// </summary>
    public double RecordingIntervalSeconds { get; set; } = 1.0;

    /// <summary>
    /// Czy trwa nagrywanie strumienia live do pliku.
    /// </summary>
    public bool IsRecording
    {
        get => _recorder is not null;
    }

    /// <summary>
    /// Czy uzytkownik moze zaczac nowe nagrywanie. Nagrywanie wymaga
    /// strumienia live i nie moze startowac dwa razy.
    /// </summary>
    public bool CanStartRecording => IsLive && !IsRecording;

    /// <summary>
    /// Liczba klatek przyjetych do nagrania.
    /// </summary>
    public int RecordedFrameCount => _recorder?.FrameCount ?? 0;

    public string RecordInfo => IsRecording
        ? $"Nagrane klatki: {RecordedFrameCount:N0}"
        : string.Empty;

    /// <summary>
    /// Zapisuje chmure widoczna w scenie - czyli juz przepuszczona przez
    /// filtry - do pliku. Format wybiera sie po rozszerzeniu.
    /// </summary>
    public void ExportCloud(string path)
    {
        if (PointCloud is null)
        {
            Status = "Błąd: nie ma chmury do zapisania.";
            return;
        }

        try
        {
            PointCloudWriter.Write(PointCloud, path);
            Status = $"Zapisano {PointCloud.Count:N0} punktów do {Path.GetFileName(path)}.";
        }
        catch (Exception ex)
        {
            Status = $"Błąd: {ex.Message}";
        }
    }

    /// <summary>
    /// Zaczyna nagrywanie strumienia live do katalogu. Katalog jest
    /// tworzony, a klatki dostaja pliki frame_00000.ply i kolejne.
    /// </summary>
    public void StartRecording(string directory)
    {
        if (_recorder is not null)
            return;

        try
        {
            _recorder = new SequenceRecorder(directory, null, RecordingIntervalSeconds);
            OnPropertyChanged(nameof(IsRecording));
            OnPropertyChanged(nameof(CanStartRecording));
            OnPropertyChanged(nameof(RecordInfo));
            Status = $"Nagrywanie do katalogu {directory}.";
        }
        catch (Exception ex)
        {
            _recorder = null;
            Status = $"Błąd: {ex.Message}";
        }
    }

    /// <summary>
    /// Konczy nagrywanie i zapisuje manifest, dzieki czemu nagranie da sie
    /// odtworzyc w aplikacji.
    /// </summary>
    public async Task StopRecordingAsync()
    {
        if (_recorder is null)
            return;

        SequenceRecorder recorder = _recorder;
        _recorder = null;
        OnPropertyChanged(nameof(IsRecording));
        OnPropertyChanged(nameof(CanStartRecording));

        try
        {
            string manifest = await recorder.FinishAsync();
            Status = $"Zapisano nagranie: {manifest} ({recorder.FrameCount} klatek).";
        }
        catch (Exception ex)
        {
            Status = $"Błąd: {ex.Message}";
        }
        finally
        {
            OnPropertyChanged(nameof(RecordInfo));
        }
    }
}
