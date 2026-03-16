using System;

namespace DorisStorageAdapter.Shared;

public static class UriHelpers
{
    public static Uri EnsureUriEndsWithSlash(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!uri.AbsoluteUri.EndsWith('/'))
        {
            return new Uri(uri.AbsoluteUri + '/');
        }

        return uri;
    }
}
