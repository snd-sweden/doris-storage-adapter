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
        _baggingDateLabel.ToUpperInvariant(),
        _bagCountLabel.ToUpperInvariant(),
        _bagGroupIdentifierLabel.ToUpperInvariant(),
        _bagSizeLabel.ToUpperInvariant(),
        _contactEmailLabel.ToUpperInvariant(),
        _contactNameLabel.ToUpperInvariant(),
        _contactPhoneLabel.ToUpperInvariant(),
        _externalDescriptionLabel.ToUpperInvariant(),
        _externalIdentifierLabel.ToUpperInvariant(),
        _internalSenderIdentifier.ToUpperInvariant(),
        _internalSenderDescription.ToUpperInvariant(),
        _organizationAddressLabel.ToUpperInvariant(),
        _sourceOrganizationLabel.ToUpperInvariant(),
        _payloadOxumLabel.ToUpperInvariant()
    ]);

    private const string _baggingDateLabel = "Bagging-Date";
    private const string _bagCountLabel = "Bag-Count";
    private const string _bagGroupIdentifierLabel = "Bag-Group-Identifier";
    private const string _bagSizeLabel = "Bag-Size";
    private const string _contactEmailLabel = "Contact-Email";
    private const string _contactNameLabel = "Contact-Name";
    private const string _contactPhoneLabel = "Contact-Phone";
    private const string _externalDescriptionLabel = "External-Description";
    private const string _externalIdentifierLabel = "External-Identifier";
    private const string _internalSenderIdentifier = "Internal-Sender-Identifier";
    private const string _internalSenderDescription = "Internal-Sender-Description";
    private const string _organizationAddressLabel = "Organization-Address";
    private const string _sourceOrganizationLabel = "Source-Organization";
    private const string _payloadOxumLabel = "Payload-Oxum";

    public DateTime? BaggingDate
    {
        get => GetSingleValue(_baggingDateLabel, v =>
            DateTime.TryParseExact(v,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var dateTime)
                    ? dateTime
                    : (DateTime?)null);

        set => SetSingleValue(_baggingDateLabel, value,
            v => v?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    public string? BagGroupIdentifier
    {
        get => GetSingleValue(_bagGroupIdentifierLabel, v => v);
        set => SetSingleValue(_bagGroupIdentifierLabel, value, v => v);
    }

    public BagCount? BagCount
    {
        get => GetSingleValue(_bagCountLabel, v =>
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

        set => SetSingleValue(_bagCountLabel, value,
            v => v.Ordinal.ToString(CultureInfo.InvariantCulture) + " of " +
                 v.TotalCount?.ToString(CultureInfo.InvariantCulture) ?? "?");
    }

    public string? BagSize
    {
        get => GetSingleValue(_bagSizeLabel, v => v);
        set => SetSingleValue(_bagSizeLabel, value, v => v);
    }

    public IEnumerable<string> ContactEmail
    {
        get => GetValues(_contactEmailLabel, false);
        set => SetValues(_contactEmailLabel, value, false);
    }

    public IEnumerable<string> ContactName
    {
        get => GetValues(_contactNameLabel, false);
        set => SetValues(_contactNameLabel, value, false);
    }

    public IEnumerable<string> ContactPhone
    {
        get => GetValues(_contactPhoneLabel, false);
        set => SetValues(_contactPhoneLabel, value, false);
    }

    public IEnumerable<string> ExternalDescription
    {
        get => GetValues(_externalDescriptionLabel, false);
        set => SetValues(_externalDescriptionLabel, value, false);
    }

    public IEnumerable<string> ExternalIdentifier
    {
        get => GetValues(_externalIdentifierLabel, false);
        set => SetValues(_externalIdentifierLabel, value, false);
    }

    public IEnumerable<string> InternalSenderDescription
    {
        get => GetValues(_internalSenderDescription, false);
        set => SetValues(_internalSenderDescription, value, false);
    }

    public IEnumerable<string> InternalSenderIdentifier
    {
        get => GetValues(_internalSenderIdentifier, false);
        set => SetValues(_internalSenderIdentifier, value, false);
    }

    public IEnumerable<string> OrganizationAddress
    {
        get => GetValues(_organizationAddressLabel, false);
        set => SetValues(_organizationAddressLabel, value, false);
    }

    public PayloadOxum? PayloadOxum
    {
        get => GetSingleValue(_payloadOxumLabel, v =>
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

        set => SetSingleValue(_payloadOxumLabel, value,
            v => v.OctetCount.ToString(CultureInfo.InvariantCulture) + '.' +
                 v.StreamCount.ToString(CultureInfo.InvariantCulture));
    }

    public IEnumerable<string> SourceOrganization
    {
        get => GetValues(_sourceOrganizationLabel, false);
        set => SetValues(_sourceOrganizationLabel, value, false);
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
