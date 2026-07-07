using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalDictation.Application.Abstractions;
using LocalDictation.Domain;

namespace LocalDictation.ViewModels;

/// <summary>
/// Backs the searchable history view. Wraps <see cref="IHistoryRepository"/> queries and exposes
/// commands to copy, favourite and delete entries. Avalonia port of the WPF HistoryViewModel; copy
/// uses <c>pbcopy</c> (no live window required).
/// </summary>
public sealed class HistoryViewModel : ObservableObject
{
    private readonly IHistoryRepository _repo;

    /// <summary>Creates the view model and loads the first page.</summary>
    public HistoryViewModel(IHistoryRepository repo)
    {
        _repo = repo;
        CopyCommand = new RelayCommand<HistoryEntry>(Copy);
        ToggleFavoriteCommand = new AsyncRelayCommand<HistoryEntry>(ToggleFavoriteAsync);
        DeleteCommand = new AsyncRelayCommand<HistoryEntry>(DeleteAsync);
        _ = LoadAsync();
    }

    private string _search = "";
    /// <summary>Full-text search term; changing it reloads the page.</summary>
    public string Search { get => _search; set { if (SetProperty(ref _search, value)) _ = LoadAsync(); } }

    private bool _favoritesOnly;
    /// <summary>Restrict the list to favourites.</summary>
    public bool FavoritesOnly { get => _favoritesOnly; set { if (SetProperty(ref _favoritesOnly, value)) _ = LoadAsync(); } }

    private bool _isEmpty;
    /// <summary>True when the current query returned no rows.</summary>
    public bool IsEmpty { get => _isEmpty; set => SetProperty(ref _isEmpty, value); }

    /// <summary>The current result set.</summary>
    public ObservableCollection<HistoryEntry> Entries { get; } = new();

    /// <summary>Copies an entry's delivered text.</summary>
    public IRelayCommand<HistoryEntry> CopyCommand { get; }
    /// <summary>Toggles the favourite flag.</summary>
    public IAsyncRelayCommand<HistoryEntry> ToggleFavoriteCommand { get; }
    /// <summary>Deletes an entry.</summary>
    public IAsyncRelayCommand<HistoryEntry> DeleteCommand { get; }

    private async Task LoadAsync()
    {
        var results = await _repo.QueryAsync(new HistoryQuery(Search, FavoritesOnly: FavoritesOnly, Take: 200));
        Entries.Clear();
        foreach (var e in results) Entries.Add(e);
        IsEmpty = Entries.Count == 0;
    }

    // Copy via pbcopy so history works without a focused TopLevel/window clipboard.
    private static void Copy(HistoryEntry? entry)
    {
        if (entry is null) return;
        try
        {
            var psi = new ProcessStartInfo("/usr/bin/pbcopy") { RedirectStandardInput = true, UseShellExecute = false };
            using var p = Process.Start(psi);
            if (p is null) return;
            p.StandardInput.Write(entry.ProcessedText);
            p.StandardInput.Close();
            p.WaitForExit(2000);
        }
        catch { /* clipboard busy */ }
    }

    private async Task ToggleFavoriteAsync(HistoryEntry? entry)
    {
        if (entry is null) return;
        entry.Favorite = !entry.Favorite;
        await _repo.UpdateFlagsAsync(entry.Id, entry.Favorite, entry.Pinned);
        if (FavoritesOnly) await LoadAsync();
    }

    private async Task DeleteAsync(HistoryEntry? entry)
    {
        if (entry is null) return;
        await _repo.DeleteAsync(entry.Id);
        Entries.Remove(entry);
        IsEmpty = Entries.Count == 0;
    }
}
