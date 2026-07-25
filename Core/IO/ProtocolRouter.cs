using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace CKAN.IO
{
    public enum UrlError
    {
        BadSyntax,
        UnknownOperation,
        NoValidKeys,
    }

    public static class ProtocolRouter
    {
        public static event Action<string>? OnFocus;
        public static event Action<string>? OnSearch;
        public static event Action<List<(string Mod, string? Version)>>? OnInstall;
        public static event Action<UrlError>? OnError;

        // --url value set by cmdline. Only read by HandlePendingLaunchUrl.
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
            // Double null check to appease compiler.
            if (rawUrl == null || string.IsNullOrWhiteSpace(rawUrl))
            {
                return;
            }

            // The --url option works without a scheme, e.g. install?mod=JNSQ.
            if (!rawUrl.StartsWith("ckan://", StringComparison.OrdinalIgnoreCase))
            {
                rawUrl = "ckan://" + rawUrl;
            }

            // Will fail if rawUrl isn't in the form `scheme://host?key=value&key=value`
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
                    RouteValue(query["mod"], OnFocus);
                    break;
                case "search":
                    RouteValue(query["q"], OnSearch);
                    break;
                case "install":
                    RouteMods(query.GetValues("mod"), OnInstall);
                    break;
                default:
                    OnError?.Invoke(UrlError.UnknownOperation);
                    break;
            }
        }

        private static void RouteValue(string? value, Action<string>? handler)
        {
            // We expect one value at this point, but the URL could include multiple, and they will end up here as CSV.

            if (value != null)
            {
                int comma = value.IndexOf(',');
                string first = (comma >= 0 ? value[..comma] : value).Trim();

                if (first.Length > 0)
                {
                    handler?.Invoke(first);
                    return;
                }
            }

            OnError?.Invoke(UrlError.NoValidKeys);
        }

        private static void RouteMods(string[]? modValues, Action<List<(string Mod, string? Version)>>? handler)
        {
            modValues = modValues?.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();

            if (modValues == null || modValues.Length == 0)
            {
                OnError?.Invoke(UrlError.NoValidKeys);
                return;
            }

            handler?.Invoke(modValues.Select(SplitPinned).ToList());
        }

        // Split "mod:version" or "mod" into "mod" and a nullable "version".
        private static (string Mod, string? Version) SplitPinned(string mod)
        {
            int colon = mod.IndexOf(':');

            return colon == -1 ? (mod, null) : (mod[..colon], mod[(colon + 1)..]);
        }
    }
}
