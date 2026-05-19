namespace TheAdventure;

//excepție specifică pentru erori de save sau load 
public sealed class SaveDataException : Exception
{
    public SaveDataException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
