namespace VRCFaceTracking.Contracts;

public interface INotifyNavigated
{
    void OnNavigatedTo();

    void OnNavigatedFrom()
    {
    }
}