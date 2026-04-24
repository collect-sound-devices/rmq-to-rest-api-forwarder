namespace RmqToRestApiForwarder.Contracts;

public static class MessageFields
{
    public const string HttpRequest = "httpRequest";
    public const string UrlSuffix = "urlSuffix";
    public const string DeviceMessageType = "deviceMessageType";
    public const string UpdateDate = "updateDate";

    public enum DeviceEventType : byte
    {
        // ReSharper disable UnusedMember.Global
        Confirmed = 0,
        Discovered = 1,
        Detached = 2,
        VolumeRenderChanged = 3,
        VolumeCaptureChanged = 4,
        DefaultRenderChanged = 5,
        DefaultCaptureChanged = 6
        // ReSharper restore UnusedMember.Global
    }

}
