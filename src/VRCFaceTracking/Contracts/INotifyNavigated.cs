namespace VRCFaceTracking.Contracts;

internal interface INotifyNavigated
{
    void OnNavigatedTo();

    void OnNavigatedFrom()
    {
    }
}
