using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.Services.Implementation.BagIt.Info;

internal sealed class BagItInfo : IBagItElement<BagItInfo>
{
    private readonly SortedDictionary<string, List<BagItInfoItem>> items = new(StringComparer.Ordinal);

    private static readonly HashSet<string> reservedLabels = new(
    [
        baggingDateLabel.ToUpperInvariant(),
        bagCountLabel.ToUpperInvariant(),
        bagGroupIdentifierLabel.ToUpperInvariant(),
        bagSizeLabel.ToUpperInvariant(),
        contactEmailLabel.ToUpperInvariant(),
        contactNameLabel.ToUpperInvariant(),
        contactPhoneLabel.ToUpperInvariant(),
        externalDescriptionLabel.ToUpperInvariant(),
        externalIdentifierLabel.ToUpperInvariant(),
        internalSenderIdentifier.ToUpperInvariant(),
        internalSenderDescription.ToUpperInvariant(),
        organizationAddressLabel.ToUpperInvariant(),
        sourceOrganizationLabel.ToUpperInvariant(),
        payloadOxumLabel.ToUpperInvariant()
    ]);

    private const string baggingDateLabel = "Bagging-Date";
    private const string bagCountLabel = "Bag-Count";
    private const string bagGroupIdentifierLabel = "Bag-Group-Identifier";
    private const string bagSizeLabel = "Bag-Size";
    private const string contactEmailLabel = "Contact-Email";
    private const string contactNameLabel = "Contact-Name";
    private const string contactPhoneLabel = "Contact-Phone";
    private const string externalDescriptionLabel = "External-Description";
    private const string externalIdentifierLabel = "External-Identifier";
    private const string internalSenderIdentifier = "Internal-Sender-Identifier";
    private const string internalSenderDescription = "Internal-Sender-Description";
    private const string organizationAddressLabel = "Organization-Address";
    private const string sourceOrganizationLabel = "Source-Organization";
    private const string payloadOxumLabel = "Payload-Oxum";

