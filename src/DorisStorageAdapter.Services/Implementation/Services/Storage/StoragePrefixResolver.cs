using DorisStorageAdapter.Services.Contract.Models;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Net.Mime;
using System.Text;

namespace DorisStorageAdapter.Services.Implementation.Services.Storage;

internal sealed class StoragePrefixResolver
{
    public StoragePrefixResolver()
    {
       // _options = options.Value;
    }

    public string GetRootPrefix(StorageContext storageContext)
    {
        //if (!_options.Enabled)
        //    return "";

        // Ska inställningen kollas så att fel slängs om ej aktiv men partition skickas in?
        // Eller bara kolla om Enabled och annars returnera tom sträng?

        return storageContext.PartitionId + '/';
    }
}