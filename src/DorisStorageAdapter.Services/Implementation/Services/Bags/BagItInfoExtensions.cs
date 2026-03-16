using DorisStorageAdapter.Services.Contract.Models;
using System.Linq;
using DorisStorageAdapter.Services.Implementation.BagIt.Info;

namespace DorisStorageAdapter.Services.Implementation.Services.Bags;

internal static class BagItInfoExtensions
{
    private const string _accessRightLabel = "Access-Right";
    private const string _datasetStatusLabel = "Dataset-Status";
    private const string _versionLabel = "Version";

    // http://publications.europa.eu/resource/authority/access-right/PUBLIC
    private const string _publicAccessRightValue = "PUBLIC";
    // http://publications.europa.eu/resource/authority/access-right/NON_PUBLIC
    private const string _nonPublicAccessRightValue = "NON_PUBLIC";

    // http://publications.europa.eu/resource/authority/dataset-status/COMPLETED
    private const string _completedDatasetStatusValue = "COMPLETED";
    // http://publications.europa.eu/resource/authority/dataset-status/WITHDRAWN
    private const string _withdrawnDatasetStatusValue = "WITHDRAWN";

    public static AccessRight? GetAccessRight(this BagItInfo bagItInfo) =>
        bagItInfo.GetCustomValues(_accessRightLabel).FirstOrDefault() switch
        {
            _publicAccessRightValue => AccessRight.@public,
            _nonPublicAccessRightValue => AccessRight.nonPublic,
            _ => null
        };

    public static void SetAccessRight(this BagItInfo bagItInfo, AccessRight? accessRight) =>
        bagItInfo.SetCustomValues(_accessRightLabel, accessRight switch
        {
            AccessRight.@public => [_publicAccessRightValue],
            AccessRight.nonPublic => [_nonPublicAccessRightValue],
            _ => []
        });


    public static DatasetVersionStatus? GetDatasetVersionStatus(this BagItInfo bagItInfo) =>
        bagItInfo.GetCustomValues(_datasetStatusLabel).FirstOrDefault() switch
        {
            _completedDatasetStatusValue => DatasetVersionStatus.published,
            _withdrawnDatasetStatusValue => DatasetVersionStatus.withdrawn,
            _ => null
        };

    public static void SetDatasetVersionStatus(this BagItInfo bagItInfo, DatasetVersionStatus? status) =>
        bagItInfo.SetCustomValues(_datasetStatusLabel, status switch
        {
            DatasetVersionStatus.published => [_completedDatasetStatusValue],
            DatasetVersionStatus.withdrawn => [_withdrawnDatasetStatusValue],
            _ => []
        });

    public static string? GetVersion(this BagItInfo bagItInfo) =>
        bagItInfo.GetCustomValues(_versionLabel).FirstOrDefault();

    public static void SetVersion(this BagItInfo bagItInfo, string? version) =>
       bagItInfo.SetCustomValues(_versionLabel, version == null ? [] : [version]);
}
