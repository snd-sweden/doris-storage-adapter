using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DorisStorageAdapter.BagIt.Info;

public sealed class BagItInfo : IBagItElement<BagItInfo>
{
    private readonly SortedDictionary<string, List<BagItInfoItem>> _items = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> _reservedLabels = new(
    [
        BaggingDateLabel,
        BagCountLabel,
        BagGroupIdentifierLabel,
        BagSizeLabel,
        ContactEmailLabel,
        ContactNameLabel,
        ContactPhoneLabel,
        ExternalDescriptionLabel,
        ExternalIdentifierLabel,
        InternalSenderIdentifierLabel,
        InternalSenderDescriptionLabel,
        OrganizationAddressLabel,
        SourceOrganizationLabel,
        PayloadOxumLabel
    ],
    StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> _nonRepeatableLabels = new(
    [
        BaggingDateLabel,
        BagCountLabel,
        BagGroupIdentifierLabel,
        BagSizeLabel,
        PayloadOxumLabel
    ],
    StringComparer.OrdinalIgnoreCase);

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

    public DateOnly? BaggingDate
    {
        get;
        set
        {
            SetSingleValue(BaggingDateLabel, value,
                v => v?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            field = value;
        }
    }

    public string? BagGroupIdentifier
    {
        get => GetSingleValue(BagGroupIdentifierLabel, v => v);
        set => SetSingleValue(BagGroupIdentifierLabel, value, v => v);
    }

    public BagCount? BagCount
    {
        get;
        set
        {
            SetSingleValue(BagCountLabel, value,
                v => v.Ordinal.ToString(CultureInfo.InvariantCulture) + " of " +
                    (v.TotalCount?.ToString(CultureInfo.InvariantCulture) ?? "?"));

            field = value;
        }
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
        get;

        set
        {
            SetSingleValue(PayloadOxumLabel, value,
                v => v.OctetCount.ToString(CultureInfo.InvariantCulture) + '.' +
                    v.StreamCount.ToString(CultureInfo.InvariantCulture));

            field = value;
        }
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

    public IEnumerable<string> GetCustomValues(string customLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customLabel);

        return GetValues(customLabel, true);
    }

    private IEnumerable<string> GetValues(string label, bool excludeReserved)
    {
        if (excludeReserved &&
            _reservedLabels.Contains(label))
        {
            return [];
        }

        if (_items.TryGetValue(label, out var value))
        {
            return value.Select(i => i.Value);
        }

        return [];
    }

    public void SetCustomValues(string customLabel, IEnumerable<string> values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customLabel);
        ArgumentNullException.ThrowIfNull(values);

        SetValues(customLabel, values, true);
    }

    private void SetValues(string label, IEnumerable<string> values, bool excludeReserved)
    {
        if (excludeReserved &&
            _reservedLabels.Contains(label))
        {
            return;
        }

        var valuesToStore = values
            .Select(v => new BagItInfoItem(label, v))
            .ToList();

        if (valuesToStore.Count == 0)
        {
            _items.Remove(label);
        }
        else
        {
            _items[label] = valuesToStore;
        }
    }

    public static BagItInfo CreateEmpty() => new();

    public static string FileName => "bag-info.txt";

    public static async Task<BagItInfo> ParseAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var result = new BagItInfo();

        using var reader = BagItParsing.CreateReader(stream);

        string? line;
        string? label = null;
        string? value = null;
        int labelLineNumber = 0;
        int lineNumber = 0;
        int? firstEmptyLineNumber = null;

        static void ThrowParseException(int lineNumber, string message) =>
            throw new BagItParseException($"Invalid bag-info element at line {lineNumber}: {message}");

        void AddItem()
        {
            if (label == null)
            {
                return;
            }

            if (value!.Length == 0)
            {
                ThrowParseException(labelLineNumber, $"Value for '{label}' is empty.");
            }

            if (_nonRepeatableLabels.Contains(label) &&
                result._items.ContainsKey(label))
            {
                ThrowParseException(labelLineNumber, $"'{label}' is not repeatable.");
            }

            if (!ParseNonStringProperties(label, value, labelLineNumber))
            {
                var item = new BagItInfoItem(label, value);

                if (result._items.TryGetValue(label, out var existing))
                {
                    existing.Add(item);
                }
                else
                {
                    result._items[label] = [item];
                }
            }

            label = null;
            value = null;
            labelLineNumber = 0;
        }

        bool ParseNonStringProperties(string label, string value, int lineNumber)
        {
            if (string.Equals(label, BaggingDateLabel, StringComparison.OrdinalIgnoreCase))
            {
                if (DateOnly.TryParseExact(
                        value,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var date))
                {
                    result.BaggingDate = date;
                }
                else
                {
                    ThrowParseException(lineNumber, $"{BaggingDateLabel} must use format 'yyyy-MM-dd'.");
                }
            }
            else if (string.Equals(label, BagCountLabel, StringComparison.OrdinalIgnoreCase))
            {
                BagCount? bagCount = null;
                var values = value.Split(" of ");

                if (values.Length == 2 &&
                    long.TryParse(values[0], out long ordinal) &&
                    ordinal >= 0)
                {
                    if (long.TryParse(values[1], out long totalCount) &&
                        totalCount >= 0)
                    {
                        bagCount = new(ordinal, totalCount);
                    }
                    else if (values[1].Trim() == "?")
                    {
                        bagCount = new(ordinal, null);
                    }
                }

                if (bagCount != null)
                {
                    result.BagCount = bagCount;
                }
                else
                {
                    ThrowParseException(lineNumber, $"{BagCountLabel} has invalid format.");
                }
            }
            else if (string.Equals(label, PayloadOxumLabel, StringComparison.OrdinalIgnoreCase))
            {
                var values = value.Split('.');
                if (values.Length == 2 &&
                    long.TryParse(values[0], out long octetCount) &&
                    long.TryParse(values[1], out long streamCount) &&
                    octetCount >= 0 &&
                    streamCount >= 0)
                {
                    result.PayloadOxum = new(octetCount, streamCount);
                }
                else
                {
                    ThrowParseException(lineNumber, $"{PayloadOxumLabel} has invalid format.");
                }
            }
            else
            {
                return false;
            }

            return true;
        }

        while ((line = await BagItParsing.ReadLineOrThrowAsync(
            reader,
            lineNumber + 1,
            cancellationToken)) != null)
        {
            lineNumber++;

            if (line.Length == 0)
            {
                firstEmptyLineNumber ??= lineNumber;
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                ThrowParseException(lineNumber, "Lines containing only whitespace are not allowed.");
            }

            if (firstEmptyLineNumber is not null)
            {
                ThrowParseException(
                    firstEmptyLineNumber.Value,
                    "Empty lines are only allowed at the end of the file.");
            }

            if (line[0] is ' ' or '\t')
            {
                if (label is null)
                {
                    ThrowParseException(lineNumber, "Continuation line without a preceding field.");
                }

                value += line.TrimStart(' ', '\t');
                continue;
            }

            AddItem();

            int colonIndex = line.IndexOf(':', StringComparison.Ordinal);
            if (colonIndex <= 0)
            {
                ThrowParseException(lineNumber, "Expected '<label>: <value>'.");
            }

            if (colonIndex + 1 == line.Length || line[colonIndex + 1] is not (' ' or '\t'))
            {
                ThrowParseException(lineNumber, "Expected exactly one space or tab after ':'.");
            }

            int valueStart = colonIndex + 2;
            if (valueStart == line.Length)
            {
                ThrowParseException(lineNumber, "Value is empty.");
            }

            label = line[..colonIndex];
            value = line[valueStart..];
            labelLineNumber = lineNumber;
        }

        AddItem();

        if (!result.HasValues())
        {
            throw new BagItParseException("Invalid bag-info file: File contains no elements.");
        }

        return result;
    }

    public byte[] Serialize()
    {
        var builder = new StringBuilder();

        foreach (var list in _items.Values)
        {
            foreach (var item in list)
            {
                builder
                    .Append(item.Label)
                    .Append(": ")
                    .Append(item.Value)
                    .Append('\n');
            }
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    public bool HasValues() => _items.Count > 0;
}
