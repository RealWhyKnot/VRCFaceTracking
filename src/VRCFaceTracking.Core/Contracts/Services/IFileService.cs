namespace VRCFaceTracking.Core.Contracts.Services;

public interface IFileService
{
    T? Read<T>(string folderPath, string fileName);

    Task SaveAsync<T>(string folderPath, string fileName, T content, CancellationToken cancellationToken);

    void Delete(string folderPath, string fileName);
}
