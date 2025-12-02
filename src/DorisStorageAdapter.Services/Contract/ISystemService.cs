using DorisStorageAdapter.Services.Contract.Models;

namespace DorisStorageAdapter.Services.Contract;

public interface ISystemService
{
    SystemInformation GetSystemInformation();
}
