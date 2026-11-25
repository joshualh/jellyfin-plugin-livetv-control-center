// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.LiveTvGroups.Model;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.LiveTv;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LiveTvGroups.Services;

public sealed class AutoCategories(IServiceProvider services, IHostApplicationLifetime lifetime,
    ILogger<AutoCategories> logger) : BackgroundService
{
    internal sealed record Entry(string Name, string Number, string Group);
    private Entry[] _entries = [];
    private static readonly Regex Attr = new("(?:^|\\s)([a-zA-Z0-9_-]+)=\"([^\"]*)\"", RegexOptions.Compiled);
    private static readonly Regex Prefix = new(@"^(\d+(?:\.\d+)?)\.\s*", RegexOptions.Compiled);
    public bool HasEntries => Volatile.Read(ref _entries).Length != 0;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!lifetime.ApplicationStarted.IsCancellationRequested)
            await Task.Delay(100, stoppingToken).ConfigureAwait(false);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Refresh(stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception) { logger.LogWarning("TV Guide category refresh failed; keeping previous snapshot."); }
            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task Refresh(CancellationToken token)
    {
        var options = services.GetRequiredService<IServerConfigurationManager>().GetConfiguration<LiveTvOptions>("livetv");
        var entries = new List<Entry>();
        var failed = false;
        foreach (var tuner in options?.TunerHosts ?? Array.Empty<TunerHostInfo>())
        {
            var source = Source(tuner);
            if (source is null) continue;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(60));
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
                using var response = Uri.TryCreate(source, UriKind.Absolute, out var uri)
                    && (uri.Scheme == "http" || uri.Scheme == "https")
                    ? await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false) : null;
                response?.EnsureSuccessStatusCode();
                using var stream = response is null ? File.OpenRead(source)
                    : await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                using var reader = new StreamReader(stream);
                var first = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
                if (first is null || !first.TrimStart('\uFEFF', ' ').StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Not M3U");
                var size = first.Length;
                while (await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false) is { } line)
                {
                    size += line.Length + 1;
                    if (size > 32 * 1024 * 1024) throw new InvalidDataException("Playlist too large");
                    if (Parse(line) is { } entry) entries.Add(entry);
                }
            }
            catch (Exception) when (!token.IsCancellationRequested)
            { failed = true; logger.LogWarning("A configured tuner category source could not be read."); }
        }
        if (failed) return;
        Volatile.Write(ref _entries, entries.ToArray());
        logger.LogInformation("TV Guide category refresh: {Entries} entries, {Groups} groups.", entries.Count,
            entries.Select(e => e.Group).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    internal static string? Source(TunerHostInfo tuner)
    {
        if (string.IsNullOrWhiteSpace(tuner.Url)) return null;
        if (string.Equals(tuner.Type, "m3u", StringComparison.OrdinalIgnoreCase)) return tuner.Url;
        if (!string.Equals(tuner.Type, "hdhomerun", StringComparison.OrdinalIgnoreCase)
            || !Uri.TryCreate(tuner.Url, UriKind.Absolute, out var url)
            || (url.Scheme != "http" && url.Scheme != "https")) return null;
        var path = url.AbsolutePath.TrimEnd('/');
        var marker = path.IndexOf("/hdhr", StringComparison.OrdinalIgnoreCase);
        if (marker < 0 || (path.Length > marker + 5 && path[marker + 5] != '/')) return null;
        var suffix = path[(marker + 5)..].TrimEnd('/');
        foreach (var file in new[] { "/discover.json", "/lineup.json", "/lineup_status.json" })
            if (suffix.EndsWith(file, StringComparison.OrdinalIgnoreCase)) { suffix = suffix[..^file.Length]; break; }
        // An output profile is a stream format setting, not a channel profile.
        var output = suffix.IndexOf("/output_profile/", StringComparison.OrdinalIgnoreCase);
        if (output >= 0) suffix = suffix[..output];
        return new UriBuilder(url) { Path = path[..marker] + "/output/m3u" + suffix, Fragment = "" }.Uri.AbsoluteUri;
    }

    internal static Entry? Parse(string line)
    {
        if (!line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase)) return null;
        var quoted = false;
        var comma = -1;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') quoted = !quoted;
            else if (line[i] == ',' && !quoted) { comma = i; break; }
        }
        if (comma < 0) return null;
        var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Attr.Matches(line[..comma])) attrs[m.Groups[1].Value] = m.Groups[2].Value.Trim();
        string Value(string key) => attrs.GetValueOrDefault(key, "");
        var name = line[(comma + 1)..].Trim();
        var prefix = Prefix.Match(name);
        var number = Value("tvg-chno");
        if (number.Length == 0) number = Value("channel-number");
        if (prefix.Success && number.Length == 0) number = prefix.Groups[1].Value;
        if (prefix.Success) name = name[prefix.Length..].Trim();
        if (name.Length == 0) name = Value("tvg-name");
        if (name.Length == 0) name = Value("tvg-id");
        var group = Value("group-title");
        if (name.Length == 0 || group.Length == 0 || group.Length > 100 || group.Any(char.IsControl)) return null;
        return new Entry(name, number, group);
    }

    public IReadOnlyList<ChannelGroup> GetGroups(IReadOnlyDictionary<Guid, LiveTvChannel> accessible)
    {
        var entries = Volatile.Read(ref _entries);
        if (entries.Length == 0 || accessible.Count == 0) return [];
        var names = accessible.Values.ToLookup(c => (c.Name ?? "").Trim(), StringComparer.OrdinalIgnoreCase);
        var groups = new Dictionary<string, ChannelGroup>(StringComparer.OrdinalIgnoreCase);
        var matched = new HashSet<Guid>();
        foreach (var e in entries)
        {
            var candidates = names[e.Name].ToArray();
            if (e.Number.Length > 0)
            {
                var exact = candidates.Where(c => string.Equals(c.Number, e.Number, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (exact.Length != 0) candidates = exact;
                else if (candidates.Any(c => !string.IsNullOrWhiteSpace(c.Number))) continue;
            }
            if (candidates.Length != 1) continue;
            var channel = candidates[0];
            if (!groups.TryGetValue(e.Group, out var group))
                groups[e.Group] = group = new ChannelGroup { Id = Id(e.Group), Name = e.Group };
            if (matched.Add(channel.Id) || group.Channels.All(c => c.ItemId != channel.Id))
                group.Channels.Add(GroupService.ToRef(channel));
        }
        if (groups.Count == 0) return [];
        var rest = accessible.Values.Where(c => !matched.Contains(c.Id)).Select(GroupService.ToRef).ToList();
        if (rest.Count > 0 && !groups.ContainsKey("Uncategorised"))
            groups["Uncategorised"] = new ChannelGroup { Id = Id("Uncategorised"), Name = "Uncategorised", Channels = rest };
        return groups.Values.ToList();
    }

    private static Guid Id(string group) => new(SHA256.HashData(Encoding.UTF8.GetBytes("jlh.tvguide.auto.v1:" + group.ToUpperInvariant())).AsSpan(0, 16));
}
