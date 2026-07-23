using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace CKAN.IO
{
    public static class ProtocolRouter
    {
        public static event Action<string>? OnFocus;
        public static event Action<string>? OnSearch;
        public static event Action<List<(string Mod, string? Version)>>? OnInstall;

        // --url value set by cmdline. Only HandlePendingLaunchUrl reads it.
        public static string? PendingLaunchUrl { internal get; set; }

        public static bool HasPendingLaunchUrl => PendingLaunchUrl != null;

        // Call once subscribed. Handles the --url URL if there was one.
        public static void HandlePendingLaunchUrl()
        {
            Handle(PendingLaunchUrl);
            PendingLaunchUrl = null;
        }

        public static void Handle(string? rawUrl)
        {
            if (rawUrl == null || string.IsNullOrWhiteSpace(rawUrl))
            {
                return;
            }

            // The --url option works without a scheme, e.g. install?mod=JNSQ.
            if (!rawUrl.StartsWith("ckan://", StringComparison.OrdinalIgnoreCase))
            {
                rawUrl = "ckan://" + rawUrl;
            }

            // TryCreate requires the canonical form of ckan://host
            if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
            {
                return;
            }

            var query = HttpUtility.ParseQueryString(uri.Query);

            // Uri Host is always lower case.
            switch (uri.Host)
            {
                case "focus":
                    string? focusMod = query["mod"];
                    if (!string.IsNullOrWhiteSpace(focusMod))
                    {
                        OnFocus?.Invoke(focusMod);
                    }
                    break;

                case "search":
                    string? q = query["q"];
                    if (!string.IsNullOrWhiteSpace(q))
                    {
                        OnSearch?.Invoke(q);
                    }
                    break;

                case "install":
                    string[]? modValues = query.GetValues("mod")
                                               ?.Where(x => !string.IsNullOrWhiteSpace(x))
                                               .ToArray();

                    if (modValues == null || modValues.Length == 0)
                    {
                        break;
                    }

                    var mods = modValues
                        .Select(mod =>
                        {
                            int colonIndex = mod.IndexOf(':');
                            return colonIndex == -1
                                ? (mod, null)
                                : (mod[..colonIndex], (string?)mod[(colonIndex + 1)..]);
                        })
                        .ToList();

                    OnInstall?.Invoke(mods);
                    break;
            }
        }
    }
}
