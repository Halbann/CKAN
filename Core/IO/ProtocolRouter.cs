using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace CKAN.IO
{
    // Why a URL couldn't be handled. Each UI words these itself.
    public enum UrlError
    {
        BadSyntax,
        UnknownOperation,
        // The operation is known but nothing usable came with it.
        NoValidKeys,
    }

    public static class ProtocolRouter
    {
        public static event Action<string>? OnFocus;
        public static event Action<string>? OnSearch;
        public static event Action<List<(string Mod, string? Version)>>? OnInstall;
        public static event Action<UrlError>? OnError;

        // --url value set by cmdline. Only HandlePendingLaunchUrl reads it.
        public static string? PendingLaunchUrl { internal get; set; }

        public static bool HasPendingLaunchUrl => PendingLaunchUrl != null;

        public static string ErrorMessage(UrlError error)
            => error switch
               {
                   UrlError.BadSyntax => Properties.Resources.UrlBadSyntax,
                   UrlError.UnknownOperation => Properties.Resources.UrlUnknownOperation,
                   UrlError.NoValidKeys => Properties.Resources.UrlNoValidKeys,
                   _ => Properties.Resources.UrlBadSyntax,
               };

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
                OnError?.Invoke(UrlError.BadSyntax);
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
                    else
                    {
                        OnError?.Invoke(UrlError.NoValidKeys);
                    }
                    break;

                case "search":
                    string? q = query["q"];
                    if (!string.IsNullOrWhiteSpace(q))
                    {
                        OnSearch?.Invoke(q);
                    }
                    else
                    {
                        OnError?.Invoke(UrlError.NoValidKeys);
                    }
                    break;

                case "install":
                    string[]? modValues = query.GetValues("mod")
                                               ?.Where(x => !string.IsNullOrWhiteSpace(x))
                                               .ToArray();

                    if (modValues == null || modValues.Length == 0)
                    {
                        OnError?.Invoke(UrlError.NoValidKeys);
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

                default:
                    OnError?.Invoke(UrlError.UnknownOperation);
                    break;
            }
        }
    }
}
