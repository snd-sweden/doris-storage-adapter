namespace DorisStorageAdapter.BagIt;

public interface IBagItElement
{
    bool HasValues();

    byte[] Serialize();
}
