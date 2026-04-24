using DorisStorageAdapter.BagIt.Fetch;
using DorisStorageAdapter.BagIt.Info;
using DorisStorageAdapter.BagIt.Manifest;
using DorisStorageAdapter.Services.Implementation.Services.Validation;
using System;
using System.Collections.Generic;
using System.Text;

namespace DorisStorageAdapter.Services.Implementation.Services.Bags;

internal static class BagItElementSafety
{
    public static void Validate(BagItInfo info)
    {
        // Om Dataset-Status finns, har giltigt värde (?)
        // Om Access-Right finns, har giltigt värde (?)
        // Kan inte kräva att de finns om vi vill använda bag-info innan publicering?

    }

    public static void Validate(BagItFetch fetch, string versionPath)
    {
        // url - lite beroende på hur mycket vi kollar i parsning
        // size - kolla inte här
        // filePath - kontrollera på vanligt vis

        static void Throw() => throw new InvalidOperationException("Invalid fetch URL encountered.");

        foreach (var item in fetch.Items)
        {
            if (string.IsNullOrEmpty(item.Url))
            {
                Throw();
            }

            if (!item.Url.StartsWith("../", StringComparison.Ordinal))
            {
                Throw();
            }

            if (!Uri.TryCreate(item.Url, UriKind.Relative, out _))
            {
                Throw();
            }

            if (item.Url.Contains('?', StringComparison.Ordinal) ||
                item.Url.Contains('#', StringComparison.Ordinal))
            {
                Throw();
            }

            string decoded = Uri.UnescapeDataString(item.Url[3..]);

            if (!PathValidation.HasOnlyValidComponents(decoded))
            {
                Throw();
            }

            int index = decoded.IndexOf('/', StringComparison.Ordinal) + 1;

            if (index <= 1)
            {
                Throw();
            }

            string referencedVersionPath = decoded[..index];

            // Check that versionPath does not point to this version.
            if (referencedVersionPath == versionPath)
            {
                Throw();
            }

            string pathInBag = decoded[index..];

            if (!pathInBag.StartsWith(BagPathLayout.PayloadRootPath, StringComparison.Ordinal))
            {
                // Does not reference a payload file (under data/).
                Throw();
            }
        }

    }

    public static void Validate(BagItPayloadManifest manifest)
    {
       // filePath + kolla börjar med data/
    }

    public static void Validate(BagItTagManifest manifest)
    {
       // filePath + kolla börjar inte med data/
    }

}
