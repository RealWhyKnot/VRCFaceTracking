using System.Text;
using Newtonsoft.Json;
using VRCFaceTracking.Core.Contracts.Services;

namespace VRCFaceTracking.Core.Services;

public sealed class FileService : IFileService
{
    public T? Read<T>(string folderPath, string fileName)
    {
        string path = Path.Combine(folderPath, fileName);
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<T>(json);
        }

        return default;
    }

    public async Task SaveAsync<T>(string folderPath, string fileName, T content, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        string fileContent = JsonConvert.SerializeObject(content);
        string path = Path.Combine(folderPath, fileName);
        string tempPath = path + ".tmp";
        await File.WriteAllTextAsync(tempPath, fileContent, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        if (File.Exists(path))
        {
            File.Replace(tempPath, path, destinationBackupFileName: null);
        }
        else
        {
            File.Move(tempPath, path);
        }
    }

    public void Delete(string folderPath, string fileName)
    {
        if (fileName != null && File.Exists(Path.Combine(folderPath, fileName)))
        {
            File.Delete(Path.Combine(folderPath, fileName));
        }
    }
}