    public DateTime? BaggingDate
    {
        get => GetSingleValue(baggingDateLabel, v =>
            DateTime.TryParseExact(v,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var dateTime)
                    ? dateTime
                    : (DateTime?)null);

        set => SetSingleValue(baggingDateLabel, value,
            v => v?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    public string? BagGroupIdentifier
    {
        get => GetSingleValue(bagGroupIdentifierLabel, v => v);
        set => SetSingleValue(bagGroupIdentifierLabel, value, v => v);
    }

    public BagCount? BagCount
    {
        get => GetSingleValue(bagCountLabel, v =>
        {
            var values = v.Split(" of ");

            if (values.Length == 2 &&
                long.TryParse(values[0], out long ordinal))
            {
                if (long.TryParse(values[1], out long totalCount))
                {
                    return new BagCount(ordinal, totalCount);
                }
                else if (values[1].Trim() == "?")
                {
                    return new BagCount(ordinal, null);
                }
            }

            return null;
        });

        set => SetSingleValue(bagCountLabel, value,
            v => v.Ordinal.ToString(CultureInfo.InvariantCulture) + " of " +
                 v.TotalCount?.ToString(CultureInfo.InvariantCulture) ?? "?");
    }

    public string? BagSize
    {
        get => GetSingleValue(bagSizeLabel, v => v);
        set => SetSingleValue(bagSizeLabel, value, v => v);
    }

    public IEnumerable<string> ContactEmail
    {
        get => GetValues(contactEmailLabel, false);
        set => SetValues(contactEmailLabel, value, false);
    }

    public IEnumerable<string> ContactName
    {
        get => GetValues(contactNameLabel, false);
        set => SetValues(contactNameLabel, value, false);
    }

    public IEnumerable<string> ContactPhone
    {
        get => GetValues(contactPhoneLabel, false);
        set => SetValues(contactPhoneLabel, value, false);
    }

    public IEnumerable<string> ExternalDescription
    {
        get => GetValues(externalDescriptionLabel, false);
        set => SetValues(externalDescriptionLabel, value, false);
    }

    public IEnumerable<string> ExternalIdentifier
    {
        get => GetValues(externalIdentifierLabel, false);
        set => SetValues(externalIdentifierLabel, value, false);
    }

    public IEnumerable<string> InternalSenderDescription
    {
        get => GetValues(internalSenderDescription, false);
        set => SetValues(internalSenderDescription, value, false);
    }

    public IEnumerable<string> InternalSenderIdentifier
    {
        get => GetValues(internalSenderIdentifier, false);
        set => SetValues(internalSenderIdentifier, value, false);
    }

    public IEnumerable<string> OrganizationAddress
    {
        get => GetValues(organizationAddressLabel, false);
        set => SetValues(organizationAddressLabel, value, false);
    }

    public PayloadOxum? PayloadOxum
    {
        get => GetSingleValue(payloadOxumLabel, v =>
        {
            var values = v.Split('.');
            if (values.Length == 2 &&
                long.TryParse(values[0], out long octetCount) &&
                long.TryParse(values[1], out long streamCount))
            {
                return new PayloadOxum(octetCount, streamCount);
            }

            return null;
        });

        set => SetSingleValue(payloadOxumLabel, value,
            v => v.OctetCount.ToString(CultureInfo.InvariantCulture) + '.' +
                 v.StreamCount.ToString(CultureInfo.InvariantCulture));
    }

    public IEnumerable<string> SourceOrganization
    {
        get => GetValues(sourceOrganizationLabel, false);
        set => SetValues(sourceOrganizationLabel, value, false);
    }

    private T? GetSingleValue<T>(string label, Func<string, T?> parser)
    {
        var value = GetValues(label, false).FirstOrDefault();

        if (value != null)
        {
            return parser(value) ?? default;
        }

        return default;
    }

    private void SetSingleValue<T>(string label, T? value, Func<T, string?> serializer)
    {
        IEnumerable<string> values = [];

        if (value != null)
        {
            var serialized = serializer(value);

            if (serialized != null)
            {
                values = [serialized];
            }
        }

        SetValues(label, values, false);
    }

    public IEnumerable<string> GetCustomValues(string customLabel) => GetValues(customLabel, true);

    private IEnumerable<string> GetValues(string label, bool excludeReserved)
    {
        string key = label.ToUpperInvariant();

        if (excludeReserved &&
            reservedLabels.Contains(key))
        {
            return [];
        }

        if (items.TryGetValue(key, out var value))
        {
            return value.Select(i => i.Value);
        }

        return [];
    }

    public void SetCustomValues(string customLabel, IEnumerable<string> values) => SetValues(customLabel, values, true);

    private void SetValues(string label, IEnumerable<string> values, bool excludeReserved)
    {
        string key = label.ToUpperInvariant();

        if (excludeReserved &&
            reservedLabels.Contains(key))
        {
            return;
        }

        var valuesToStore = values
            .Select(v => new BagItInfoItem(label, v))
            .ToList();

        if (valuesToStore.Count == 0)
        {
            items.Remove(key);
        }
        else
        {
            items[key] = valuesToStore;
        }
    }

    public static string FileName => "bag-info.txt";

    public static async Task<BagItInfo> ParseAsync(Stream stream, CancellationToken cancellationToken)
    {
        var result = new BagItInfo();

        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        string? line;
        string value = "";
        string label = "";

        void AddItemIfNotEmpty()
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            var item = new BagItInfoItem(label, value);
            string key = label.ToUpperInvariant();

            if (result.items.TryGetValue(key, out var existing))
            {
                existing.Add(item);
            }
            else
            {
                result.items[key] = [item];
            }
        }

        while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(cancellationToken)))
        {
            if (line.StartsWith(' ') || line.StartsWith('\t'))
            {
                // value is continued from previous line
                value += ' ' + line.TrimStart();
            }
            else
            {
                AddItemIfNotEmpty();

                int index = line.IndexOf(": ", StringComparison.Ordinal);
                label = line[..index];
                value = line[(index + 2)..];
            }
        }

        // Add last item
        AddItemIfNotEmpty();

        return result;
    }

    public byte[] Serialize()
    {
        var builder = new StringBuilder();

        foreach (var list in items.Values)
        {
            foreach (var item in list)
            {
                builder.Append(item.Label);
                builder.Append(": ");
                builder.Append(item.Value);
                builder.Append('\n');
            }
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    public bool HasValues() => items.Count > 0;
}
