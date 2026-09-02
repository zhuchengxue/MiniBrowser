using MiniBrowser.App.Models;

namespace MiniBrowser.App.Services;

public sealed class TabSessionService
{
    public const int MaximumTabs = 20;
    private readonly WindowProfile _profile;
    private readonly string _homeUrl;

    public TabSessionService(WindowProfile profile, string homeUrl)
    {
        _profile = profile;
        _homeUrl = homeUrl;
        EnsureSession();
    }

    public IReadOnlyList<TabProfile> Tabs => _profile.Tabs;

    public TabProfile ActiveTab =>
        _profile.Tabs.First(tab => tab.Id == _profile.ActiveTabId);

    public TabProfile Create(string? url = null, string title = "New tab")
    {
        if (_profile.Tabs.Count >= MaximumTabs)
        {
            throw new InvalidOperationException($"MiniBrowser supports up to {MaximumTabs} tabs.");
        }

        var tab = new TabProfile
        {
            Url = string.IsNullOrWhiteSpace(url) ? _homeUrl : url,
            Title = string.IsNullOrWhiteSpace(title) ? "New tab" : title
        };
        _profile.Tabs.Add(tab);
        _profile.ActiveTabId = tab.Id;
        SyncWindowUrl(tab);
        return tab;
    }

    public bool Activate(string tabId)
    {
        var tab = _profile.Tabs.FirstOrDefault(candidate => candidate.Id == tabId);
        if (tab is null)
        {
            return false;
        }

        _profile.ActiveTabId = tab.Id;
        SyncWindowUrl(tab);
        return true;
    }

    public TabProfile ActivateRelative(int offset)
    {
        var currentIndex = _profile.Tabs.FindIndex(tab => tab.Id == _profile.ActiveTabId);
        var count = _profile.Tabs.Count;
        var nextIndex = ((currentIndex + offset) % count + count) % count;
        var next = _profile.Tabs[nextIndex];
        Activate(next.Id);
        return next;
    }

    public TabCloseResult Close(string tabId)
    {
        var index = _profile.Tabs.FindIndex(tab => tab.Id == tabId);
        if (index < 0)
        {
            return new TabCloseResult(null, ActiveTab, false);
        }

        var removed = _profile.Tabs[index];
        if (_profile.Tabs.Count == 1)
        {
            var replacement = Create(_homeUrl);
            _profile.Tabs.Remove(removed);
            SyncWindowUrl(replacement);
            return new TabCloseResult(removed, replacement, true);
        }

        var wasActive = removed.Id == _profile.ActiveTabId;
        _profile.Tabs.RemoveAt(index);
        if (wasActive)
        {
            var replacementIndex = Math.Min(index, _profile.Tabs.Count - 1);
            Activate(_profile.Tabs[replacementIndex].Id);
        }

        return new TabCloseResult(removed, ActiveTab, wasActive);
    }

    private void EnsureSession()
    {
        _profile.Tabs.RemoveAll(tab => string.IsNullOrWhiteSpace(tab.Id));
        if (_profile.Tabs.Count == 0)
        {
            Create(_profile.Url);
            return;
        }

        if (_profile.Tabs.All(tab => tab.Id != _profile.ActiveTabId))
        {
            _profile.ActiveTabId = _profile.Tabs[0].Id;
        }

        SyncWindowUrl(ActiveTab);
    }

    private void SyncWindowUrl(TabProfile tab)
    {
        _profile.Url = tab.Url;
    }
}

public sealed record TabCloseResult(TabProfile? Removed, TabProfile Active, bool ActiveChanged);
