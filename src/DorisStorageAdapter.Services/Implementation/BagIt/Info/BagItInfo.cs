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
    private readonly SortedDictionary<string, List<BagItInfoItem>> _items = new(StringComparer.Ordinal);

    private static readonly HashSet<string> _reservedLabels = new(
    [
        BaggingDateLabel.ToUpperInvariant(),
        BagCountLabel.ToUpperInvariant(),
        BagGroupIdentifierLabel.ToUpperInvariant(),
        BagSizeLabel.ToUpperInvariant(),
        ContactEmailLabel.ToUpperInvariant(),
        ContactNameLabel.ToUpperInvariant(),
        ContactPhoneLabel.ToUpperInvariant(),
        ExternalDescriptionLabel.ToUpperInvariant(),
        ExternalIdentifierLabel.ToUpperInvariant(),
        InternalSenderIdentifierLabel.ToUpperInvariant(),
        InternalSenderDescriptionLabel.ToUpperInvariant(),
        OrganizationAddressLabel.ToUpperInvariant(),
        SourceOrganizationLabel.ToUpperInvariant(),
        PayloadOxumLabel.ToUpperInvariant()
    ]);

    private const string BaggingDateLabel = "Bagging-Date";
    private const string BagCountLabel = "Bag-Count";
    private const string BagGroupIdentifierLabel = "Bag-Group-Identifier";
    private const string BagSizeLabel = "Bag-Size";
    private const string ContactEmailLabel = "Contact-Email";
    private const string ContactNameLabel = "Contact-Name";
    private const string ContactPhoneLabel = "Contact-Phone";
    private const string ExternalDescriptionLabel = "External-Description";
    private const string ExternalIdentifierLabel = "External-Identifier";
    private const string InternalSenderIdentifierLabel = "Internal-Sender-Identifier";
    private const string InternalSenderDescriptionLabel = "Internal-Sender-Description";
    private const string OrganizationAddressLabel = "Organization-Address";
    private const string SourceOrganizationLabel = "Source-Organization";
    private const string PayloadOxumLabel = "Payload-Oxum";

    public DateTime? BaggingDate
    {
        get => GetSingleValue(BaggingDateLabel, v =>
            DateTime.TryParseExact(v,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var dateTime)
                    ? dateTime
                    : (DateTime?)null);

        set => SetSingleValue(BaggingDateLabel, value,
            v => v?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    public string? BagGroupIdentifier
    {
        get => GetSingleValue(BagGroupIdentifierLabel, v => v);
        set => SetSingleValue(BagGroupIdentifierLabel, value, v => v);
    }

    public BagCount? BagCount
    {
        get => GetSingleValue(BagCountLabel, v =>
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

        set => SetSingleValue(BagCountLabel, value,
            v => v.Ordinal.ToString(CultureInfo.InvariantCulture) + " of " +
                 v.TotalCount?.ToString(CultureInfo.InvariantCulture) ?? "?");
    }

    public string? BagSize
    {
        get => GetSingleValue(BagSizeLabel, v => v);
        set => SetSingleValue(BagSizeLabel, value, v => v);
    }

    public IEnumerable<string> ContactEmail
    {
        get => GetValues(ContactEmailLabel, false);
        set => SetValues(ContactEmailLabel, value, false);
    }

    public IEnumerable<string> ContactName
    {
        get => GetValues(ContactNameLabel, false);
        set => SetValues(ContactNameLabel, value, false);
    }

    public IEnumerable<string> ContactPhone
    {
        get => GetValues(ContactPhoneLabel, false);
        set => SetValues(ContactPhoneLabel, value, false);
    }

    public IEnumerable<string> ExternalDescription
    {
        get => GetValues(ExternalDescriptionLabel, false);
        set => SetValues(ExternalDescriptionLabel, value, false);
    }

    public IEnumerable<string> ExternalIdentifier
    {
        get => GetValues(ExternalIdentifierLabel, false);
        set => SetValues(ExternalIdentifierLabel, value, false);
    }

    public IEnumerable<string> InternalSenderDescription
    {
        get => GetValues(InternalSenderDescriptionLabel, false);
        set => SetValues(InternalSenderDescriptionLabel, value, false);
    }

    public IEnumerable<string> InternalSenderIdentifier
    {
        get => GetValues(InternalSenderIdentifierLabel, false);
        set => SetValues(InternalSenderIdentifierLabel, value, false);
    }

    public IEnumerable<string> OrganizationAddress
    {
        get => GetValues(OrganizationAddressLabel, false);
        set => SetValues(OrganizationAddressLabel, value, false);
    }

    public PayloadOxum? PayloadOxum
    {
        get => GetSingleValue(PayloadOxumLabel, v =>
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

        set => SetSingleValue(PayloadOxumLabel, value,
            v => v.OctetCount.ToString(CultureInfo.InvariantCulture) + '.' +
                 v.StreamCount.ToString(CultureInfo.InvariantCulture));
    }

    public IEnumerable<string> SourceOrganization
    {
        get => GetValues(SourceOrganizationLabel, false);
        set => SetValues(SourceOrganizationLabel, value, false);
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
            _reservedLabels.Contains(key))
        {
            return [];
        }

        if (_items.TryGetValue(key, out var value))
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
            _reservedLabels.Contains(key))
        {
            return;
        }

        var valuesToStore = values
            .Select(v => new BagItInfoItem(label, v))
            .ToList();

        if (valuesToStore.Count == 0)
        {
            _items.Remove(key);
        }
        else
        {
            _items[key] = valuesToStore;
        }
    }

    public static BagItInfo CreateEmpty() => new();

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

            if (result._items.TryGetValue(key, out var existing))
            {
                existing.Add(item);
            }
            else
            {
                result._items[key] = [item];
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

        foreach (var list in _items.Values)
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

    public bool HasValues() => _items.Count > 0;
}
