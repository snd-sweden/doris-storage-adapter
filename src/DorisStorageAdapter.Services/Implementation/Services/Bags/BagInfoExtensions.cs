using DorisStorageAdapter.Services.Contract.Models;
using System.Linq;
using DorisStorageAdapter.Services.Implementation.BagIt.Info;

namespace DorisStorageAdapter.Services.Implementation.Services.Bags;

internal static class BagInfoExtensions
{
    private const string AccessRightLabel = "Access-Right";
    private const string DatasetStatusLabel = "Dataset-Status";
    private const string VersionLabel = "Version";

    private const string PublicAccessRightValue = "http://publications.europa.eu/resource/authority/access-right/PUBLIC";
    private const string NonPublicAccessRightValue = "http://publications.europa.eu/resource/authority/access-right/NON_PUBLIC";

    private const string CompletedDatasetStatusValue = "http://publications.europa.eu/resource/authority/dataset-status/COMPLETED";
    private const string WithdrawnDatasetStatusValue = "http://publications.europa.eu/resource/authority/dataset-status/WITHDRAWN";

    public static AccessRight? GetAccessRight(this BagItInfo bagItInfo) =>
        bagItInfo.GetCustomValues(AccessRightLabel).FirstOrDefault() switch
        {
            PublicAccessRightValue => AccessRight.Public,
            NonPublicAccessRightValue => AccessRight.NonPublic,
            _ => null
        };

    public static void SetAccessRight(this BagItInfo bagItInfo, AccessRight? accessRight) =>
        bagItInfo.SetCustomValues(AccessRightLabel, accessRight switch
        {
            AccessRight.Public => [PublicAccessRightValue],
            AccessRight.NonPublic => [NonPublicAccessRightValue],
            _ => []
        });


    public static DatasetVersionStatus? GetDatasetVersionStatus(this BagItInfo bagItInfo) =>
        bagItInfo.GetCustomValues(DatasetStatusLabel).FirstOrDefault() switch
        {
            CompletedDatasetStatusValue => DatasetVersionStatus.Published,
            WithdrawnDatasetStatusValue => DatasetVersionStatus.Withdrawn,
            _ => null
        };

    public static void SetDatasetVersionStatus(this BagItInfo bagItInfo, DatasetVersionStatus? status) =>
        bagItInfo.SetCustomValues(DatasetStatusLabel, status switch
        {
            DatasetVersionStatus.Published => [CompletedDatasetStatusValue],
            DatasetVersionStatus.Withdrawn => [WithdrawnDatasetStatusValue],
            _ => []
        });

    public static string? GetVersion(this BagItInfo bagItInfo) =>
        bagItInfo.GetCustomValues(VersionLabel).FirstOrDefault();

    public static void SetVersion(this BagItInfo bagItInfo, string? version) =>
       bagItInfo.SetCustomValues(VersionLabel, version == null ? [] : [version]);
}
