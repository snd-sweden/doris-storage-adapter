using System;

namespace DorisStorageAdapter.Services.Implementation.Services.Validation;

internal static class PathValidation
{
    public static bool IsValidComponent(string value) =>
        !string.IsNullOrEmpty(value) &&
        !value.Contains('/', StringComparison.Ordinal) &&
        value is not "." and not "..";

    public static bool HasOnlyValidComponents(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        foreach (string component in path.Split('/'))
        {
            if (!IsValidComponent(component))
            {
                return false;
            }
        }

        return true;
    }
}
