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

        // A ckan:// URL from --url waits here until a UI has subscribed above.
        public static string? PendingLaunchUrl;

        // Call once subscribed. Handles the --url URL if there was one.
        public static void HandlePendingLaunchUrl()
        {
            if (PendingLaunchUrl != null)
            {
                Handle(PendingLaunchUrl);
                PendingLaunchUrl = null;
            }
        }

        public static void Handle(string rawUrl)
        {
            if (string.IsNullOrWhiteSpace(rawUrl))
            {
                return;
            }

            // Defend against partial or mangled URLs: ckan:host, ckan:/host, CKAN://host, //host, host.

            if (rawUrl.StartsWith("ckan:", StringComparison.OrdinalIgnoreCase))
            {
                rawUrl = rawUrl["ckan:".Length..];
            }

            rawUrl = "ckan://" + rawUrl.TrimStart('/');

            // TryCreate requires the canonical form of ckan://host
            if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
            {
                return;
            }

            string host = uri.Host.ToLowerInvariant();
            var query = HttpUtility.ParseQueryString(uri.Query);

            switch (host)
            {
                case "focus":
                    {
                        string? mod = query["mod"];
                        if (!string.IsNullOrWhiteSpace(mod)) { OnFocus?.Invoke(mod); }
                        break;
                    }

                case "search":
                    {
                        string? q = query["q"];
                        if (!string.IsNullOrWhiteSpace(q)) { OnSearch?.Invoke(q); }
                        break;
                    }

                case "install":
                    {
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
}
