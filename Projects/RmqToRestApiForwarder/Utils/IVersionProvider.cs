namespace RmqToRestApiForwarder.Utils;

internal interface IVersionProvider
{
    string AppName { get; }
    string Runtime { get; }
    string CodeVersion { get; }
    string LastCommitDate { get; }
}
